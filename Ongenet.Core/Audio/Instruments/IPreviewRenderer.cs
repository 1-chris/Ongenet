using System;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Implemented by instruments whose sound is a deterministic one-shot (e.g. a drum synth), so the
/// inspector can render and display a preview waveform of the current patch. The render must be
/// self-contained and side-effect free: the host always calls this on a <b>detached copy</b>
/// (<see cref="IInstrument.Clone"/>) off the audio thread, never on a live, sounding instrument.
/// </summary>
public interface IPreviewRenderer
{
    /// <summary>
    /// Synthesises one hit of the current patch into <paramref name="mono"/> (single channel) at
    /// <paramref name="sampleRate"/>. Implementations should write the whole span (clearing as needed)
    /// and trigger the note internally.
    /// </summary>
    void RenderPreview(Span<float> mono, int sampleRate);

    /// <summary>Suggested preview length in seconds (the inspector sizes its buffer from this).</summary>
    double PreviewSeconds { get; }
}
