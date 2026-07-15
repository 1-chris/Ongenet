using System;
using Ongenet.App.Localization;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.ViewModels.Effects;

/// <summary>Analyser-only loudness meter card with M/ST/I/LRA/dBTP readouts.</summary>
public sealed class LoudnessMeterEffectViewModel : EffectViewModel
{
    public LoudnessMeterEffectViewModel(LoudnessMeterEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
    }

    private LoudnessMeterEffect Meter => (LoudnessMeterEffect)Effect;

    public string ReadoutText
    {
        get
        {
            static string F(float v) => float.IsNegativeInfinity(v) || float.IsNaN(v) ? "−∞" : v.ToString("0.0");
            return Loc.Get("Mastering_LoudnessMeter_Readout",
                "M {0} · ST {1} · I {2} · LRA {3} · {4} dBTP",
                F(Meter.MomentaryLufs), F(Meter.ShortTermLufs), F(Meter.IntegratedLufs),
                F(Meter.Lra), F(Meter.TruePeakDbTp));
        }
    }

    public string Hint => Loc.Get("Mastering_LoudnessMeter_Hint",
        "Pass-through analyser — place before the Peak Limiter for pre-limiter staging.");

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(ReadoutText));
    }
}
