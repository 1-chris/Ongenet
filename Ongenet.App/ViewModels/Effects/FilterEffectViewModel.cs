using System;
using System.Linq;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.ViewModels.Effects
{
    /// <summary>
    /// A <see cref="FilterEffect"/> in the chain. Adds typed access to the frequency, resonance and
    /// mode parameter view models so the analyser graph can bind to (and live-update with) the same
    /// instances the knobs drive.
    /// </summary>
    public sealed class FilterEffectViewModel : EffectViewModel
    {
        public FilterEffectViewModel(FilterEffect effect, Action<EffectViewModel> remove,
            Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
            : base(effect, remove, moveUp, moveDown)
        {
            FrequencyParam = Param<FloatParameterViewModel>("Frequency");
            ResonanceParam = Param<FloatParameterViewModel>("Resonance");
            ModeParam = Param<ChoiceParameterViewModel>("Mode");
        }

        public FloatParameterViewModel FrequencyParam { get; }
        public FloatParameterViewModel ResonanceParam { get; }
        public ChoiceParameterViewModel ModeParam { get; }

        /// <summary>Sample rate used by the analyser graph to plot the response (display only).</summary>
        public double SampleRate => 44100.0;

        /// <summary>Live post-filter audio for the spectrum overlay (the same instance the engine runs).</summary>
        public ISpectrumSource? Source => Effect as ISpectrumSource;

        private T Param<T>(string name) where T : ParameterViewModel
            => (T)Parameters.First(p => p.Name == name);
    }
}
