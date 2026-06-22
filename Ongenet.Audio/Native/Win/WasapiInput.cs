using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Ongenet.Core.Audio;
using W = Ongenet.Audio.Interop.WasapiNative;

namespace Ongenet.Audio.Native.Win;

/// <summary>
/// <see cref="IAudioInput"/> for Windows WASAPI (shared mode). Captures from a normal capture endpoint,
/// or — when a "loopback" device is selected — from a render endpoint with the LOOPBACK flag, which
/// records whatever is playing on that output (Windows' equivalent of a monitor source: how you record
/// another app). All COM lives on a dedicated MTA polling thread. Build-verified; needs on-Windows shakeout.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WasapiInput : IAudioInput
{
    private readonly object _lock = new();
    private readonly IAudioDeviceService _devices;
    private AudioCaptureCallback? _onAudio;
    private Thread? _thread;
    private volatile bool _running;
    private Exception? _startError;
    private readonly ManualResetEventSlim _ready = new(false);
    private float[] _silence = Array.Empty<float>();

    public WasapiInput(IAudioDeviceService devices) => _devices = devices;

    public AudioFormat Format { get; private set; } = new(48000, 2);
    public bool IsCapturing { get; private set; }

    public void Start(AudioCaptureCallback onAudio)
    {
        lock (_lock)
        {
            if (IsCapturing) return;
            _onAudio = onAudio;
            _startError = null;
            _ready.Reset();
            _running = true;
            _thread = new Thread(CaptureThread) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "wasapi-in" };
            _thread.Start();
            _ready.Wait(3000);
            if (_startError is not null) { _running = false; throw _startError; }
            IsCapturing = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsCapturing) return;
            IsCapturing = false;
            _running = false;
            _thread?.Join(1000);
            _thread = null;
            _onAudio = null;
        }
    }

    private unsafe void CaptureThread()
    {
        IntPtr enumPtr = IntPtr.Zero, devPtr = IntPtr.Zero, clientPtr = IntPtr.Zero, capPtr = IntPtr.Zero, fmt = IntPtr.Zero;
        object? enumObj = null, devObj = null, clientObj = null, capObj = null;
        try
        {
            W.CoInitializeEx(IntPtr.Zero, W.COINIT_MULTITHREADED);
            var (endpointId, loopback) = Endpoint();

            Check(W.CoCreateInstance(ref W.CLSID_MMDeviceEnumerator, IntPtr.Zero, W.CLSCTX_ALL, ref W.IID_IMMDeviceEnumerator, out enumPtr), "CoCreateInstance");
            enumObj = Marshal.GetObjectForIUnknown(enumPtr);
            var devEnum = (W.IMMDeviceEnumerator)enumObj;

            // Loopback reads from a RENDER endpoint; normal capture from a CAPTURE endpoint.
            var dataFlow = loopback ? W.eRender : W.eCapture;
            Check(endpointId is null
                ? devEnum.GetDefaultAudioEndpoint(dataFlow, W.eConsole, out devPtr)
                : devEnum.GetDevice(endpointId, out devPtr), "GetEndpoint");
            devObj = Marshal.GetObjectForIUnknown(devPtr);
            var device = (W.IMMDevice)devObj;

            Check(device.Activate(ref W.IID_IAudioClient, W.CLSCTX_ALL, IntPtr.Zero, out clientPtr), "Activate");
            clientObj = Marshal.GetObjectForIUnknown(clientPtr);
            var client = (W.IAudioClient)clientObj;

            Check(client.GetMixFormat(out fmt), "GetMixFormat");
            var wfx = Marshal.PtrToStructure<W.WAVEFORMATEX>(fmt);
            var channels = (int)wfx.nChannels;
            var rate = (int)wfx.nSamplesPerSec;

            // Polling (no event flag) — required for loopback and fine for shared capture.
            var flags = loopback ? W.AUDCLNT_STREAMFLAGS_LOOPBACK : 0u;
            Check(client.Initialize(W.AUDCLNT_SHAREMODE_SHARED, flags, 0, 0, fmt, IntPtr.Zero), "Initialize");
            Check(client.GetService(ref W.IID_IAudioCaptureClient, out capPtr), "GetService(capture)");
            capObj = Marshal.GetObjectForIUnknown(capPtr);
            var cap = (W.IAudioCaptureClient)capObj;

            Check(client.Start(), "Start");
            Format = new AudioFormat(rate, channels);
            _ready.Set();

            while (_running)
            {
                Thread.Sleep(5);
                while (_running && cap.GetNextPacketSize(out var packet) >= 0 && packet > 0)
                {
                    if (cap.GetBuffer(out var data, out var frames, out var bufFlags, out _, out _) < 0) break;
                    if (frames > 0)
                    {
                        var onAudio = _onAudio;
                        if (onAudio is not null)
                        {
                            var count = (int)frames * channels;
                            if ((bufFlags & W.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                            {
                                if (_silence.Length < count) _silence = new float[count];
                                onAudio(_silence.AsSpan(0, count), channels);
                            }
                            else if (data != IntPtr.Zero)
                            {
                                onAudio(new ReadOnlySpan<float>((void*)data, count), channels);
                            }
                        }
                    }

                    cap.ReleaseBuffer(frames);
                }
            }

            client.Stop();
        }
        catch (Exception ex)
        {
            _startError = ex;
            _ready.Set();
        }
        finally
        {
            if (fmt != IntPtr.Zero) W.CoTaskMemFree(fmt);
            if (capObj is not null) Marshal.ReleaseComObject(capObj);
            if (clientObj is not null) Marshal.ReleaseComObject(clientObj);
            if (devObj is not null) Marshal.ReleaseComObject(devObj);
            if (enumObj is not null) Marshal.ReleaseComObject(enumObj);
            if (capPtr != IntPtr.Zero) Marshal.Release(capPtr);
            if (clientPtr != IntPtr.Zero) Marshal.Release(clientPtr);
            if (devPtr != IntPtr.Zero) Marshal.Release(devPtr);
            if (enumPtr != IntPtr.Zero) Marshal.Release(enumPtr);
            W.CoUninitialize();
        }
    }

    // (endpointId, loopback). "wasapi:loopback:<renderId>" → loopback; "wasapi:<captureId>" → normal; default otherwise.
    private (string? id, bool loopback) Endpoint()
    {
        var raw = _devices.SelectedInput?.Id;
        if (raw is null || !raw.StartsWith("wasapi:", StringComparison.Ordinal)) return (null, false);
        var rest = raw["wasapi:".Length..];
        if (rest.StartsWith("loopback:", StringComparison.Ordinal))
        {
            var ep = rest["loopback:".Length..];
            return (ep == "default" ? null : ep, true);
        }

        return (rest == "default" ? null : rest, false);
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private static void Check(int hr, string op)
    {
        if (hr < 0) throw new InvalidOperationException($"WASAPI {op} failed: HRESULT 0x{hr:X8}");
    }
}
