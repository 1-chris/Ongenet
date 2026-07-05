using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ongenet.Au.Interop;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Au;

/// <summary>
/// Shared host for a single Audio Unit instance: component lookup + instantiation, the canonical
/// (non-interleaved float) stream format, parameter bridging, MIDI dispatch, the pull-model
/// <see cref="AudioUnitApi.AudioUnitRender"/> audio bridge, and the plugin's Cocoa GUI
/// (<see cref="IPluginEditor"/>, in the partial <c>AuPluginBase.Editor.cs</c>). Subclasses specialise
/// the audio I/O — <see cref="AuInstrument"/> (notes in, audio out, additive) and <see cref="AuEffect"/>
/// (audio in → audio out, in place). All native failures are caught; a broken plugin simply produces
/// silence / passes audio through. macOS only.
/// </summary>
public abstract unsafe partial class AuPluginBase : IPluginEditor, IDisposable
{
    protected const int MaxBlock = 8192;
    private const int MaxParamsShown = 256;
    private const uint kAudioUnitParameterFlag_CFNameRelease = 0x10;

    /// <summary>Optional diagnostic sink (set once at startup); surfaces host + GUI logs to the app log.</summary>
    public static Action<string>? Log;

    protected readonly uint Type;
    protected readonly uint SubType;
    protected readonly uint Manufacturer;

    private readonly object _midiLock = new();
    private readonly List<MidiMsg> _pendingMidi = new();
    private readonly HashSet<int> _held = new();

    private IntPtr _unit;
    private GCHandle _selfHandle;

    // Native scratch (allocated in Prepare, sized to the bus channel count).
    private int _busChannels = 2;
    private float** _outData;    // per-channel output scratch (deinterleaved)
    private float** _inData;     // per-channel input scratch (effects only)
    private byte* _outList;      // AudioBufferList handed to AudioUnitRender
    private volatile int _renderFrames;

    private AudioFormat _format = AudioFormat.Default;
    private double _sampleTime;
    private bool _loadAttempted;
    private bool _loaded;
    private bool _initialized;
    private double _initializedRate;
    private bool _disposed;

    private IReadOnlyList<Parameter>? _parameters;

    protected AuPluginBase(uint type, uint subType, uint manufacturer, string displayName)
    {
        Type = type;
        SubType = subType;
        Manufacturer = manufacturer;
        Name = displayName;
    }

    /// <summary>Whether this host feeds audio input into the unit (effects) vs. none (instruments).</summary>
    protected abstract bool FeedsInput { get; }

    /// <summary>
    /// The composite registry id for an Audio Unit:
    /// <c>au:&lt;type&gt;-&lt;subType&gt;-&lt;manufacturer&gt;</c> (each is the 8-hex four-char code).
    /// </summary>
    public static string MakeId(uint type, uint subType, uint manufacturer) =>
        $"au:{type.ToString("x8", CultureInfo.InvariantCulture)}-{subType.ToString("x8", CultureInfo.InvariantCulture)}-{manufacturer.ToString("x8", CultureInfo.InvariantCulture)}";

    public string Name { get; }

    public IReadOnlyList<Parameter> Parameters
    {
        get { EnsureLoaded(); return _parameters ?? Array.Empty<Parameter>(); }
    }

    // --- Loading / configuration ---

    protected bool EnsureLoaded()
    {
        if (_loaded) return true;
        if (_loadAttempted) return false;
        _loadAttempted = true;

        if (!OperatingSystem.IsMacOS()) return false;

        try
        {
            var desc = new AudioUnitApi.AudioComponentDescription
            {
                componentType = Type,
                componentSubType = SubType,
                componentManufacturer = Manufacturer,
            };

            var comp = AudioUnitApi.AudioComponentFindNext(IntPtr.Zero, ref desc);
            if (comp == IntPtr.Zero) throw new InvalidOperationException("component not found.");
            if (AudioUnitApi.AudioComponentInstanceNew(comp, out _unit) != 0 || _unit == IntPtr.Zero)
                throw new InvalidOperationException("AudioComponentInstanceNew failed.");

            _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);
            BuildParameters();
            DetectEditor();

            _loaded = true;
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"AU '{Name}': load failed: {ex.Message}");
            TeardownNative();
            return false;
        }
    }

    public void Prepare(AudioFormat format)
    {
        _format = format;
        if (!EnsureLoaded() || _unit == IntPtr.Zero) return;

        var channels = format.Channels < 1 ? 1 : format.Channels;
        if (_initialized && Math.Abs(_initializedRate - format.SampleRate) < 0.5 && channels == _busChannels) return;

        try
        {
            if (_initialized) { AudioUnitApi.AudioUnitUninitialize(_unit); _initialized = false; }

            AllocateScratch(channels);

            var asbd = CanonicalFormat(format.SampleRate, channels);
            var size = (uint)sizeof(AudioUnitApi.AudioStreamBasicDescription);
            AudioUnitApi.AudioUnitSetProperty(_unit, AudioUnitApi.kAudioUnitProperty_StreamFormat,
                AudioUnitApi.kAudioUnitScope_Output, 0, &asbd, size);

            if (FeedsInput)
            {
                AudioUnitApi.AudioUnitSetProperty(_unit, AudioUnitApi.kAudioUnitProperty_StreamFormat,
                    AudioUnitApi.kAudioUnitScope_Input, 0, &asbd, size);
                InstallInputCallback();
            }

            uint maxFrames = MaxBlock;
            AudioUnitApi.AudioUnitSetProperty(_unit, AudioUnitApi.kAudioUnitProperty_MaximumFramesPerSlice,
                AudioUnitApi.kAudioUnitScope_Global, 0, &maxFrames, sizeof(uint));

            if (AudioUnitApi.AudioUnitInitialize(_unit) == 0)
            {
                _initialized = true;
                _initializedRate = format.SampleRate;
                _sampleTime = 0;
            }
            else
            {
                Log?.Invoke($"AU '{Name}': AudioUnitInitialize failed.");
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"AU '{Name}': prepare failed: {ex.Message}");
        }
    }

    private static AudioUnitApi.AudioStreamBasicDescription CanonicalFormat(int sampleRate, int channels) => new()
    {
        mSampleRate = sampleRate,
        mFormatID = AudioUnitApi.kAudioFormatLinearPCM,
        mFormatFlags = AudioUnitApi.kAudioFormatFlagIsFloat | AudioUnitApi.kAudioFormatFlagIsPacked
                       | AudioUnitApi.kAudioFormatFlagIsNonInterleaved,
        mBytesPerPacket = 4,
        mFramesPerPacket = 1,
        mBytesPerFrame = 4,
        mChannelsPerFrame = (uint)channels,
        mBitsPerChannel = 32,
    };

    private void InstallInputCallback()
    {
        delegate* unmanaged[Cdecl]<IntPtr, uint*, AudioUnitApi.AudioTimeStamp*, uint, uint, void*, int> fp = &InputRenderCallback;
        var cb = new AudioUnitApi.AURenderCallbackStruct
        {
            inputProc = (IntPtr)fp,
            inputProcRefCon = GCHandle.ToIntPtr(_selfHandle),
        };
        AudioUnitApi.AudioUnitSetProperty(_unit, AudioUnitApi.kAudioUnitProperty_SetRenderCallback,
            AudioUnitApi.kAudioUnitScope_Input, 0, &cb, (uint)sizeof(AudioUnitApi.AURenderCallbackStruct));
    }

    // --- Parameters ---

    private void BuildParameters()
    {
        _parameters = Array.Empty<Parameter>();
        if (_unit == IntPtr.Zero) return;

        if (AudioUnitApi.AudioUnitGetPropertyInfo(_unit, AudioUnitApi.kAudioUnitProperty_ParameterList,
                AudioUnitApi.kAudioUnitScope_Global, 0, out var size, out _) != 0 || size == 0)
            return;

        var count = Math.Min((int)(size / sizeof(uint)), MaxParamsShown);
        var idsPtr = (uint*)Marshal.AllocHGlobal((nint)size);
        try
        {
            var ioSize = size;
            if (AudioUnitApi.AudioUnitGetProperty(_unit, AudioUnitApi.kAudioUnitProperty_ParameterList,
                    AudioUnitApi.kAudioUnitScope_Global, 0, idsPtr, ref ioSize) != 0)
                return;

            var list = new List<Parameter>();
            for (var i = 0; i < count; i++)
            {
                var id = idsPtr[i];
                AudioUnitApi.AudioUnitParameterInfo info = default;
                var infoSize = (uint)sizeof(AudioUnitApi.AudioUnitParameterInfo);
                if (AudioUnitApi.AudioUnitGetProperty(_unit, AudioUnitApi.kAudioUnitProperty_ParameterInfo,
                        AudioUnitApi.kAudioUnitScope_Global, id, &info, ref infoSize) != 0)
                    continue;

                var name = ParamName(&info) ?? $"Param {i}";
                var min = info.minValue;
                var max = info.maxValue;
                if (max <= min) max = min + 1;

                list.Add(new FloatParameter(name, min, max, () => GetParam(id), v => SetParam(id, (float)v)));
            }

            _parameters = list;
        }
        finally
        {
            Marshal.FreeHGlobal((nint)idsPtr);
        }
    }

    private static string? ParamName(AudioUnitApi.AudioUnitParameterInfo* info)
    {
        if (info->cfNameString != IntPtr.Zero)
        {
            var s = AudioUnitApi.CFStringToManaged(info->cfNameString);
            if ((info->flags & kAudioUnitParameterFlag_CFNameRelease) != 0)
                AudioUnitApi.CFRelease(info->cfNameString);
            if (!string.IsNullOrEmpty(s)) return s;
        }

        var len = 0;
        while (len < 52 && info->name[len] != 0) len++;
        if (len == 0) return null;
        var bytes = new byte[len];
        for (var i = 0; i < len; i++) bytes[i] = info->name[i];
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private double GetParam(uint id)
    {
        if (_unit == IntPtr.Zero) return 0;
        return AudioUnitApi.AudioUnitGetParameter(_unit, id, AudioUnitApi.kAudioUnitScope_Global, 0, out var v) == 0 ? v : 0;
    }

    private void SetParam(uint id, float value)
    {
        if (_unit == IntPtr.Zero) return;
        AudioUnitApi.AudioUnitSetParameter(_unit, id, AudioUnitApi.kAudioUnitScope_Global, 0, value, 0);
    }

    // --- Audio thread ---

    /// <summary>
    /// Renders one block. <paramref name="feedInput"/> de-interleaves the engine buffer into the unit's
    /// audio input (effects, via the render callback); <paramref name="replace"/> overwrites the engine
    /// buffer with the unit output (effects) vs. adding to it (instruments).
    /// </summary>
    protected void RenderAudio(Span<float> buffer, bool feedInput, bool replace)
    {
        if (!_initialized || _unit == IntPtr.Zero || _outData == null || _outList == null) return;

        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;
        if (frames <= 0) return;
        if (frames > MaxBlock) frames = MaxBlock;

        if (feedInput && _inData != null)
        {
            for (var ic = 0; ic < _busChannels; ic++)
            {
                var dst = _inData[ic];
                for (var f = 0; f < frames; f++) dst[f] = ic < channels ? buffer[f * channels + ic] : 0f;
            }

            _renderFrames = frames;
        }

        FlushMidi();

        // Point the output buffer list at our scratch for this block.
        var bufs = (AudioUnitApi.AudioBuffer*)(_outList + 8);
        for (var c = 0; c < _busChannels; c++)
        {
            bufs[c].mNumberChannels = 1;
            bufs[c].mDataByteSize = (uint)(frames * sizeof(float));
            bufs[c].mData = (IntPtr)_outData[c];
        }

        uint flags = 0;
        AudioUnitApi.AudioTimeStamp ts = default;
        ts.mSampleTime = _sampleTime;
        ts.mFlags = AudioUnitApi.kAudioTimeStampSampleTimeValid;

        var status = AudioUnitApi.AudioUnitRender(_unit, &flags, &ts, 0, (uint)frames, _outList);
        _sampleTime += frames;
        if (status != 0) return; // render error — leave the engine buffer untouched (passthrough / silence)

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var pc = c < _busChannels ? c : _busChannels - 1;
                var src = (float*)bufs[pc].mData;
                var v = src != null ? src[frame] : 0f;
                if (replace) buffer[i + c] = v;
                else buffer[i + c] += v;
            }
        }
    }

    // Fills the unit's requested input buffers from our de-interleaved input scratch (effects).
    private void FillInput(void* ioData, uint frames)
    {
        if (_inData == null || ioData == null) return;
        var p = (byte*)ioData;
        var nbuf = *(uint*)p;
        var bufs = (AudioUnitApi.AudioBuffer*)(p + 8);
        var fr = (int)Math.Min(frames, (uint)MaxBlock);

        for (var i = 0u; i < nbuf; i++)
        {
            var src = i < (uint)_busChannels ? _inData[i] : null;
            if (bufs[i].mData != IntPtr.Zero)
            {
                var dst = (float*)bufs[i].mData;
                if (src != null) Buffer.MemoryCopy(src, dst, bufs[i].mDataByteSize, (long)fr * sizeof(float));
                else new Span<float>(dst, fr).Clear();
            }
            else if (src != null)
            {
                bufs[i].mData = (IntPtr)src;
                bufs[i].mDataByteSize = (uint)(fr * sizeof(float));
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int InputRenderCallback(IntPtr inRefCon, uint* ioActionFlags,
        AudioUnitApi.AudioTimeStamp* inTimeStamp, uint inBusNumber, uint inNumberFrames, void* ioData)
    {
        try
        {
            var gch = GCHandle.FromIntPtr(inRefCon);
            if (gch.Target is AuPluginBase self) self.FillInput(ioData, inNumberFrames);
        }
        catch { /* ignore */ }

        return 0;
    }

    // --- MIDI (scheduler / UI threads) ---

    private void FlushMidi()
    {
        lock (_midiLock)
        {
            if (_pendingMidi.Count == 0) return;
            foreach (var m in _pendingMidi)
                AudioUnitApi.MusicDeviceMIDIEvent(_unit, m.Status, m.Data1, m.Data2, 0);
            _pendingMidi.Clear();
        }
    }

    protected void EnqueueNoteOn(int midiNote, float velocity)
    {
        var vel = (uint)Math.Clamp((int)(velocity * 127f + 0.5f), 1, 127);
        lock (_midiLock)
        {
            _pendingMidi.Add(new MidiMsg(0x90, (uint)(midiNote & 0x7F), vel));
            _held.Add(midiNote);
        }
    }

    protected void EnqueueNoteOff(int midiNote)
    {
        lock (_midiLock)
        {
            _pendingMidi.Add(new MidiMsg(0x80, (uint)(midiNote & 0x7F), 0));
            _held.Remove(midiNote);
        }
    }

    protected void EnqueueAllNotesOff()
    {
        lock (_midiLock)
        {
            foreach (var note in _held) _pendingMidi.Add(new MidiMsg(0x80, (uint)(note & 0x7F), 0));
            _held.Clear();
            _pendingMidi.Add(new MidiMsg(0xB0, 123, 0)); // CC#123 All Notes Off
        }
    }

    protected void EnqueueControlChange(int controller, int value)
    {
        lock (_midiLock) _pendingMidi.Add(new MidiMsg(0xB0, (uint)(controller & 0x7F), (uint)(value & 0x7F)));
    }

    protected void EnqueuePitchBend(int value14)
    {
        var v = Math.Clamp(value14, 0, 16383);
        lock (_midiLock) _pendingMidi.Add(new MidiMsg(0xE0, (uint)(v & 0x7F), (uint)((v >> 7) & 0x7F)));
    }

    protected void EnqueueAftertouch(int value)
    {
        lock (_midiLock) _pendingMidi.Add(new MidiMsg(0xD0, (uint)(value & 0x7F), 0));
    }

    // --- Scratch buffers ---

    private void AllocateScratch(int channels)
    {
        FreeScratch();
        _busChannels = channels;
        _outData = AllocChannels(channels);
        if (FeedsInput) _inData = AllocChannels(channels);

        // AudioBufferList: UInt32 count + 4 bytes padding + channels * AudioBuffer.
        var bytes = 8 + channels * sizeof(AudioUnitApi.AudioBuffer);
        _outList = (byte*)Marshal.AllocHGlobal(bytes);
        new Span<byte>(_outList, bytes).Clear();
        *(uint*)_outList = (uint)channels;
    }

    private static float** AllocChannels(int channels)
    {
        var data = (float**)Marshal.AllocHGlobal(channels * sizeof(void*));
        for (var c = 0; c < channels; c++) data[c] = (float*)Marshal.AllocHGlobal(MaxBlock * sizeof(float));
        return data;
    }

    private void FreeScratch()
    {
        FreeChannels(ref _outData, _busChannels);
        FreeChannels(ref _inData, _busChannels);
        if (_outList != null) { Marshal.FreeHGlobal((nint)_outList); _outList = null; }
    }

    private static void FreeChannels(ref float** data, int channels)
    {
        if (data == null) return;
        for (var c = 0; c < channels; c++) if (data[c] != null) Marshal.FreeHGlobal((nint)data[c]);
        Marshal.FreeHGlobal((nint)data);
        data = null;
    }

    // --- Teardown ---

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TeardownNative();
    }

    private void TeardownNative()
    {
        try
        {
            DestroyEditor();
            if (_unit != IntPtr.Zero)
            {
                if (_initialized) AudioUnitApi.AudioUnitUninitialize(_unit);
                AudioUnitApi.AudioComponentInstanceDispose(_unit);
            }
        }
        catch { /* ignore */ }

        _unit = IntPtr.Zero;
        _initialized = false;
        _loaded = false;

        FreeScratch();
        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    private readonly record struct MidiMsg(uint Status, uint Data1, uint Data2);
}
