namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Opt-in interface for effects that need the per-block <see cref="EffectContext"/> (tempo, playhead,
/// sidechain bus). The engine (and offline renderer) call <see cref="SetContext"/> immediately before
/// <see cref="IAudioEffect.Process"/> on every block; the effect stashes it and reads it inside Process.
/// Effects that don't need timing/sidechain simply don't implement this — their Process is unchanged.
/// </summary>
public interface IContextualEffect
{
    void SetContext(EffectContext context);
}
