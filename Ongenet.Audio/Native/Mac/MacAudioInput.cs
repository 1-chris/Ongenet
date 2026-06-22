using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ongenet.Core.Audio;
using CA = Ongenet.Audio.Interop.CoreAudioNative;

namespace Ongenet.Audio.Native.Mac;

/// <summary>
/// <see cref="IAudioInput"/> for macOS, backed by a HAL output AudioUnit with input enabled (AUHAL).
/// The unit notifies us when captured frames are ready; we pull them with <c>AudioUnitRender</c> into a
/// pre-allocated buffer list and hand the block to the capture callback. Build-verified; needs on-device
/// shakeout.
/// </summary>
internal sealed unsafe class MacAudioInput : IAudioInput
{
    private const int Rate = 48000;
    private const int MaxFrames = 4096;

    private readonly object _lock = new();
    private readonly IAudioDeviceService _devices;
    private AudioCaptureCallback? _onAudio;
    private IntPtr _unit;
    private GCHandle _self;

    private int _channels = 1;
    private float[] _buffer = Array.Empty<float>();
    private GCHandle _bufPin;
    private IntPtr _bufList; // native AudioBufferList (1 buffer → _buffer)

    public MacAudioInput(IAudioDeviceService devices) => _devices = devices;

    public AudioFormat Format { get; private set; } = new(Rate, 1);
    public bool IsCapturing { get; private set; }

    public void Start(AudioCaptureCallback onAudio)
    {
        lock (_lock)
        {
            if (IsCapturing) return;
            _onAudio = onAudio;
            _channels = _devices.InputChannelMode == AudioInputChannelMode.Mono ? 1 : 2;
            OpenUnit();
            IsCapturing = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsCapturing) return;
            IsCapturing = false;
            CloseUnit();
            _onAudio = null;
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
        if (comp == IntPtr.Zero) throw new InvalidOperationException("No CoreAudio HAL component.");
        Check(CA.AudioComponentInstanceNew(comp, out _unit), "AudioComponentInstanceNew");

        // Enable input (bus 1) and disable output (bus 0) — this unit is capture-only.
        uint one = 1, zero = 0;
        SetProp(CA.kAudioOutputUnitProperty_EnableIO, CA.kAudioUnitScope_Input, 1, ref one);
        SetProp(CA.kAudioOutputUnitProperty_EnableIO, CA.kAudioUnitScope_Output, 0, ref zero);

        if (TryDeviceId(out var deviceId))
            SetProp(CA.kAudioOutputUnitProperty_CurrentDevice, CA.kAudioUnitScope_Global, 0, ref deviceId);

        // The format we want to receive from the unit (its output scope, bus 1): float32 interleaved.
        var asbd = new CA.AudioStreamBasicDescription
        {
            mSampleRate = Rate,
            mFormatID = CA.kAudioFormatLinearPCM,
            mFormatFlags = CA.kAudioFormatFlagIsFloat | CA.kAudioFormatFlagIsPacked,
            mFramesPerPacket = 1,
            mChannelsPerFrame = (uint)_channels,
            mBitsPerChannel = 32,
            mBytesPerFrame = (uint)(4 * _channels),
            mBytesPerPacket = (uint)(4 * _channels),
        };
        SetProp(CA.kAudioUnitProperty_StreamFormat, CA.kAudioUnitScope_Output, 1, ref asbd);

        // Pre-allocate the capture buffer + a native AudioBufferList pointing at it.
        _buffer = new float[MaxFrames * _channels];
        _bufPin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _bufList = Marshal.AllocHGlobal(24);
        Marshal.WriteInt32(_bufList, 0, 1);                       // mNumberBuffers
        Marshal.WriteInt32(_bufList, 8, _channels);               // mBuffers[0].mNumberChannels
        Marshal.WriteIntPtr(_bufList, 16, _bufPin.AddrOfPinnedObject()); // mBuffers[0].mData

        _self = GCHandle.Alloc(this);
        var cb = new CA.AURenderCallbackStruct
        {
            inputProc = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, uint, uint, IntPtr, int>)&InputThunk,
            inputProcRefCon = GCHandle.ToIntPtr(_self),
        };
        SetProp(CA.kAudioOutputUnitProperty_SetInputCallback, CA.kAudioUnitScope_Global, 0, ref cb);

        Check(CA.AudioUnitInitialize(_unit), "AudioUnitInitialize");
        Check(CA.AudioOutputUnitStart(_unit), "AudioOutputUnitStart");
        Format = new AudioFormat(Rate, _channels);
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

        if (_bufList != IntPtr.Zero) { Marshal.FreeHGlobal(_bufList); _bufList = IntPtr.Zero; }
        if (_bufPin.IsAllocated) _bufPin.Free();
        if (_self.IsAllocated) _self.Free();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int InputThunk(IntPtr refCon, IntPtr ioActionFlags, IntPtr inTimeStamp,
        uint inBusNumber, uint inNumberFrames, IntPtr ioData)
    {
        try
        {
            if (GCHandle.FromIntPtr(refCon).Target is MacAudioInput i)
                i.Capture(ioActionFlags, inTimeStamp, inBusNumber, inNumberFrames);
        }
        catch
        {
            // Never let a managed exception escape onto the RT thread.
        }

        return 0;
    }

    private void Capture(IntPtr ioActionFlags, IntPtr inTimeStamp, uint bus, uint nframes)
    {
        var frames = (int)Math.Min(nframes, (uint)MaxFrames);
        var bytes = frames * _channels * sizeof(float);
        Marshal.WriteInt32(_bufList, 12, bytes); // mDataByteSize for this pull

        if (CA.AudioUnitRender(_unit, ioActionFlags, inTimeStamp, bus, nframes, _bufList) != 0) return;

        var onAudio = _onAudio;
        if (onAudio is null) return;
        var span = new ReadOnlySpan<float>((void*)_bufPin.AddrOfPinnedObject(), frames * _channels);
        onAudio(span, _channels);
    }

    private bool TryDeviceId(out uint id)
    {
        id = 0;
        var sel = _devices.SelectedInput;
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

    public void Dispose() => Stop();

    private static void Check(int status, string op)
    {
        if (status != 0) throw new InvalidOperationException($"{op} failed: OSStatus {status}");
    }
}
