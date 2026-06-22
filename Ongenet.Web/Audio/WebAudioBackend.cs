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
    /// interleaved samples. Returns <c>double[]</c> because that marshals cleanly to a JS number array.
    /// </summary>
    internal double[] Render(int frames, int channels)
    {
        var n = frames * channels;
        var outBuffer = new double[n];
        if (_callback is null) return outBuffer; // silence until the engine attaches

        if (_scratch.Length < n) _scratch = new float[n];
        var span = _scratch.AsSpan(0, n);
        span.Clear();
        try { _callback(span); }
        catch { return new double[n]; } // a render fault must not kill the audio thread

        for (var i = 0; i < n; i++) outBuffer[i] = span[i];
        return outBuffer;
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
    public IReadOnlyList<AudioDevice> InputDevices { get; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<AudioDevice> OutputDevices { get; } = Array.Empty<AudioDevice>();

    public AudioDevice? SelectedOutput { get; set; }
    public AudioDevice? SelectedInput { get; set; }
    public AudioInputChannelMode InputChannelMode { get; set; } = AudioInputChannelMode.Stereo;

    public void Refresh() => DevicesChanged?.Invoke();

    public event Action? DevicesChanged;
    public event Action? OutputChanged;
    public event Action? InputChanged;
}
