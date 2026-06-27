using System;
using Ongenet.Core.Audio.Effects;
using Ongenet.App.Controls.Engine3D;

namespace Ongenet.App.ViewModels.Effects
{
    /// <summary>
    /// The pass-through "3D Scope" effect. It carries no knobs; instead it provides a
    /// <see cref="VisualizationFactory"/> that builds the 3D waveform-trail visualization (fed by the
    /// effect's <see cref="IWaveformSource"/> tap) for the embedded view and the pop-out window.
    /// </summary>
    public sealed class WaveformVisualizerEffectViewModel : EffectViewModel
    {
        public WaveformVisualizerEffectViewModel(WaveformVisualizerEffect effect, Action<EffectViewModel> remove,
            Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
            : base(effect, remove, moveUp, moveDown)
        {
            var source = effect as IWaveformSource;
            VisualizationFactory = () => new WaveformTrailVisualization(source);
        }

        /// <summary>Creates a fresh visualization instance (one per hosted view / pop-out window).</summary>
        public Func<IEngine3DVisualization> VisualizationFactory { get; }
    }
}
