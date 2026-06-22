using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.Core.Audio;

/// <summary>
/// The single switchable audio backend. Holds every available <see cref="IAudioBackend"/> and presents
/// itself as the engine's <see cref="IAudioOutput"/>, <see cref="IAudioInput"/> and
/// <see cref="IAudioDeviceService"/>, forwarding each call to the currently active backend. Switching
/// backends stops the active streams, swaps which backend is active, and restarts any stream that was
/// running — all of which is invisible to the engine, recording and DSP.
///
/// <para>Performance: forwarding happens only at control points (start/stop/device-change/switch).
/// The render and capture callbacks are handed straight to the active backend's stream, so the audio
/// thread reaches the engine with no extra indirection — there is no per-sample or per-block wrapper.</para>
/// </summary>
public sealed class AudioBackendManager : IAudioBackendManager, IAudioOutput, IAudioInput, IAudioDeviceService, IDisposable
{
    private readonly object _lock = new();
    private readonly List<IAudioBackend> _backends;
    private IAudioBackend _active;

    // Stored so a backend switch can transparently restart whatever was running.
    private AudioRenderCallback? _render;
    private AudioCaptureCallback? _capture;
    private bool _outputWanted;
    private bool _captureWanted;
    private bool _disposed;

    public AudioBackendManager(IEnumerable<IAudioBackend> backends)
    {
        _backends = backends.Where(b => b.IsSupported).ToList();
        if (_backends.Count == 0)
            throw new InvalidOperationException("No supported audio backend is available.");

        // Prefer the OS-native backend (ALSA/PipeWire/JACK/Pulse on Linux, CoreAudio on macOS, WASAPI on
        // Windows); fall back to the first available one if it isn't present for some reason.
        _active = _backends.FirstOrDefault(b => b.Id == "native") ?? _backends[0];
        Subscribe(_active);
    }

    // ---- IAudioBackendManager -------------------------------------------------------------------

    public IReadOnlyList<AudioBackendInfo> Backends =>
        _backends.Select(b => new AudioBackendInfo(b.Id, b.DisplayName, b.IsSupported, ReferenceEquals(b, _active)))
                 .ToList();

    public string ActiveId => _active.Id;

    public event Action? BackendChanged;

    public void Switch(string id)
    {
        lock (_lock)
        {
            if (_disposed) return;
            if (_active.Id == id) return;
            var next = _backends.FirstOrDefault(b => b.Id == id);
            if (next is null) return; // unknown / unsupported — leave the current backend active

            var hadOutput = _outputWanted;
            var hadCapture = _captureWanted;

            // Tear down the active backend's streams and stop listening to it.
            if (_active.Output.IsRunning) _active.Output.Stop();
            if (_active.Input.IsCapturing) _active.Input.Stop();
            Unsubscribe(_active);

            _active = next;
            Subscribe(_active);

            // Bring the new backend up to the same running state.
            if (hadOutput && _render is not null) _active.Output.Start(_render);
            if (hadCapture && _capture is not null) _active.Input.Start(_capture);
        }

        // The device list and (likely) the sample rate differ on the new backend; tell the UI to
        // refresh and the engine to re-prepare its DSP at the new rate.
        DevicesChanged?.Invoke();
        FormatChanged?.Invoke();
        BackendChanged?.Invoke();
    }

    // ---- IAudioOutput ---------------------------------------------------------------------------

    public AudioFormat Format => _active.Output.Format;
    public bool IsRunning => _active.Output.IsRunning;
    public event Action? FormatChanged;

    public void Start(AudioRenderCallback callback)
    {
        lock (_lock)
        {
            _render = callback;
            _outputWanted = true;
            _active.Output.Start(callback);
        }
    }

    void IAudioOutput.Stop()
    {
        lock (_lock)
        {
            _outputWanted = false;
            _active.Output.Stop();
        }
    }

    // ---- IAudioInput ----------------------------------------------------------------------------

    AudioFormat IAudioInput.Format => _active.Input.Format;
    public bool IsCapturing => _active.Input.IsCapturing;

    public void Start(AudioCaptureCallback onAudio)
    {
        lock (_lock)
        {
            _capture = onAudio;
            _captureWanted = true;
            _active.Input.Start(onAudio);
        }
    }

    void IAudioInput.Stop()
    {
        lock (_lock)
        {
            _captureWanted = false;
            _active.Input.Stop();
        }
    }

    // ---- IAudioDeviceService --------------------------------------------------------------------

    public IReadOnlyList<AudioDevice> InputDevices => _active.Devices.InputDevices;
    public IReadOnlyList<AudioDevice> OutputDevices => _active.Devices.OutputDevices;

    public AudioDevice? SelectedOutput
    {
        get => _active.Devices.SelectedOutput;
        set => _active.Devices.SelectedOutput = value;
    }

    public AudioDevice? SelectedInput
    {
        get => _active.Devices.SelectedInput;
        set => _active.Devices.SelectedInput = value;
    }

    public AudioInputChannelMode InputChannelMode
    {
        get => _active.Devices.InputChannelMode;
        set => _active.Devices.InputChannelMode = value;
    }

    public void Refresh() => _active.Devices.Refresh();

    public event Action? DevicesChanged;
    public event Action? OutputChanged;
    public event Action? InputChanged;

    // ---- backend event plumbing -----------------------------------------------------------------

    private void Subscribe(IAudioBackend backend)
    {
        backend.Output.FormatChanged += RaiseFormatChanged;
        backend.Devices.DevicesChanged += RaiseDevicesChanged;
        backend.Devices.OutputChanged += RaiseOutputChanged;
        backend.Devices.InputChanged += RaiseInputChanged;
    }

    private void Unsubscribe(IAudioBackend backend)
    {
        backend.Output.FormatChanged -= RaiseFormatChanged;
        backend.Devices.DevicesChanged -= RaiseDevicesChanged;
        backend.Devices.OutputChanged -= RaiseOutputChanged;
        backend.Devices.InputChanged -= RaiseInputChanged;
    }

    private void RaiseFormatChanged() => FormatChanged?.Invoke();
    private void RaiseDevicesChanged() => DevicesChanged?.Invoke();
    private void RaiseOutputChanged() => OutputChanged?.Invoke();
    private void RaiseInputChanged() => InputChanged?.Invoke();

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        foreach (var backend in _backends)
        {
            try { backend.Dispose(); }
            catch { /* a backend failing to tear down must not block the rest */ }
        }
    }
}
