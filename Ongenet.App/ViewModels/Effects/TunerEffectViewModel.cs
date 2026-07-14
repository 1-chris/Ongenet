using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.ViewModels.Effects;

/// <summary>Pass-through tuner card with a large note readout refreshed during playback.</summary>
public sealed class TunerEffectViewModel : EffectViewModel
{
    public TunerEffectViewModel(TunerEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
    }

    public TunerEffect Tuner => (TunerEffect)Effect;

    public string DetectedNote => string.IsNullOrEmpty(Tuner.DetectedNote) ? "—" : Tuner.DetectedNote;

    public string DetectedHzText => Tuner.DetectedHz > 0 ? $"{Tuner.DetectedHz:0.0} Hz" : "";

    public new void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(DetectedNote));
        OnPropertyChanged(nameof(DetectedHzText));
    }
}
