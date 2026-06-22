using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ongenet.Core.Audio;
using CA = Ongenet.Audio.Interop.CoreAudioNative;

namespace Ongenet.Audio.Native.Mac;

/// <summary>
/// <see cref="IAudioOutput"/> for macOS, backed by a HAL output AudioUnit (AUHAL). Opens the selected
/// device as float32 interleaved (the engine's native layout) and the unit pulls blocks from our render
/// callback on CoreAudio's real-time thread. Reopens when the selected device changes. Build-verified;
/// needs on-device shakeout (selectors/scopes transcribed without headers).
/// </summary>
internal sealed unsafe class MacAudioOutput : IAudioOutput
{
    private const int Rate = 48000;
    private const int Channels = 2;

    private readonly object _lock = new();
    private readonly IAudioDeviceService _devices;
    private AudioRenderCallback? _render;
    private IntPtr _unit;
    private GCHandle _self;

    public MacAudioOutput(IAudioDeviceService devices)
    {
        _devices = devices;
        _devices.OutputChanged += OnDeviceChanged;
    }

    public AudioFormat Format { get; private set; } = new(Rate, Channels);
    public event Action? FormatChanged;
    public bool IsRunning { get; private set; }

    public void Start(AudioRenderCallback callback)
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _render = callback;
            OpenUnit();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            CloseUnit();
            _render = null;
        }
    }

    private void OpenUnit()
    {
        var desc = new CA.AudioComponentDescription
        {
            componentType = CA.kAudioUnitType_Output,
            componentSubType = CA.kAudioUnitSubType_HALOutput,
            componentManufacturer = CA.kAudioUnitManufacturer_Apple,
        };
        var comp = CA.AudioComponentFindNext(IntPtr.Zero, ref desc);
        if (comp == IntPtr.Zero) throw new InvalidOperationException("No CoreAudio HAL output component.");
        Check(CA.AudioComponentInstanceNew(comp, out _unit), "AudioComponentInstanceNew");

        // Pin the chosen device (if any) to this unit; otherwise it uses the system default output.
        if (TryDeviceId(out var deviceId))
            SetProp(CA.kAudioOutputUnitProperty_CurrentDevice, CA.kAudioUnitScope_Global, 0, ref deviceId);

        // float32 interleaved, set on the unit's input scope (the side our render callback feeds).
        var asbd = new CA.AudioStreamBasicDescription
        {
            mSampleRate = Rate,
            mFormatID = CA.kAudioFormatLinearPCM,
            mFormatFlags = CA.kAudioFormatFlagIsFloat | CA.kAudioFormatFlagIsPacked,
            mFramesPerPacket = 1,
            mChannelsPerFrame = Channels,
            mBitsPerChannel = 32,
            mBytesPerFrame = 4 * Channels,
            mBytesPerPacket = 4 * Channels,
        };
        SetProp(CA.kAudioUnitProperty_StreamFormat, CA.kAudioUnitScope_Input, 0, ref asbd);

        _self = GCHandle.Alloc(this);
        var cb = new CA.AURenderCallbackStruct
        {
            inputProc = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, uint, uint, IntPtr, int>)&RenderThunk,
            inputProcRefCon = GCHandle.ToIntPtr(_self),
        };
        SetProp(CA.kAudioUnitProperty_SetRenderCallback, CA.kAudioUnitScope_Input, 0, ref cb);

        Check(CA.AudioUnitInitialize(_unit), "AudioUnitInitialize");
        Check(CA.AudioOutputUnitStart(_unit), "AudioOutputUnitStart");

        var fmt = new AudioFormat(Rate, Channels);
        var changed = fmt != Format;
        Format = fmt;
        if (changed) FormatChanged?.Invoke();
    }

    private void CloseUnit()
    {
        if (_unit != IntPtr.Zero)
        {
            CA.AudioOutputUnitStop(_unit);
            CA.AudioUnitUninitialize(_unit);
            CA.AudioComponentInstanceDispose(_unit);
            _unit = IntPtr.Zero;
        }

        if (_self.IsAllocated) _self.Free();
    }

    private void OnDeviceChanged()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            CloseUnit();
            try { OpenUnit(); }
            catch { IsRunning = false; } // leave the engine running silently if the new device won't open
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RenderThunk(IntPtr refCon, IntPtr ioActionFlags, IntPtr inTimeStamp,
        uint inBusNumber, uint inNumberFrames, IntPtr ioData)
    {
        try
        {
            if (GCHandle.FromIntPtr(refCon).Target is MacAudioOutput o) o.Render(ioData);
        }
        catch
        {
            // Never let a managed exception escape onto the RT thread.
        }

        return 0;
    }

    private void Render(IntPtr ioData)
    {
        if (ioData == IntPtr.Zero) return;
        // AudioBufferList: mNumberBuffers@0, mBuffers[0] @8 = { mNumberChannels@8, mDataByteSize@12, mData@16 }.
        var byteSize = (int)Marshal.ReadInt32(ioData, 12);
        var data = Marshal.ReadIntPtr(ioData, 16);
        if (data == IntPtr.Zero || byteSize <= 0) return;

        var span = new Span<float>((void*)data, byteSize / sizeof(float));
        var render = _render;
        if (render is not null) render(span);
        else span.Clear();
    }

    private bool TryDeviceId(out uint id)
    {
        id = 0;
        var sel = _devices.SelectedOutput;
        if (sel is null) return false;
        if (sel.Id.StartsWith("ca:", StringComparison.Ordinal) && uint.TryParse(sel.Id.AsSpan(3), out id)) return true;
        if (sel.Index > 0) { id = (uint)sel.Index; return true; }
        return false;
    }

    private void SetProp<T>(uint prop, uint scope, uint elem, ref T value) where T : unmanaged
    {
        fixed (void* p = &value)
            Check(CA.AudioUnitSetProperty(_unit, prop, scope, elem, (IntPtr)p, (uint)sizeof(T)), "AudioUnitSetProperty");
    }

    public void Dispose()
    {
        _devices.OutputChanged -= OnDeviceChanged;
        Stop();
    }

    private static void Check(int status, string op)
    {
        if (status != 0) throw new InvalidOperationException($"{op} failed: OSStatus {status}");
    }
}
