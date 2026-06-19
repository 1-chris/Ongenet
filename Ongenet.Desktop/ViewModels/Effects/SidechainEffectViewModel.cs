using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Desktop.ViewModels.Effects
{
    /// <summary>
    /// A <see cref="SidechainEffect"/> in the chain. Adds a source picker (from
    /// <see cref="SourceTrackEffectViewModel"/>): "Tempo (synced)" for the built-in tempo pump, or any
    /// track/group whose output triggers the duck.
    /// </summary>
    public sealed class SidechainEffectViewModel : SourceTrackEffectViewModel
    {
        public SidechainEffectViewModel(SidechainEffect effect, Action<EffectViewModel> remove,
            Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
            : base(effect, effect, "Tempo (synced)", remove, moveUp, moveDown)
        {
        }
    }
}
