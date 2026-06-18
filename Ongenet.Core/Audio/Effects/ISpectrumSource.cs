namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// An effect that exposes its most recent (post-processing) audio for a real-time spectrum
/// display. The audio thread fills a ring buffer; the UI copies the latest window out to analyse.
/// </summary>
public interface ISpectrumSource
{
    /// <summary>The sample rate the captured audio is at.</summary>
    int SampleRate { get; }

    /// <summary>
    /// Copies the most recent <paramref name="dest"/>.Length mono samples (chronological order)
    /// into <paramref name="dest"/>. Returns the number of samples written.
    /// </summary>
    int CaptureLatest(float[] dest);
}
