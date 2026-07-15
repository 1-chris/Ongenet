namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Marker for pass-through analyser / visualiser effects whose <see cref="IAudioEffect.Process"/>
/// never modifies audio. Offline renderers may skip these for speed.
/// </summary>
public interface IAnalyserOnlyEffect
{
}
