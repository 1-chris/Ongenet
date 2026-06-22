using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Platform;
using Ongenet.Vst.Vst2.Interop;

namespace Ongenet.Vst.Vst2;

/// <summary>
/// Shared host for a VST2 plugin instance: module loading, the <c>AEffect</c> lifecycle
/// (open → setSampleRate/setBlockSize → resume), parameter bridging (normalised get/set), MIDI delivery
/// via <c>effProcessEvents</c>, the <c>processReplacing</c> audio bridge, and the native editor
/// (<c>effEditOpen</c>) with its idle loop. Subclasses specialise the audio I/O —
/// <see cref="Vst2Instrument"/> (notes in, audio out, additive) and <see cref="Vst2Effect"/> (audio in →
/// audio out, in place). All native failures are caught; a broken plugin produces silence / passes audio
/// through.
/// </summary>
public abstract unsafe class Vst2PluginBase : IPluginEditor, IDisposable
{
    public const int MaxBlock = 8192;
    private const int EventCapacity = 512;
    private const int MaxParamsShown = 512;
    private const int EmbedDefaultW = 800;
    private const int EmbedDefaultH = 600;

    /// <summary>Optional diagnostic sink (set once at startup); surfaces plugin + GUI logs to the app log.</summary>
    public static Action<string>? Log;

    protected readonly string ModulePath;
    protected readonly string Uid;

    private readonly object _evLock = new();
    private readonly List<Pending> _pending = new();
    private readonly HashSet<int> _held = new();

    private Vst2Module? _module;
    private Vst2Api.AEffect* _effect;
    private GCHandle _selfHandle;

    // Native scratch (allocated on load).
    private float** _inData;
    private float** _outData;
    private int _inChannels;
    private int _outChannels;
    private byte* _eventsBlock;      // VstEvents header + pointer array
    private Vst2Api.VstMidiEvent* _midiEvents;
    private Vst2Api.VstTimeInfo* _timeInfo;

    private AudioFormat _format = AudioFormat.Default;
    private bool _loadAttempted;
    private bool _loaded;
    private bool _resumed;
    private double _resumedRate;
    private bool _disposed;

    private bool _hasEditor;
    private bool _editorOpen;
    private int _editorW;
    private int _editorH;

    // Intermediate GL-compatible X11 child we embed the plugin UI into (Linux/X11; see X11Embed).
    private nint _x11Display;
    private nint _embedWindow;

    private IReadOnlyList<Parameter>? _parameters;

    protected Vst2PluginBase(string modulePath, string uid, string displayName)
    {
        ModulePath = modulePath;
        Uid = uid;
        Name = displayName;
    }

    /// <summary>The composite registry id for a VST2 plugin: <c>vst2:&lt;module&gt;|&lt;uid&gt;</c>.</summary>
    public static string MakeId(string modulePath, string uid) => $"vst2:{modulePath}|{uid}";

    public string Name { get; }

    /// <summary>The sample rate the plugin was last prepared for (read by the host callback).</summary>
    internal double SampleRate => _format.SampleRate;

    /// <summary>Pointer to this instance's <see cref="Vst2Api.VstTimeInfo"/> (handed to plugins via audioMaster).</summary>
    internal nint TimeInfo => (nint)_timeInfo;

    public IReadOnlyList<Parameter> Parameters
    {
        get { EnsureLoaded(); return _parameters ?? Array.Empty<Parameter>(); }
    }

    // --- Loading / activation ---

    private bool EnsureLoaded()
    {
        if (_loaded) return true;
        if (_loadAttempted) return false;
        _loadAttempted = true;

        try
        {
            _module = new Vst2Module(ModulePath);
            _effect = _module.Open();
            _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);
            _effect->User = (void*)GCHandle.ToIntPtr(_selfHandle);

            Dispatch(Vst2Api.EffOpen, 0, 0, null, 0);

            _inChannels = Math.Clamp(_effect->NumInputs, 0, 32);
            _outChannels = Math.Clamp(_effect->NumOutputs, 0, 32);
            _hasEditor = (_effect->Flags & Vst2Api.FlagsHasEditor) != 0;

            AllocateScratch();
            BuildParameters();

            _loaded = true;
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"VST2 '{Name}': load failed: {ex.Message}");
            TeardownNative();
            return false;
        }
    }

    private void AllocateScratch()
    {
        if (_inChannels > 0) _inData = AllocChannels(_inChannels);
        if (_outChannels > 0) _outData = AllocChannels(_outChannels);

        _eventsBlock = (byte*)Marshal.AllocHGlobal(sizeof(Vst2Api.VstEventsHeader) + EventCapacity * sizeof(void*));
        _midiEvents = (Vst2Api.VstMidiEvent*)Marshal.AllocHGlobal(EventCapacity * sizeof(Vst2Api.VstMidiEvent));

        _timeInfo = (Vst2Api.VstTimeInfo*)Marshal.AllocHGlobal(sizeof(Vst2Api.VstTimeInfo));
        *_timeInfo = default;
        _timeInfo->SampleRate = _format.SampleRate > 0 ? _format.SampleRate : 44100;
        _timeInfo->Tempo = 120.0;
        _timeInfo->TimeSigNumerator = 4;
        _timeInfo->TimeSigDenominator = 4;
        _timeInfo->Flags = 0x400 | 0x200; // kVstTempoValid | kVstPpqPosValid
    }

    private static float** AllocChannels(int channels)
    {
        var data = (float**)Marshal.AllocHGlobal(channels * sizeof(void*));
        for (var c = 0; c < channels; c++) data[c] = (float*)Marshal.AllocHGlobal(MaxBlock * sizeof(float));
        return data;
    }

    private void BuildParameters()
    {
        var count = Math.Min(_effect->NumParams, MaxParamsShown);
        if (count <= 0) { _parameters = Array.Empty<Parameter>(); return; }

        var list = new List<Parameter>(count);
        for (var i = 0; i < count; i++)
        {
            var index = i; // capture per iteration
            var name = DispatchString(Vst2Api.EffGetParamName, index);
            if (string.IsNullOrWhiteSpace(name)) name = $"Param {index}";
            list.Add(new FloatParameter(name, 0, 1, () => GetParam(index), v => EnqueueParam(index, (float)v)));
        }

        _parameters = list;
    }

    private double GetParam(int index)
    {
        if (_effect == null || _effect->GetParameter == null) return 0;
        return _effect->GetParameter(_effect, index);
    }

    public void Prepare(AudioFormat format)
    {
        _format = format;
        if (!EnsureLoaded() || _effect == null) return;

        var rate = format.SampleRate > 0 ? format.SampleRate : 44100;
        if (_resumed && Math.Abs(_resumedRate - rate) < 0.5) return;
        if (_resumed) Suspend();

        if (_timeInfo != null) _timeInfo->SampleRate = rate;
        Dispatch(Vst2Api.EffSetSampleRate, 0, 0, null, (float)rate);
        Dispatch(Vst2Api.EffSetBlockSize, 0, MaxBlock, null, 0);
        Dispatch(Vst2Api.EffMainsChanged, 0, 1, null, 0);
        _resumed = true;
        _resumedRate = rate;
    }

    private void Suspend()
    {
        if (_effect == null) return;
        Dispatch(Vst2Api.EffMainsChanged, 0, 0, null, 0);
        _resumed = false;
    }

    // --- Audio thread ---

    /// <summary>
    /// Runs one <c>processReplacing()</c> block. <paramref name="feedInput"/> de-interleaves the engine
    /// buffer into the plugin's audio inputs (effects); <paramref name="replace"/> overwrites the engine
    /// buffer with the plugin output (effects) vs. adding to it (instruments).
    /// </summary>
    protected void RenderAudio(Span<float> buffer, bool feedInput, bool replace)
    {
        if (!_resumed || _effect == null || _outChannels <= 0) return;

        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;
        if (frames <= 0) return;
        if (frames > MaxBlock) frames = MaxBlock;

        ApplyParamChanges();
        SendMidiEvents();

        // Inputs: de-interleave for effects, silence for instruments.
        for (var ic = 0; ic < _inChannels; ic++)
        {
            var dst = _inData[ic];
            if (feedInput)
                for (var f = 0; f < frames; f++) dst[f] = ic < channels ? buffer[f * channels + ic] : 0f;
            else
                new Span<float>(dst, frames).Clear();
        }

        for (var oc = 0; oc < _outChannels; oc++) new Span<float>(_outData[oc], frames).Clear();

        if (_effect->ProcessReplacing != null)
            _effect->ProcessReplacing(_effect, _inData, _outData, frames);
        else if (_effect->Process != null)
            _effect->Process(_effect, _inData, _outData, frames); // deprecated accumulating variant
        else
            return;

        for (var f = 0; f < frames; f++)
        {
            var i = f * channels;
            for (var c = 0; c < channels; c++)
            {
                var pc = c < _outChannels ? c : _outChannels - 1; // mono out → spread to all channels
                var v = _outData[pc][f];
                if (replace) buffer[i + c] = v;
                else buffer[i + c] += v;
            }
        }
    }

    private void ApplyParamChanges()
    {
        if (_effect->SetParameter == null) return;
        lock (_evLock)
        {
            foreach (var p in _pending)
                if (p.Kind == PendingKind.Param) _effect->SetParameter(_effect, p.Index, p.Value);
            _pending.RemoveAll(p => p.Kind == PendingKind.Param);
        }
    }

    private void SendMidiEvents()
    {
        if (_effect->Dispatcher == null) return;

        int n;
        lock (_evLock)
        {
            n = 0;
            var ptrs = (void**)(_eventsBlock + sizeof(Vst2Api.VstEventsHeader));
            foreach (var p in _pending)
            {
                if (p.Kind == PendingKind.Param) continue;
                if (n >= EventCapacity) break;
                var ev = &_midiEvents[n];
                *ev = default;
                ev->Type = Vst2Api.MidiEventType;
                ev->ByteSize = sizeof(Vst2Api.VstMidiEvent);
                ev->Data0 = p.Data0;
                ev->Data1 = p.Data1;
                ev->Data2 = p.Data2;
                ptrs[n] = ev;
                n++;
            }

            _pending.RemoveAll(p => p.Kind != PendingKind.Param);
        }

        if (n == 0) return;
        var header = (Vst2Api.VstEventsHeader*)_eventsBlock;
        header->NumEvents = n;
        header->Reserved = 0;
        _effect->Dispatcher(_effect, Vst2Api.EffProcessEvents, 0, 0, _eventsBlock, 0);
    }

    // --- Event input (UI + scheduler threads) ---

    protected void EnqueueNoteOn(int midiNote, float velocity)
    {
        var vel = (int)Math.Round(velocity * 127f);
        vel = Math.Clamp(vel, 1, 127);
        lock (_evLock)
        {
            _pending.Add(Pending.Midi(0x90, (byte)(midiNote & 0x7F), (byte)vel));
            _held.Add(midiNote);
        }
    }

    protected void EnqueueNoteOff(int midiNote)
    {
        lock (_evLock)
        {
            _pending.Add(Pending.Midi(0x80, (byte)(midiNote & 0x7F), 0));
            _held.Remove(midiNote);
        }
    }

    protected void EnqueueAllNotesOff()
    {
        lock (_evLock)
        {
            foreach (var note in _held) _pending.Add(Pending.Midi(0x80, (byte)(note & 0x7F), 0));
            _held.Clear();
        }
    }

    protected void EnqueueControlChange(int controller, int value)
    {
        lock (_evLock) _pending.Add(Pending.Midi(0xB0, (byte)(controller & 0x7F), (byte)(value & 0x7F)));
    }

    protected void EnqueuePitchBend(int value14)
    {
        lock (_evLock) _pending.Add(Pending.Midi(0xE0, (byte)(value14 & 0x7F), (byte)((value14 >> 7) & 0x7F)));
    }

    protected void EnqueueAftertouch(int value)
    {
        lock (_evLock) _pending.Add(Pending.Midi(0xD0, (byte)(value & 0x7F), 0));
    }

    private void EnqueueParam(int index, float value)
    {
        lock (_evLock) _pending.Add(Pending.Param(index, value));
    }

    // --- IPluginEditor (native VST2 GUI) ---

    public bool HasEditor { get { EnsureLoaded(); return _hasEditor; } }
    public bool IsEditorOpen => _editorOpen;
    public bool PrefersFloating => false; // VST2 editors are always embedded into a parent window
    public int EditorWidth => _editorW;
    public int EditorHeight => _editorH;

    public void OpenEditor(nint windowHandle, string apiType, bool floating)
    {
        if (_editorOpen) return;
        if (!EnsureLoaded() || !_hasEditor) { Log?.Invoke($"VST2 '{Name}': no editor."); return; }

        try
        {
            // Embed into an intermediate default-visual X11 child on Linux; a GL plugin UI can't realize
            // directly in Avalonia's 32-bit ARGB window. Fall back to the host window if the child fails.
            var parent = windowHandle;
            if (OperatingSystem.IsLinux() && _embedWindow == 0)
            {
                if (X11Embed.Create(windowHandle, EmbedDefaultW, EmbedDefaultH, out _x11Display, out _embedWindow))
                    parent = _embedWindow;
                else
                    Log?.Invoke($"VST2 '{Name}': X11 embed window failed; using host window directly.");
            }
            else if (_embedWindow != 0)
            {
                parent = _embedWindow;
            }

            Dispatch(Vst2Api.EffEditOpen, 0, 0, (void*)parent, 0);

            Vst2Api.ERect* rect = null;
            Dispatch(Vst2Api.EffEditGetRect, 0, 0, &rect, 0);
            if (rect != null)
            {
                _editorW = rect->Right - rect->Left;
                _editorH = rect->Bottom - rect->Top;
            }

            if (_embedWindow != 0 && _editorW > 0 && _editorH > 0)
                X11Embed.Resize(_x11Display, _embedWindow, _editorW, _editorH);

            _editorOpen = true;
            Log?.Invoke($"VST2 '{Name}': editor opened {_editorW}x{_editorH}.");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"VST2 '{Name}': editor open failed: {ex.Message}");
            CloseEditor();
        }
    }

    public void SetEditorSize(int width, int height)
    {
        if (_embedWindow != 0 && width > 0 && height > 0)
            X11Embed.Resize(_x11Display, _embedWindow, width, height);
    }

    internal void OnPluginResize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _editorW = width;
        _editorH = height;
        if (_embedWindow != 0) X11Embed.Resize(_x11Display, _embedWindow, width, height);
    }

    public void CloseEditor()
    {
        if (_effect != null && _editorOpen) Dispatch(Vst2Api.EffEditClose, 0, 0, null, 0);
        _editorOpen = false;
        X11Embed.Destroy(ref _x11Display, ref _embedWindow);
    }

    public void PumpEditor()
    {
        if (!_editorOpen || _effect == null) return;
        Dispatch(Vst2Api.EffEditIdle, 0, 0, null, 0);
    }

    // --- Dispatch helpers ---

    private nint Dispatch(int opcode, int index, nint value, void* ptr, float opt)
        => _effect != null && _effect->Dispatcher != null ? _effect->Dispatcher(_effect, opcode, index, value, ptr, opt) : 0;

    private string DispatchString(int opcode, int index)
    {
        if (_effect == null || _effect->Dispatcher == null) return string.Empty;
        const int cap = 256;
        var buf = stackalloc byte[cap];
        _effect->Dispatcher(_effect, opcode, index, 0, buf, 0);
        return Vst2Api.ReadFixed(buf, cap);
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
            if (_effect != null)
            {
                CloseEditor();
                if (_resumed) Suspend();
                Dispatch(Vst2Api.EffClose, 0, 0, null, 0);
            }
        }
        catch { /* ignore */ }
        _effect = null;

        FreeChannels(ref _inData, _inChannels);
        FreeChannels(ref _outData, _outChannels);
        if (_eventsBlock != null) { Marshal.FreeHGlobal((nint)_eventsBlock); _eventsBlock = null; }
        if (_midiEvents != null) { Marshal.FreeHGlobal((nint)_midiEvents); _midiEvents = null; }
        if (_timeInfo != null) { Marshal.FreeHGlobal((nint)_timeInfo); _timeInfo = null; }
        if (_selfHandle.IsAllocated) _selfHandle.Free();

        _module?.Dispose();
        _module = null;
        _loaded = false;
        _resumed = false;
    }

    private static void FreeChannels(ref float** data, int channels)
    {
        if (data == null) return;
        for (var c = 0; c < channels; c++) if (data[c] != null) Marshal.FreeHGlobal((nint)data[c]);
        Marshal.FreeHGlobal((nint)data);
        data = null;
    }

    private enum PendingKind : byte { Midi, Param }

    private readonly record struct Pending(PendingKind Kind, int Index, float Value, byte Data0, byte Data1, byte Data2)
    {
        public static Pending Midi(byte d0, byte d1, byte d2) => new(PendingKind.Midi, 0, 0, d0, d1, d2);
        public static Pending Param(int index, float value) => new(PendingKind.Param, index, value, 0, 0, 0);
    }
}
