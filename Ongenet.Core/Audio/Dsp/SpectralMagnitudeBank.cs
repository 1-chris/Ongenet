using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Mutable magnitude spectrum shared between <see cref="Field.Nodes.SpectralImportNode"/> and
/// <see cref="Field.Nodes.PartialBankNode"/> via an asset wire.
/// </summary>
public sealed class SpectralMagnitudeBank
{
    private readonly float[] _magnitudes = new float[AdditivePartialEngine.MaxPartials];

    public int BinCount { get; private set; }
    public int Revision { get; private set; }

    public ReadOnlySpan<float> Magnitudes => _magnitudes;

    public void SetMagnitudes(ReadOnlySpan<float> source, int binCount)
    {
        binCount = Math.Clamp(binCount, 1, source.Length);
        var copy = Math.Min(binCount, _magnitudes.Length);
        for (var i = 0; i < copy; i++) _magnitudes[i] = source[i];
        for (var i = copy; i < _magnitudes.Length; i++) _magnitudes[i] = 0f;
        BinCount = binCount;
        Revision++;
    }
}
