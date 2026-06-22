using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.ViewModels.Effects
{
    /// <summary>
    /// A <see cref="VocoderEffect"/> in the chain. Adds a carrier-track picker (from
    /// <see cref="SourceTrackEffectViewModel"/>) on top of the generic parameters: "(None)" to bypass,
    /// or the track/group whose sound is shaped by this track's envelope.
    /// </summary>
    public sealed class VocoderEffectViewModel : SourceTrackEffectViewModel
    {
        public VocoderEffectViewModel(VocoderEffect effect, Action<EffectViewModel> remove,
            Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
            : base(effect, effect, "(None)", remove, moveUp, moveDown)
        {
        }
    }
}
