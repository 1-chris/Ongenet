using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.ViewModels.Effects
{
    /// <summary>
    /// An <see cref="EqEffect"/> in the chain. Exposes the effect (the interactive graph edits its
    /// bands directly) and its live post-EQ spectrum source.
    /// </summary>
    public sealed class EqEffectViewModel : EffectViewModel
    {
        public EqEffectViewModel(EqEffect effect, Action<EffectViewModel> remove,
            Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
            : base(effect, remove, moveUp, moveDown)
            => Eq = effect;

        public EqEffect Eq { get; }

        /// <summary>Live post-EQ audio for the spectrum overlay (the same instance the engine runs).</summary>
        public ISpectrumSource Source => Eq;
    }
}
