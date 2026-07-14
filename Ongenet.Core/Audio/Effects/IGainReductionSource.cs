namespace Ongenet.Core.Audio.Effects;

/// <summary>Effects that expose current gain reduction for meter UI.</summary>
public interface IGainReductionSource
{
    double GainReductionDb { get; }
}
