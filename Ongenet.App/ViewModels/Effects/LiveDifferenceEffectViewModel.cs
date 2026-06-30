using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.ViewModels.Effects
{
    /// <summary>
    /// A <see cref="LiveDifferenceEffect"/> in the chain. Reuses the shared source picker
    /// (<see cref="SourceTrackEffectViewModel"/>) to choose the track to subtract; "None" leaves the audio
    /// untouched. The Amount / Output knobs come from the generic <see cref="EffectViewModel.Parameters"/>.
    /// </summary>
    public sealed class LiveDifferenceEffectViewModel : SourceTrackEffectViewModel
    {
        public LiveDifferenceEffectViewModel(LiveDifferenceEffect effect, Action<EffectViewModel> remove,
            Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
            : base(effect, effect, "None", remove, moveUp, moveDown)
        {
        }
    }
}
