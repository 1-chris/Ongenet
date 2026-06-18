using System;

namespace Ongenet.Core.Audio;

/// <summary>
/// Delivers a block of freshly captured input from the audio thread. The handler MUST copy what it
/// needs and return immediately — the span is only valid for the duration of the call.
/// </summary>
/// <param name="input">Interleaved input samples for this block (length = frames × <paramref name="channels"/>).</param>
/// <param name="channels">Number of interleaved channels in <paramref name="input"/>.</param>
public delegate void AudioCaptureCallback(ReadOnlySpan<float> input, int channels);

/// <summary>
/// Abstraction over the platform audio capture device. Distinct from <see cref="IAudioOutput"/> so
/// input and output can run as independent streams on independently chosen devices. The concrete
/// implementation (PortAudio) opens the device selected in <see cref="IAudioDeviceService"/>.
/// </summary>
public interface IAudioInput : IDisposable
{
    /// <summary>The format capture runs at (known after <see cref="Start"/>).</summary>
    AudioFormat Format { get; }

    /// <summary>Whether the device is currently open and capturing.</summary>
    bool IsCapturing { get; }

    /// <summary>Opens the selected input device and begins capturing, pushing blocks to <paramref name="onAudio"/>.</summary>
    void Start(AudioCaptureCallback onAudio);

    /// <summary>Stops capturing and closes the device.</summary>
    void Stop();
}
