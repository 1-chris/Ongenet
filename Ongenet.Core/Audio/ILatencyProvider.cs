namespace Ongenet.Core.Audio;

/// <summary>
/// Reports algorithmic latency introduced by an instrument or effect, in samples at the current
/// engine sample rate. Used by plugin delay compensation (PDC).
/// </summary>
public interface ILatencyProvider
{
    /// <summary>Latency in samples at the rate passed to <see cref="Effects.IAudioEffect.Prepare"/>.</summary>
    int ReportedLatencySamples { get; }
}
