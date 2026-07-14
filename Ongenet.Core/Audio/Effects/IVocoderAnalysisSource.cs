using System;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Effects that expose vocoder band envelope levels for visualization.</summary>
public interface IVocoderAnalysisSource
{
    int BandCount { get; }

    /// <summary>Latest per-band envelope levels (0..1), length <see cref="BandCount"/>.</summary>
    ReadOnlySpan<float> BandLevels { get; }
}
