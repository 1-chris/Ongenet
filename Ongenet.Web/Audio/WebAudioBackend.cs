using System;
using System.Collections.Generic;
using Ongenet.Core.Audio;

namespace Ongenet.Web.Audio;

/// <summary>
/// <see cref="IAudioBackend"/> implemented over the browser's Web Audio API. Output is driven by a
/// <c>ScriptProcessorNode</c> (see <see cref="AudioInterop"/>); input/capture is not supported in the
/// browser demo. Device enumeration is a no-op — the browser routes to the system default output.
/// </summary>
public sealed class WebAudioBackend : IAudioBackend
{
    private readonly WebAudioOutput _output = new();
    private readonly WebAudioInput _input = new();
    private readonly WebAudioDeviceService _devices = new();

    public string Id => "webaudio";
    public string DisplayName => "Web Audio";
    public bool IsSupported => true;

    public IAudioDeviceService Devices => _devices;
    public IAudioOutput Output => _output;
    public IAudioInput Input => _input;

    public void Dispose() => _output.Dispose();
}

/// <summary>
/// Playback over a browser <c>ScriptProcessorNode</c>. The node pulls blocks on the main thread by calling
/// <see cref="Render"/>, which runs the engine's render callback into a scratch buffer and copies it out
/// as interleaved samples.
/// </summary>
public sealed class WebAudioOutput : IAudioOutput
{
    private const int Channels = 2;

    /// <summary>The output JS pulls from via <see cref="AudioInterop.RenderBlock"/>. Single output stream.</summary>
    internal static WebAudioOutput? Active { get; private set; }

    private AudioRenderCallback? _callback;
    private float[] _scratch = Array.Empty<float>();
    private double[] _interop = Array.Empty<double>();

    public AudioFormat Format { get; private set; } = AudioFormat.Default;
    public bool IsRunning { get; private set; }
    public event Action? FormatChanged;

    public void Start(AudioRenderCallback callback)
    {
        if (IsRunning) Stop();
        _callback = callback;
        Active = this;

        // Create the audio graph; JS pulls blocks from RenderBlock. The context reports its real sample
        // rate (usually 48000); the engine re-prepares its DSP for it via FormatChanged.
        var sampleRate = AudioInterop.StartAudio(Channels);

        Format = new AudioFormat(sampleRate > 0 ? sampleRate : 44100, Channels);
        IsRunning = true;
        FormatChanged?.Invoke();
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        if (ReferenceEquals(Active, this)) Active = null;
        try { AudioInterop.StopAudio(); } catch { /* tearing down — ignore */ }
    }

    /// <summary>
    /// Pulled by JS once per audio block. Renders <paramref name="frames"/> × <paramref name="channels"/>
    /// interleaved samples. Returns a reused <c>double[]</c> (marshals cleanly to a JS number array).
    /// Grow-only pooling avoids a ~4k-double GC alloc on every ScriptProcessor callback.
    /// </summary>
    internal double[] Render(int frames, int channels)
    {
        var n = frames * channels;
        EnsureBuffers(n);
        if (_callback is null)
        {
            Array.Clear(_interop, 0, n);
            return _interop;
        }

        var span = _scratch.AsSpan(0, n);
        span.Clear();
        try { _callback(span); }
        catch
        {
            // A render fault must not kill the audio callback — return silence from the pool.
            Array.Clear(_interop, 0, n);
            return _interop;
        }

        for (var i = 0; i < n; i++) _interop[i] = span[i];
        return _interop;
    }

    private void EnsureBuffers(int n)
    {
        if (_scratch.Length < n) _scratch = new float[n];
        if (_interop.Length < n) _interop = new double[n];
    }

    public void Dispose() => Stop();
}

/// <summary>Capture is unsupported in the browser demo (mic input via getUserMedia is a future addition).</summary>
public sealed class WebAudioInput : IAudioInput
{
    public AudioFormat Format => AudioFormat.Default;
    public bool IsCapturing => false;
    public void Start(AudioCaptureCallback onAudio) { /* no input in the browser demo */ }
    public void Stop() { }
    public void Dispose() { }
}

/// <summary>
/// No device enumeration in the browser — playback goes to the system default output chosen by the OS.
/// Presents a single synthetic "Default output" so the UI's device picker has something to show.
/// </summary>
public sealed class WebAudioDeviceService : IAudioDeviceService
{
    private AudioDevice? _selectedOutput;
    private AudioDevice? _selectedInput;
    private AudioInputChannelMode _inputChannelMode = AudioInputChannelMode.Stereo;

    public IReadOnlyList<AudioDevice> InputDevices { get; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<AudioDevice> OutputDevices { get; } = Array.Empty<AudioDevice>();

    public AudioDevice? SelectedOutput
    {
        get => _selectedOutput;
        set
        {
            if (Equals(_selectedOutput, value)) return;
            _selectedOutput = value;
            OutputChanged?.Invoke();
        }
    }

    public AudioDevice? SelectedInput
    {
        get => _selectedInput;
        set
        {
            if (Equals(_selectedInput, value)) return;
            _selectedInput = value;
            InputChanged?.Invoke();
        }
    }

    public AudioInputChannelMode InputChannelMode
    {
        get => _inputChannelMode;
        set
        {
            if (_inputChannelMode == value) return;
            _inputChannelMode = value;
            InputChanged?.Invoke();
        }
    }

    public bool LowLatencyExclusive { get; set; }

    public void Refresh() => DevicesChanged?.Invoke();

    public event Action? DevicesChanged;
    public event Action? OutputChanged;
    public event Action? InputChanged;
}
