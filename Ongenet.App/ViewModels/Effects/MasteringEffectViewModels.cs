using System;
using System.Collections.Generic;
using Avalonia;
using Ongenet.Core.Audio.Effects;
using Ongenet.App.Localization;
using Ongenet.Core.Audio.Dsp;

namespace Ongenet.App.ViewModels.Effects;

public sealed class MatchEqEffectViewModel : EffectViewModel
{
    public MatchEqEffectViewModel(MatchEqEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown) { }

    private MatchEqEffect Match => (MatchEqEffect)Effect;
    public string StatusText => Match.HasTarget
        ? Loc.Get("Mastering_Card_MatchEq_TargetReady", "Reference target captured")
        : Loc.Get("Mastering_Card_MatchEq_NoTarget", "No reference target captured");
    public string Hint => Loc.Get("Mastering_Card_MatchEq_Hint",
        "Capture a reference, then raise Blend gently; the curve shows the applied spectral correction.");
    public Points DeltaCurve
    {
        get
        {
            Span<float> gains = stackalloc float[MatchEqEffect.EqBandCount];
            Match.CopyBandGainsDb(gains);
            var points = new Points();
            for (var i = 0; i < gains.Length; i++)
                points.Add(new Point(i * (160.0 / (gains.Length - 1)), 24 - Math.Clamp(gains[i], -12, 12) * 1.5));
            return points;
        }
    }

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DeltaCurve));
    }
}

public sealed class LinearPhaseEqEffectViewModel : EffectViewModel
{
    public LinearPhaseEqEffectViewModel(LinearPhaseEqEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown) { }

    private LinearPhaseEqEffect Linear => (LinearPhaseEqEffect)Effect;
    public string LatencyText => Loc.Format("Mastering_Card_LinearPhase_Latency", Linear.ReportedLatencySamples);
    public string Hint => Loc.Get("Mastering_Card_LinearPhase_Hint",
        "Approximate symmetric FIR EQ: phase-coherent but higher latency, with possible pre-ringing on transients.");
    public Points ResponseCurve
    {
        get
        {
            var frequencies = new[] { Linear.LowFreq, Linear.LowMidFreq, Linear.HighMidFreq, Linear.HighFreq };
            var gains = new[] { Linear.LowGainDb, Linear.LowMidGainDb, Linear.HighMidGainDb, Linear.HighGainDb };
            var points = new Points();
            const double minHz = 30, maxHz = 20000, width = 160, height = 48;
            for (var i = 0; i < 64; i++)
            {
                var t = i / 63.0;
                var hz = minHz * Math.Pow(maxHz / minHz, t);
                double db = 0;
                for (var b = 0; b < frequencies.Length; b++)
                {
                    var octaves = Math.Log2(hz / frequencies[b]);
                    db += gains[b] * Math.Exp(-0.5 * octaves * octaves / 0.35);
                }
                points.Add(new Point(t * width, height / 2 - Math.Clamp(db, -18, 18) * (height / 36)));
            }
            return points;
        }
    }

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(LatencyText));
        OnPropertyChanged(nameof(ResponseCurve));
    }
}

public sealed class MidSideEqEffectViewModel : EffectViewModel
{
    public MidSideEqEffectViewModel(MidSideEqEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
    }

    public MidSideEqEffect Ms => (MidSideEqEffect)Effect;

    public bool SoloMid
    {
        get => Ms.SoloMid;
        set { if (Ms.SoloMid == value) return; Ms.SoloMid = value; if (value) Ms.SoloSide = false; NotifySolos(); }
    }

    public bool SoloSide
    {
        get => Ms.SoloSide;
        set { if (Ms.SoloSide == value) return; Ms.SoloSide = value; if (value) Ms.SoloMid = false; NotifySolos(); }
    }

    public string Hint => Loc.Get("Mastering_Card_MidSide_Hint");
    public double MidEnergy => Math.Clamp(Ms.MidEnergy * 2.0, 0, 1);
    public double SideEnergy => Math.Clamp(Ms.SideEnergy * 2.0, 0, 1);

    public Points SideResponseCurve
    {
        get
        {
            const double sampleRate = 48000;
            const int points = 64;
            const double minHz = 30;
            const double maxHz = 20000;
            const double topDb = 6;
            const double botDb = -24;
            const double width = 160;
            const double height = 40;

            var hp = BiquadCoefficients.ComputeEq(EqBandType.HighPass, Ms.SideLowCutHz, 0.707, 0, sampleRate);
            var shelf = BiquadCoefficients.ComputeEq(EqBandType.HighShelf, Ms.SideAirHz, 0.7, Ms.SideAirDb, sampleRate);
            var curve = new Points();
            for (var i = 0; i < points; i++)
            {
                var t = i / (double)(points - 1);
                var hz = minHz * Math.Pow(maxHz / minHz, t);
                var db = hp.MagnitudeDb(hz, sampleRate) + shelf.MagnitudeDb(hz, sampleRate);
                var x = t * width;
                var y = Math.Clamp((topDb - db) / (topDb - botDb), 0, 1) * height;
                curve.Add(new Point(x, y));
            }
            return curve;
        }
    }

    private void NotifySolos()
    {
        OnPropertyChanged(nameof(SoloMid));
        OnPropertyChanged(nameof(SoloSide));
        OnPropertyChanged(nameof(MidEnergy));
        OnPropertyChanged(nameof(SideEnergy));
    }

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(SoloMid));
        OnPropertyChanged(nameof(SoloSide));
        OnPropertyChanged(nameof(MidEnergy));
        OnPropertyChanged(nameof(SideEnergy));
        OnPropertyChanged(nameof(SideResponseCurve));
    }
}

public sealed class MultibandCompressorEffectViewModel : EffectViewModel
{
    public MultibandCompressorEffectViewModel(MultibandCompressorEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
    }

    private MultibandCompressorEffect Multiband => (MultibandCompressorEffect)Effect;
    public string BandInfo => Loc.Format("Mastering_Card_Multiband_Crossovers",
        Multiband.LowCrossoverHz, Multiband.HighCrossoverHz);
    public string PresetHint => MasteringPresetBank.GetMultiband(Multiband.MasteringPresetIndex).Description;
    public double LowEnergy => Math.Clamp(Multiband.LowEnergy * 2, 0, 1);
    public double MidEnergy => Math.Clamp(Multiband.MidEnergy * 2, 0, 1);
    public double HighEnergy => Math.Clamp(Multiband.HighEnergy * 2, 0, 1);
    public string LowGr => $"{Multiband.LowGainReductionDb:0.0} dB";
    public string MidGr => $"{Multiband.MidGainReductionDb:0.0} dB";
    public string HighGr => $"{Multiband.HighGainReductionDb:0.0} dB";

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(PresetHint));
        OnPropertyChanged(nameof(BandInfo));
        OnPropertyChanged(nameof(LowEnergy));
        OnPropertyChanged(nameof(MidEnergy));
        OnPropertyChanged(nameof(HighEnergy));
        OnPropertyChanged(nameof(LowGr));
        OnPropertyChanged(nameof(MidGr));
        OnPropertyChanged(nameof(HighGr));
    }
}

public sealed class ClipperEffectViewModel : EffectViewModel
{
    public ClipperEffectViewModel(ClipperEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
    }

    public ClipperEffect Clip => (ClipperEffect)Effect;

    public string Hint => Loc.Format("Mastering_Card_Clipper_Hint", Clip.CeilingDb);
    public Points TransferCurve
    {
        get
        {
            // Non-null Points required — Polyline.CreateDefiningGeometry throws on null.
            var points = new Points();
            var drive = Math.Pow(10, Clip.DriveDb / 20);
            var ceiling = Math.Pow(10, Clip.CeilingDb / 20);
            for (var i = 0; i <= 32; i++)
            {
                var x = i / 32.0 * 2 - 1;
                var y = Math.Tanh(x * drive / ceiling) * ceiling;
                points.Add(new Point(i * 4, 24 - y * 20));
            }
            return points;
        }
    }
    public Points RecentWaveform
    {
        get
        {
            var samples = new float[128];
            var count = Clip.CaptureRecent(samples);
            var points = new Points();
            for (var i = 0; i < count; i++)
                points.Add(new Point(i, 20 - samples[i] * 16));
            return points;
        }
    }
    public double CeilingY => 20 - Math.Pow(10, Clip.CeilingDb / 20) * 16;
    public double CeilingBottomY => 20 + Math.Pow(10, Clip.CeilingDb / 20) * 16;

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(TransferCurve));
        OnPropertyChanged(nameof(RecentWaveform));
        OnPropertyChanged(nameof(CeilingY));
        OnPropertyChanged(nameof(CeilingBottomY));
    }
}

public sealed class StereoWidthEffectViewModel : EffectViewModel
{
    public StereoWidthEffectViewModel(StereoWidthEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
    }

    public StereoWidthEffect WidthFx => (StereoWidthEffect)Effect;

    public double WidthNormalized => Math.Clamp(WidthFx.Width / 2.0, 0, 1);
    public double CorrelationNormalized => (WidthFx.Correlation + 1) * 0.5;
    public string CorrelationText => $"Correlation {WidthFx.Correlation:+0.00;-0.00;0.00}";

    public string Hint => Loc.Get("Mastering_Card_Width_Hint");

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(WidthNormalized));
        OnPropertyChanged(nameof(CorrelationNormalized));
        OnPropertyChanged(nameof(CorrelationText));
    }
}

public sealed class PeakLimiterEffectViewModel : EffectViewModel
{
    private readonly Queue<double> _history = new();
    private readonly Func<double>? _deliveryCeilingDbTp;
    public PeakLimiterEffectViewModel(PeakLimiterEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown,
        Func<double>? deliveryCeilingDbTp = null)
        : base(effect, remove, moveUp, moveDown)
    {
        _deliveryCeilingDbTp = deliveryCeilingDbTp;
    }

    public PeakLimiterEffect Limiter => (PeakLimiterEffect)Effect;

    public string PresetHint
    {
        get
        {
            var p = MasteringPresetBank.GetLimiter(Limiter.MasteringPresetIndex);
            return Limiter.OversampleIndex == 0
                ? $"{p.Description} {Loc.Get("Mastering_Card_Limiter_OversampleWarning", "1× mode detects sample peaks only; use oversampling for true-peak delivery.")}"
                : p.Description;
        }
    }

    public string CeilingLine
    {
        get
        {
            var baseText = $"Ceiling {Limiter.CeilingDb:0.0} dBFS · Spectral {(Limiter.SpectralLimiter ? "on" : "off")}";
            return _deliveryCeilingDbTp is null
                ? baseText
                : $"{baseText} · Delivery {_deliveryCeilingDbTp():0.0} dBTP";
        }
    }
    public double OutputLevel => Math.Clamp(Limiter.OutputPeak, 0, 1);
    public double CeilingLevel => Math.Clamp(Math.Pow(10, Limiter.CeilingDb / 20), 0, 1);
    public double DeliveryCeilingLevel => Math.Clamp(Math.Pow(10, (_deliveryCeilingDbTp?.Invoke() ?? -1) / 20), 0, 1);
    public double DeliveryCeilingY => 24 - DeliveryCeilingLevel * 24;
    public Points PeakHistory
    {
        get
        {
            var points = new Points();
            var i = 0;
            foreach (var v in _history)
            {
                points.Add(new Point(i * 4, 24 - Math.Clamp(v, 0, 12) * 2));
                i++;
            }
            return points;
        }
    }

    public override void Refresh()
    {
        base.Refresh();
        _history.Enqueue(-Limiter.GainReductionDb);
        while (_history.Count > 32) _history.Dequeue();
        OnPropertyChanged(nameof(PresetHint));
        OnPropertyChanged(nameof(CeilingLine));
        OnPropertyChanged(nameof(OutputLevel));
        OnPropertyChanged(nameof(CeilingLevel));
        OnPropertyChanged(nameof(DeliveryCeilingLevel));
        OnPropertyChanged(nameof(DeliveryCeilingY));
        OnPropertyChanged(nameof(PeakHistory));
    }
}
