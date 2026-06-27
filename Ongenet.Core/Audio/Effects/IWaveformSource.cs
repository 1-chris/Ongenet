namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// An effect (or other node) that exposes its most recent audio as a raw mono waveform for a real-time
/// oscilloscope-style display. The audio thread fills a ring buffer; the UI copies the latest window out.
/// Distinct from <see cref="ISpectrumSource"/> only in intent (time-domain waveform vs frequency analysis).
/// </summary>
public interface IWaveformSource
{
    /// <summary>The sample rate the captured audio is at.</summary>
    int SampleRate { get; }

    /// <summary>
    /// Copies the most recent <paramref name="dest"/>.Length mono samples (chronological order) into
    /// <paramref name="dest"/>. Returns the number of samples written.
    /// </summary>
    int CaptureLatest(float[] dest);
}
