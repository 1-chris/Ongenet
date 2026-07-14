using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.ViewModels.Effects;

/// <summary>Spectrum analyser effect card with a live graph bound to the effect's tap.</summary>
public sealed class SpectrumEffectViewModel : EffectViewModel
{
    public SpectrumEffectViewModel(SpectrumEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
    }

    public SpectrumEffect Spectrum => (SpectrumEffect)Effect;
    public ISpectrumSource Source => Spectrum;
    public IWaveformSource Waveform => Spectrum;
    public IAudioAnalyzerSource Analyzer => Spectrum;
}
