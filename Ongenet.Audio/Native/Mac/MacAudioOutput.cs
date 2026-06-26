using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Ongenet.Core.Audio;
using CA = Ongenet.Audio.Interop.CoreAudioNative;

namespace Ongenet.Audio.Native.Mac;

/// <summary>
/// <see cref="IAudioOutput"/> for macOS, backed by a HAL output AudioUnit (AUHAL). Opens the selected
/// device as float32 interleaved (the engine's native layout) and the unit pulls blocks from our render
/// callback on CoreAudio's real-time thread.
///
/// The engine's DSP is deliberately <b>not</b> run inside that render callback. CoreAudio's render thread
/// is a real-time thread that the .NET runtime can suspend (for GC) and that macOS can deprioritise (App
/// Nap / efficiency-core scheduling) once the app looks idle — so doing the heavy mix there leaves zero
/// slack and any stall becomes an instant, audible dropout (the classic "fine for ~30 s, then constant
/// crackle" symptom). Instead a dedicated high-priority producer thread renders the engine ahead of time
/// into a lock-free single-producer/single-consumer ring buffer, and the render callback only copies
/// finished samples out. The buffered "lead" is the slack that absorbs producer-side jitter (and is also
/// the added output latency, so it is kept small and is tunable via <c>ONGENET_CA_LEAD_FRAMES</c>).
///
/// Reopens when the selected device changes; the producer and ring survive the reopen.
/// </summary>
internal sealed unsafe class MacAudioOutput : IAudioOutput
{
    private const int Rate = 48000;
    private const int Channels = 2;

    // --- Decoupling ring buffer (frames are interleaved Channels-sample groups) --------------------
    private const int RingFrames = 1 << 14;   // 16384 frames (~341 ms) capacity; power of two for masking
    private const int RingMask = RingFrames - 1;
    private const int FillChunkFrames = 512;  // engine render granularity on the producer thread
    private const int DefaultLeadFrames = 2048; // ~43 ms target buffered ahead (latency == jitter slack)

    private readonly int _leadFrames = ResolveLeadFrames();
    private readonly float[] _ring = new float[RingFrames * Channels];
    private long _writeCount; // frames produced (monotonic; only the producer writes this)
    private long _readCount;  // frames consumed (monotonic; only the consumer writes this)
    private readonly AutoResetEvent _drained = new(false);
    private readonly float[] _scratch = new float[FillChunkFrames * Channels];
    private Thread? _producer;
    private volatile bool _producing;

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
            StartProducer(); // pre-roll: fills the ring before the device starts pulling
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
            StopProducer();
            _render = null;
        }
    }

    // --- Producer (renders the engine ahead of the device into the ring) --------------------------

    private void StartProducer()
    {
        Volatile.Write(ref _writeCount, 0);
        Volatile.Write(ref _readCount, 0);
        _producing = true;
        _producer = new Thread(ProducerLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = "coreaudio-render",
        };
        _producer.Start();
    }

    private void StopProducer()
    {
        _producing = false;
        _drained.Set();
        _producer?.Join(1000);
        _producer = null;
    }

    // Keep the ring topped up to roughly _leadFrames ahead of the consumer, rendering in fixed chunks.
    // When far enough ahead, block on the drained event (raised by the render callback after each read)
    // so we neither busy-spin nor run the engine unboundedly into the future (which would add latency).
    private void ProducerLoop()
    {
        var render = _render;
        if (render is null) return;

        while (_producing)
        {
            var filled = Volatile.Read(ref _writeCount) - Volatile.Read(ref _readCount);
            if (filled >= _leadFrames)
            {
                _drained.WaitOne(5);
                continue;
            }

            // The engine clears and fills the whole span each call; render off the RT thread so any
            // allocation/GC/JIT cost here is absorbed by the ring rather than dropping a device block.
            render(_scratch);
            WriteRing(_scratch);
        }
    }

    private void WriteRing(ReadOnlySpan<float> src)
    {
        var write = Volatile.Read(ref _writeCount);
        var startSample = (int)(write & RingMask) * Channels;
        var first = Math.Min(src.Length, _ring.Length - startSample);
        src.Slice(0, first).CopyTo(_ring.AsSpan(startSample));
        if (first < src.Length) src.Slice(first).CopyTo(_ring.AsSpan(0));
        // Publish only after the samples are in place (release); the consumer acquires via _writeCount.
        Volatile.Write(ref _writeCount, write + src.Length / Channels);
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

    // Runs on CoreAudio's real-time thread: copy only — no DSP, no allocation, so it always makes the
    // deadline. The heavy work was done ahead of time by the producer thread.
    private void Render(IntPtr ioData)
    {
        if (ioData == IntPtr.Zero) return;
        // AudioBufferList: mNumberBuffers@0, mBuffers[0] @8 = { mNumberChannels@8, mDataByteSize@12, mData@16 }.
        var byteSize = (int)Marshal.ReadInt32(ioData, 12);
        var data = Marshal.ReadIntPtr(ioData, 16);
        if (data == IntPtr.Zero || byteSize <= 0) return;

        ReadRing(new Span<float>((void*)data, byteSize / sizeof(float)));
        _drained.Set(); // wake the producer to refill what we just drained
    }

    private void ReadRing(Span<float> dst)
    {
        var read = Volatile.Read(ref _readCount);
        // Acquire _writeCount before touching the ring so we observe the producer's published samples.
        var available = (int)(Volatile.Read(ref _writeCount) - read) * Channels;
        var n = Math.Min(dst.Length, available);

        var startSample = (int)(read & RingMask) * Channels;
        var first = Math.Min(n, _ring.Length - startSample);
        _ring.AsSpan(startSample, first).CopyTo(dst);
        if (first < n) _ring.AsSpan(0, n - first).CopyTo(dst.Slice(first));

        if (n < dst.Length) dst.Slice(n).Clear(); // under-run → silence the remainder (never garbage)
        Volatile.Write(ref _readCount, read + n / Channels);
    }

    private static int ResolveLeadFrames()
    {
        var env = Environment.GetEnvironmentVariable("ONGENET_CA_LEAD_FRAMES");
        if (!string.IsNullOrWhiteSpace(env) &&
            int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return Math.Clamp(v, FillChunkFrames, RingFrames - FillChunkFrames);
        return DefaultLeadFrames;
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
        _drained.Dispose();
    }

    private static void Check(int status, string op)
    {
        if (status != 0) throw new InvalidOperationException($"{op} failed: OSStatus {status}");
    }
}
