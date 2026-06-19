using System;

namespace Ongenet.Core.Audio;

/// <summary>
/// Pulls audio from the engine: the device repeatedly calls back asking for the next block of
/// interleaved samples to play.
/// </summary>
/// <param name="buffer">Buffer to fill, length = frames × channels (interleaved).</param>
public delegate void AudioRenderCallback(Span<float> buffer);

/// <summary>
/// Abstraction over the platform audio device. The concrete implementation (PortAudio) lives
/// in the Ongenet.Audio project; the engine depends only on this seam, so the device layer is
/// swappable without touching any DSP.
/// </summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>The format the device runs at (known after <see cref="Start"/>).</summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Raised when <see cref="Format"/> changes (e.g. opening a device whose actual sample rate differs,
    /// or switching devices while running), so the engine can re-prepare its DSP at the new rate.
    /// </summary>
    event Action? FormatChanged;

    /// <summary>Whether the device is currently open and streaming.</summary>
    bool IsRunning { get; }

    /// <summary>Opens the device and begins streaming, pulling blocks via <paramref name="callback"/>.</summary>
    void Start(AudioRenderCallback callback);

    /// <summary>Stops streaming and closes the device.</summary>
    void Stop();
}
