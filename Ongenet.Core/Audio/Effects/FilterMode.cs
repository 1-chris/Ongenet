namespace Ongenet.Core.Audio.Effects;

/// <summary>The response shapes the <see cref="FilterEffect"/> can take.</summary>
public enum FilterMode
{
    LowPass,
    BandPass,
    HighPass,
    Notch,

    /// <summary>Pass-through: the filter (and its gains) are transparent.</summary>
    Bypass
}
