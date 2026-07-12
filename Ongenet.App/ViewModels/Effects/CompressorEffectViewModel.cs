using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.ViewModels.Effects;

/// <summary>
/// A <see cref="CompressorEffect"/> in the chain. Adds an optional sidechain source picker above the
/// threshold/ratio knobs.
/// </summary>
public sealed class CompressorEffectViewModel : SourceTrackEffectViewModel
{
    public CompressorEffectViewModel(CompressorEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, effect, "Off (internal)", remove, moveUp, moveDown)
    {
    }
}
