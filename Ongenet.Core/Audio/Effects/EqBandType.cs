namespace Ongenet.Core.Audio.Effects;

/// <summary>The shape of a single <see cref="EqBand"/>.</summary>
public enum EqBandType
{
    /// <summary>Peaking (boost/cut a band around the centre frequency).</summary>
    Bell,
    LowShelf,
    HighShelf,
    HighPass,
    LowPass,
    Notch
}
