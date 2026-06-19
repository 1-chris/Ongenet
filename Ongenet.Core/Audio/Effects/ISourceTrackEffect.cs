using System;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Implemented by effects that read another track's output as an input signal (via the engine's
/// <see cref="ISidechainBus"/>) — e.g. the sidechain trigger or the vocoder carrier. The shared
/// contract lets one UI ("pick a source track") drive any such effect.
/// </summary>
public interface ISourceTrackEffect
{
    /// <summary>The source track/group whose output this effect taps; null = no source.</summary>
    Guid? SourceTrackId { get; set; }
}
