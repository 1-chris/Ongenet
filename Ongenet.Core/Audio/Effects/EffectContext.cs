namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Per-block context handed to effects that opt in via <see cref="IContextualEffect"/>. Carries the
/// engine format, musical timing (tempo + playhead at the start of the block), and the shared
/// <see cref="ISidechainBus"/> so an effect can read another track's output as a trigger signal.
/// Reusable by any future effect that needs tempo-sync or cross-track signals.
/// </summary>
public sealed class EffectContext
{
    /// <summary>Engine sample rate + channel count for this block.</summary>
    public AudioFormat Format { get; set; }

    /// <summary>Project tempo in beats per minute.</summary>
    public double Bpm { get; set; } = 120.0;

    /// <summary>Playhead position, in quarter-note beats, at the START of this block.</summary>
    public double PlayheadBeats { get; set; }

    /// <summary>True while the transport is playing (false when stopped — tempo-synced effects idle).</summary>
    public bool Playing { get; set; }

    /// <summary>Shared bus for reading another track's output as a sidechain trigger. Never null.</summary>
    public ISidechainBus Sidechain { get; set; } = SidechainBus.Empty;
}
