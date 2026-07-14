using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A polyphonic Hammond-style drawbar organ. Each voice runs a <see cref="DrawbarOrgan"/> through a
/// simple amp envelope, with optional vibrato and a short percussion transient on note attacks.
/// </summary>
public sealed class OrganInstrument : PolyphonicInstrument
{
    public const string TypeId = "organ";

    protected override string GetTypeId() => TypeId;

    private static readonly string[] DrawbarNames =
    {
        "16′", "5⅓′", "8′", "4′", "2⅔′", "2′", "1⅗′", "1⅓′", "1′"
    };

    private Parameter[]? _parameters;

    public OrganInstrument() : base(polyphony: 16)
    {
        Drawbars = new double[DrawbarOrgan.DrawbarCount];
        Drawbars[0] = 6.4;
        Drawbars[1] = 6.4;
        Drawbars[2] = 8.0;
    }

    public override string Name => "Organ";

    /// <summary>Drawbar levels in Hammond 0..8 notation (mapped to 0..1 internally).</summary>
    public double[] Drawbars { get; }

    public bool PercussionOn { get; set; }
    public double PercussionLevel { get; set; } = 0.6;
    public double PercussionDecayMs { get; set; } = 80;

    public bool VibratoOn { get; set; } = true;
    public double VibratoRate { get; set; } = 5.5;
    public double VibratoDepth { get; set; } = 35;

    public double AttackSeconds { get; set; } = 0.002;
    public double ReleaseSeconds { get; set; } = 0.08;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= BuildParameters();

    private Parameter[] BuildParameters()
    {
        var list = new List<Parameter>(DrawbarOrgan.DrawbarCount + 10);
        for (var i = 0; i < DrawbarOrgan.DrawbarCount; i++)
        {
            var idx = i;
            list.Add(new FloatParameter(DrawbarNames[i], 0, 8,
                () => Drawbars[idx], v => Drawbars[idx] = v, "0.0") { Group = "Drawbars" });
        }

        list.Add(new BoolParameter("On", () => PercussionOn, v => PercussionOn = v) { Group = "Percussion" });
        list.Add(new FloatParameter("Level", 0, 1, () => PercussionLevel, v => PercussionLevel = v, "0.00") { Group = "Percussion" });
        list.Add(new FloatParameter("Decay", 5, 300, () => PercussionDecayMs, v => PercussionDecayMs = v, "0", "ms", skew: 2.0) { Group = "Percussion" });

        list.Add(new BoolParameter("On", () => VibratoOn, v => VibratoOn = v) { Group = "Vibrato" });
        list.Add(new FloatParameter("Rate", 0.1, 12, () => VibratoRate, v => VibratoRate = v, "0.0", "Hz") { Group = "Vibrato" });
        list.Add(new FloatParameter("Depth", 0, 100, () => VibratoDepth, v => VibratoDepth = v, "0", "ct") { Group = "Vibrato" });

        list.Add(new FloatParameter("Attack", 0.001, 0.5, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s") { Group = "Amp Envelope" });
        list.Add(new FloatParameter("Release", 0.001, 2.0, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s") { Group = "Amp Envelope" });
        return list.ToArray();
    }

    protected override Voice CreateVoice() => new OrganVoice(this);

    public override IInstrument Clone()
    {
        var c = new OrganInstrument
        {
            PercussionOn = PercussionOn,
            PercussionLevel = PercussionLevel,
            PercussionDecayMs = PercussionDecayMs,
            VibratoOn = VibratoOn,
            VibratoRate = VibratoRate,
            VibratoDepth = VibratoDepth,
            AttackSeconds = AttackSeconds,
            ReleaseSeconds = ReleaseSeconds
        };
        Array.Copy(Drawbars, c.Drawbars, Drawbars.Length);
        return c;
    }

    private sealed class OrganVoice : Voice
    {
        private const float VoiceGain = 0.2f;

        private readonly OrganInstrument _inst;
        private readonly DrawbarOrgan _organ = new();
        private readonly DahdsrEnvelope _envelope = new();
        private CurveEnvelope _percEnv;
        private double _percPhase;
        private double _percInc;
        private long _elapsed;
        private float _velocity;
        private bool _percActive;

        public OrganVoice(OrganInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;

            var sr = format.SampleRate;
            var freq = MusicalMath.NoteToFrequency(midiNote);
            _organ.Configure(freq, sr);
            for (var d = 0; d < DrawbarOrgan.DrawbarCount; d++)
                _organ.SetDrawbar(d, _inst.Drawbars[d] / 8.0);
            _organ.SetVibrato(_inst.VibratoOn ? _inst.VibratoRate : 0, _inst.VibratoOn ? _inst.VibratoDepth : 0);
            _organ.Reset();

            _envelope.SetSampleRate(sr);
            _envelope.DelaySeconds = 0;
            _envelope.StartLevel = 0;
            _envelope.AttackSeconds = _inst.AttackSeconds;
            _envelope.HoldSeconds = 0;
            _envelope.DecaySeconds = 0;
            _envelope.SustainLevel = 1.0;
            _envelope.ReleaseSeconds = _inst.ReleaseSeconds;
            _envelope.Gate();

            _percActive = _inst.PercussionOn && _inst.PercussionLevel > 0.001;
            _percEnv = new CurveEnvelope(0, 0.0005, 0, _inst.PercussionDecayMs / 1000.0, 0.9);
            _percPhase = 0;
            _percInc = freq * 2.0 / sr;
            _elapsed = 0;
        }

        public override void Release() => _envelope.Release();

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var percLvl = (float)_inst.PercussionLevel;

            for (var frame = 0; frame < frames; frame++)
            {
                for (var d = 0; d < DrawbarOrgan.DrawbarCount; d++)
                    _organ.SetDrawbar(d, _inst.Drawbars[d] / 8.0);
                _organ.SetVibrato(_inst.VibratoOn ? _inst.VibratoRate : 0, _inst.VibratoOn ? _inst.VibratoDepth : 0);

                var sample = _organ.Process() * _envelope.Process() * _velocity * VoiceGain;

                if (_percActive)
                {
                    var pe = (float)_percEnv.Evaluate(_elapsed / (double)Format.SampleRate);
                    if (pe > 0)
                    {
                        // A bright 4′ partial burst — the classic percussion harmonic.
                        sample += (float)Math.Sin(2.0 * Math.PI * _percPhase) * pe * percLvl * _velocity * VoiceGain * 0.35f;
                        _percPhase += _percInc;
                        if (_percPhase >= 1.0) _percPhase -= 1.0;
                    }
                }

                _elapsed++;

                var baseIndex = frame * channels;
                for (var ch = 0; ch < channels; ch++)
                    buffer[baseIndex + ch] += sample;

                if (!_envelope.IsActive)
                {
                    IsActive = false;
                    return;
                }
            }
        }
    }
}
