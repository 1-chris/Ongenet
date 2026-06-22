using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Ongenet.Core.Audio;
using W = Ongenet.Audio.Interop.WasapiNative;

namespace Ongenet.Audio.Native.Win;

/// <summary>
/// <see cref="IAudioOutput"/> for Windows WASAPI (shared mode, event-driven). All COM lives on a single
/// dedicated MTA render thread: it activates the selected (or default) render endpoint, initialises the
/// audio client at the device mix format (float32 interleaved — the engine's native layout, no
/// conversion), then loops filling the render buffer each time WASAPI signals the event. Build-verified;
/// needs on-Windows shakeout.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WasapiOutput : IAudioOutput
{
    private readonly object _lock = new();
    private readonly IAudioDeviceService _devices;
    private AudioRenderCallback? _render;
    private Thread? _thread;
    private volatile bool _running;
    private Exception? _startError;
    private readonly ManualResetEventSlim _ready = new(false);

    public WasapiOutput(IAudioDeviceService devices)
    {
        _devices = devices;
        _devices.OutputChanged += OnDeviceChanged;
    }

    public AudioFormat Format { get; private set; } = new(48000, 2);
    public event Action? FormatChanged;
    public bool IsRunning { get; private set; }

    public void Start(AudioRenderCallback callback)
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _render = callback;
            _startError = null;
            _ready.Reset();
            _running = true;
            _thread = new Thread(RenderThread) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "wasapi-out" };
            _thread.Start();
            _ready.Wait(3000);
            if (_startError is not null) { _running = false; throw _startError; }
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _running = false;
            _thread?.Join(1000);
            _thread = null;
            _render = null;
        }
    }

    private unsafe void RenderThread()
    {
        IntPtr enumPtr = IntPtr.Zero, devPtr = IntPtr.Zero, clientPtr = IntPtr.Zero, renderPtr = IntPtr.Zero, fmt = IntPtr.Zero, evt = IntPtr.Zero;
        W.IAudioClient? client = null;
        W.IAudioRenderClient? rc = null;
        object? enumObj = null, devObj = null, clientObj = null, rcObj = null;
        try
        {
            W.CoInitializeEx(IntPtr.Zero, W.COINIT_MULTITHREADED);

            Check(W.CoCreateInstance(ref W.CLSID_MMDeviceEnumerator, IntPtr.Zero, W.CLSCTX_ALL, ref W.IID_IMMDeviceEnumerator, out enumPtr), "CoCreateInstance");
            enumObj = Marshal.GetObjectForIUnknown(enumPtr);
            var devEnum = (W.IMMDeviceEnumerator)enumObj;

            var id = EndpointId();
            Check(id is null
                ? devEnum.GetDefaultAudioEndpoint(W.eRender, W.eConsole, out devPtr)
                : devEnum.GetDevice(id, out devPtr), "GetEndpoint");
            devObj = Marshal.GetObjectForIUnknown(devPtr);
            var device = (W.IMMDevice)devObj;

            Check(device.Activate(ref W.IID_IAudioClient, W.CLSCTX_ALL, IntPtr.Zero, out clientPtr), "Activate");
            clientObj = Marshal.GetObjectForIUnknown(clientPtr);
            client = (W.IAudioClient)clientObj;

            Check(client.GetMixFormat(out fmt), "GetMixFormat");
            var wfx = Marshal.PtrToStructure<W.WAVEFORMATEX>(fmt);
            var channels = wfx.nChannels;
            var rate = (int)wfx.nSamplesPerSec;

            Check(client.Initialize(W.AUDCLNT_SHAREMODE_SHARED, W.AUDCLNT_STREAMFLAGS_EVENTCALLBACK, 0, 0, fmt, IntPtr.Zero), "Initialize");

            evt = W.CreateEventW(IntPtr.Zero, false, false, null);
            if (evt == IntPtr.Zero) throw new InvalidOperationException("CreateEvent failed.");
            Check(client.SetEventHandle(evt), "SetEventHandle");

            Check(client.GetBufferSize(out var bufferFrames), "GetBufferSize");
            Check(client.GetService(ref W.IID_IAudioRenderClient, out renderPtr), "GetService(render)");
            rcObj = Marshal.GetObjectForIUnknown(renderPtr);
            rc = (W.IAudioRenderClient)rcObj;

            Check(client.Start(), "Start");

            var newFmt = new AudioFormat(rate, channels);
            var changed = newFmt != Format;
            Format = newFmt;
            _ready.Set();
            if (changed) FormatChanged?.Invoke();

            // Event-driven loop: on each signal, fill the free part of the shared buffer with float32.
            while (_running)
            {
                if (W.WaitForSingleObject(evt, 200) != W.WAIT_OBJECT_0) continue;
                if (!_running) break;
                if (client.GetCurrentPadding(out var padding) < 0) continue;
                var avail = bufferFrames - padding;
                if (avail == 0) continue;
                if (rc.GetBuffer(avail, out var data) < 0 || data == IntPtr.Zero) continue;

                var span = new Span<float>((void*)data, (int)avail * channels);
                var render = _render;
                if (render is not null) render(span);
                else span.Clear();
                rc.ReleaseBuffer(avail, 0);
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
            if (evt != IntPtr.Zero) W.CloseHandle(evt);
            if (rcObj is not null) Marshal.ReleaseComObject(rcObj);
            if (clientObj is not null) Marshal.ReleaseComObject(clientObj);
            if (devObj is not null) Marshal.ReleaseComObject(devObj);
            if (enumObj is not null) Marshal.ReleaseComObject(enumObj);
            if (renderPtr != IntPtr.Zero) Marshal.Release(renderPtr);
            if (clientPtr != IntPtr.Zero) Marshal.Release(clientPtr);
            if (devPtr != IntPtr.Zero) Marshal.Release(devPtr);
            if (enumPtr != IntPtr.Zero) Marshal.Release(enumPtr);
            W.CoUninitialize();
        }
    }

    private string? EndpointId()
    {
        var sel = _devices.SelectedOutput;
        if (sel?.Id is { } id && id.StartsWith("wasapi:", StringComparison.Ordinal))
        {
            var ep = id["wasapi:".Length..];
            return ep == "default" ? null : ep;
        }

        return null;
    }

    private void OnDeviceChanged()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            // Restart the thread on the new device.
            IsRunning = false;
            _running = false;
            _thread?.Join(1000);
            var cb = _render;
            if (cb is null) return;
            _startError = null;
            _ready.Reset();
            _running = true;
            _thread = new Thread(RenderThread) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "wasapi-out" };
            _thread.Start();
            _ready.Wait(3000);
            IsRunning = _startError is null;
        }
    }

    public void Dispose()
    {
        _devices.OutputChanged -= OnDeviceChanged;
        Stop();
        _ready.Dispose();
    }

    private static void Check(int hr, string op)
    {
        if (hr < 0) throw new InvalidOperationException($"WASAPI {op} failed: HRESULT 0x{hr:X8}");
    }
}
