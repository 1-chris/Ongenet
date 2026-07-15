using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A mastering-grade mid/side equaliser — the "Japanese trance secret". It decodes the stereo image
/// into Mid (centre) and Side (the far L/R edges), then processes the Side channel only: a high-pass
/// that mono-folds the sub-bass into the dead centre for maximum club punch, plus a gentle high-shelf
/// "air" boost that pushes the supersaw sheen and wide reverbs outward, making the track feel massive.
/// Re-encodes to stereo afterwards. On a mono signal it is a transparent pass-through.
/// </summary>
public sealed class MidSideEqEffect : IAudioEffect
{
    public const string TypeId = "midside_eq";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    /// <summary>Side-channel low cut (Hz): everything below folds to mono centre.</summary>
    public double SideLowCutHz { get; set; } = 120.0;

    /// <summary>Side-channel "air" high-shelf frequency (Hz).</summary>
    public double SideAirHz { get; set; } = 9000.0;

    /// <summary>Side-channel "air" high-shelf gain (dB) — a gentle 1–1.5 dB widens the top end.</summary>
    public double SideAirDb { get; set; } = 1.2;

    /// <summary>When true, output mid only (side silenced) for audition.</summary>
    public bool SoloMid { get; set; }

    /// <summary>When true, output side only (mid silenced) for audition.</summary>
    public bool SoloSide { get; set; }

    public double MidEnergy { get; private set; }
    public double SideEnergy { get; private set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;

    private readonly EqBand _sideHp = new(EqBandType.HighPass, 120.0, 0.0, 0.707);
    private readonly EqBand _sideAir = new(EqBandType.HighShelf, 9000.0, 1.2, 0.7);

    public string Name => "Mid/Side EQ";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Side Low Cut", 20.0, 400.0, () => SideLowCutHz, v => SideLowCutHz = v, "0", "Hz", 2.0),
        new FloatParameter("Air Freq", 2000.0, 16000.0, () => SideAirHz, v => SideAirHz = v, "0", "Hz", 2.0),
        new FloatParameter("Air Gain", 0.0, 6.0, () => SideAirDb, v => SideAirDb = v, "0.#", "dB"),
        new BoolParameter("Solo Mid", () => SoloMid, v =>
        {
            SoloMid = v;
            if (v) SoloSide = false;
        }) { Group = "Audition" },
        new BoolParameter("Solo Side", () => SoloSide, v =>
        {
            SoloSide = v;
            if (v) SoloMid = false;
        }) { Group = "Audition" }
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _sideHp.Prepare(1);
        _sideAir.Prepare(1);
    }

    public IAudioEffect Clone() => new MidSideEqEffect
    {
        Enabled = Enabled, SideLowCutHz = SideLowCutHz, SideAirHz = SideAirHz, SideAirDb = SideAirDb,
        SoloMid = SoloMid, SoloSide = SoloSide
    };

    public void Process(Span<float> buffer)
    {
        if (_channels < 2) return;

        _sideHp.Frequency = SideLowCutHz;
        _sideAir.Frequency = SideAirHz;
        _sideAir.GainDb = SideAirDb;
        _sideHp.EnsureCoeffs(_sampleRate);
        _sideAir.EnsureCoeffs(_sampleRate);

        var frames = buffer.Length / _channels;
        double midSum = 0, sideSum = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * _channels;
            var l = buffer[i];
            var r = buffer[i + 1];

            var mid = (l + r) * 0.5f;
            var side = (l - r) * 0.5f;

            side = _sideHp.Process(0, side);
            side = _sideAir.Process(0, side);
            midSum += mid * mid;
            sideSum += side * side;

            if (SoloMid) { buffer[i] = mid; buffer[i + 1] = mid; }
            else if (SoloSide) { buffer[i] = side; buffer[i + 1] = -side; }
            else { buffer[i] = mid + side; buffer[i + 1] = mid - side; }
        }
        if (frames > 0)
        {
            MidEnergy = MidEnergy * 0.75 + Math.Sqrt(midSum / frames) * 0.25;
            SideEnergy = SideEnergy * 0.75 + Math.Sqrt(sideSum / frames) * 0.25;
        }
    }
}
