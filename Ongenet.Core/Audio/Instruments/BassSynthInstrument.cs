using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A compact subtractive bass synth: unison-capable <see cref="WaveOscillator"/> tone plus a sine
/// sub, resonant low-pass with its own ADSR, soft drive via <see cref="WaveShaper"/>, and an amp
/// envelope. Low polyphony keeps stacked notes from muddying the low end.
/// </summary>
public sealed class BassSynthInstrument : PolyphonicInstrument, IPresetProvider
{
    public const string TypeId = "basssynth";

    private const int MaxUnison = 7;

    protected override string GetTypeId() => TypeId;

    private Parameter[]? _parameters;

    public BassSynthInstrument() : base(polyphony: 4) => Reset();

    public override string Name => "Bass Synth";

    public int Wave { get; set; }
    public double SubLevel { get; set; }
    public double Cutoff { get; set; }
    public double Resonance { get; set; }
    public double FilterEnvAmount { get; set; }

    public double AttackSeconds { get; set; }
    public double DecaySeconds { get; set; }
    public double SustainLevel { get; set; }
    public double ReleaseSeconds { get; set; }

    public double FAttackSeconds { get; set; }
    public double FDecaySeconds { get; set; }
    public double FSustainLevel { get; set; }
    public double FReleaseSeconds { get; set; }

    public double Drive { get; set; }
    public double Gain { get; set; }
    public int Unison { get; set; }

    private static readonly string[] WaveNames = { "Sine", "Triangle", "Saw", "Square" };

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Wave", WaveNames, () => Wave, i => Wave = i) { Group = "Oscillator" },
        new FloatParameter("Sub", 0, 1, () => SubLevel, v => SubLevel = v, "0.00") { Group = "Oscillator" },
        new FloatParameter("Unison", 1, MaxUnison, () => Unison, v => Unison = (int)Math.Round(v), "0") { Group = "Oscillator" },

        new FloatParameter("Cutoff", 20, 12000, () => Cutoff, v => Cutoff = v, "0", "Hz", skew: 3.0) { Group = "Filter" },
        new FloatParameter("Reso", 0.5, 16, () => Resonance, v => Resonance = v, "0.0", "Q", skew: 2.0) { Group = "Filter" },
        new FloatParameter("Env Amt", -1, 1, () => FilterEnvAmount, v => FilterEnvAmount = v, "0.00") { Group = "Filter" },

        new FloatParameter("Attack", 0.001, 2, () => FAttackSeconds, v => FAttackSeconds = v, "0.000", "s", skew: 2.0) { Group = "Filter Envelope" },
        new FloatParameter("Decay", 0.001, 2, () => FDecaySeconds, v => FDecaySeconds = v, "0.000", "s", skew: 2.0) { Group = "Filter Envelope" },
        new FloatParameter("Sustain", 0, 1, () => FSustainLevel, v => FSustainLevel = v, "0.00") { Group = "Filter Envelope" },
        new FloatParameter("Release", 0.001, 3, () => FReleaseSeconds, v => FReleaseSeconds = v, "0.000", "s", skew: 2.0) { Group = "Filter Envelope" },

        new FloatParameter("Attack", 0.001, 2, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Decay", 0.001, 2, () => DecaySeconds, v => DecaySeconds = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Sustain", 0, 1, () => SustainLevel, v => SustainLevel = v, "0.00") { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 3, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },

        new FloatParameter("Drive", 0, 1, () => Drive, v => Drive = v, "0.00") { Group = "Output" },
        new FloatParameter("Gain", 0, 1, () => Gain, v => Gain = v, "0.00") { Group = "Output" }
    };

    protected override Voice CreateVoice() => new BassVoice(this);

    public override IInstrument Clone()
    {
        var c = new BassSynthInstrument();
        CopyStateTo(c);
        return c;
    }

    private void CopyStateTo(BassSynthInstrument c)
    {
        c.Wave = Wave;
        c.SubLevel = SubLevel;
        c.Cutoff = Cutoff;
        c.Resonance = Resonance;
        c.FilterEnvAmount = FilterEnvAmount;
        c.AttackSeconds = AttackSeconds;
        c.DecaySeconds = DecaySeconds;
        c.SustainLevel = SustainLevel;
        c.ReleaseSeconds = ReleaseSeconds;
        c.FAttackSeconds = FAttackSeconds;
        c.FDecaySeconds = FDecaySeconds;
        c.FSustainLevel = FSustainLevel;
        c.FReleaseSeconds = FReleaseSeconds;
        c.Drive = Drive;
        c.Gain = Gain;
        c.Unison = Unison;
    }

    private static readonly string[] PresetNamesList =
    {
        "Init", "Deep Sub", "Reese", "Acid Pulse", "Warm Square",
        "Plucky Bass", "Growl Drive", "Soft Sine", "Funky Slap"
    };

    public IReadOnlyList<string> PresetNames => PresetNamesList;

    public void LoadPreset(int index)
    {
        switch (index)
        {
            case 1: DeepSub(); break;
            case 2: Reese(); break;
            case 3: AcidPulse(); break;
            case 4: WarmSquare(); break;
            case 5: PluckyBass(); break;
            case 6: GrowlDrive(); break;
            case 7: SoftSine(); break;
            case 8: FunkySlap(); break;
            default: Reset(); break;
        }
    }

    private void Reset()
    {
        Wave = (int)OscWave.Saw;
        SubLevel = 0.45;
        Cutoff = 600;
        Resonance = 1.2;
        FilterEnvAmount = 0.45;
        AttackSeconds = 0.005;
        DecaySeconds = 0.18;
        SustainLevel = 0.65;
        ReleaseSeconds = 0.12;
        FAttackSeconds = 0.005;
        FDecaySeconds = 0.22;
        FSustainLevel = 0.25;
        FReleaseSeconds = 0.15;
        Drive = 0.15;
        Gain = 0.8;
        Unison = 1;
    }

    private void DeepSub()
    {
        Reset();
        Wave = (int)OscWave.Sine;
        SubLevel = 0.85;
        Cutoff = 280;
        Resonance = 0.7;
        FilterEnvAmount = 0.15;
        AttackSeconds = 0.01;
        DecaySeconds = 0.25;
        SustainLevel = 0.8;
        ReleaseSeconds = 0.2;
        FAttackSeconds = 0.01;
        FDecaySeconds = 0.3;
        FSustainLevel = 0.5;
        Drive = 0.05;
        Gain = 0.9;
        Unison = 1;
    }

    private void Reese()
    {
        Reset();
        Wave = (int)OscWave.Saw;
        SubLevel = 0.55;
        Cutoff = 900;
        Resonance = 1.8;
        FilterEnvAmount = 0.35;
        AttackSeconds = 0.02;
        DecaySeconds = 0.4;
        SustainLevel = 0.75;
        ReleaseSeconds = 0.25;
        FAttackSeconds = 0.03;
        FDecaySeconds = 0.5;
        FSustainLevel = 0.4;
        Drive = 0.35;
        Gain = 0.75;
        Unison = 5;
    }

    private void AcidPulse()
    {
        Reset();
        Wave = (int)OscWave.Square;
        SubLevel = 0.2;
        Cutoff = 700;
        Resonance = 8.0;
        FilterEnvAmount = 0.75;
        AttackSeconds = 0.002;
        DecaySeconds = 0.14;
        SustainLevel = 0.15;
        ReleaseSeconds = 0.06;
        FAttackSeconds = 0.002;
        FDecaySeconds = 0.16;
        FSustainLevel = 0.05;
        FReleaseSeconds = 0.08;
        Drive = 0.25;
        Gain = 0.82;
        Unison = 1;
    }

    private void WarmSquare()
    {
        Reset();
        Wave = (int)OscWave.Square;
        SubLevel = 0.5;
        Cutoff = 1100;
        Resonance = 1.0;
        FilterEnvAmount = 0.3;
        AttackSeconds = 0.008;
        DecaySeconds = 0.22;
        SustainLevel = 0.7;
        ReleaseSeconds = 0.18;
        FAttackSeconds = 0.01;
        FDecaySeconds = 0.28;
        FSustainLevel = 0.35;
        Drive = 0.2;
        Gain = 0.78;
        Unison = 2;
    }

    private void PluckyBass()
    {
        Reset();
        Wave = (int)OscWave.Triangle;
        SubLevel = 0.35;
        Cutoff = 1800;
        Resonance = 2.5;
        FilterEnvAmount = 0.7;
        AttackSeconds = 0.001;
        DecaySeconds = 0.09;
        SustainLevel = 0.1;
        ReleaseSeconds = 0.08;
        FAttackSeconds = 0.001;
        FDecaySeconds = 0.1;
        FSustainLevel = 0.0;
        FReleaseSeconds = 0.08;
        Drive = 0.1;
        Gain = 0.85;
        Unison = 1;
    }

    private void GrowlDrive()
    {
        Reset();
        Wave = (int)OscWave.Saw;
        SubLevel = 0.4;
        Cutoff = 500;
        Resonance = 3.5;
        FilterEnvAmount = 0.55;
        AttackSeconds = 0.004;
        DecaySeconds = 0.2;
        SustainLevel = 0.55;
        ReleaseSeconds = 0.15;
        FAttackSeconds = 0.004;
        FDecaySeconds = 0.18;
        FSustainLevel = 0.2;
        Drive = 0.7;
        Gain = 0.7;
        Unison = 3;
    }

    private void SoftSine()
    {
        Reset();
        Wave = (int)OscWave.Sine;
        SubLevel = 0.6;
        Cutoff = 450;
        Resonance = 0.6;
        FilterEnvAmount = 0.1;
        AttackSeconds = 0.03;
        DecaySeconds = 0.35;
        SustainLevel = 0.85;
        ReleaseSeconds = 0.3;
        FAttackSeconds = 0.04;
        FDecaySeconds = 0.4;
        FSustainLevel = 0.6;
        Drive = 0.0;
        Gain = 0.88;
        Unison = 1;
    }

    private void FunkySlap()
    {
        Reset();
        Wave = (int)OscWave.Triangle;
        SubLevel = 0.25;
        Cutoff = 2500;
        Resonance = 1.5;
        FilterEnvAmount = 0.85;
        AttackSeconds = 0.001;
        DecaySeconds = 0.12;
        SustainLevel = 0.2;
        ReleaseSeconds = 0.1;
        FAttackSeconds = 0.001;
        FDecaySeconds = 0.08;
        FSustainLevel = 0.1;
        FReleaseSeconds = 0.1;
        Drive = 0.3;
        Gain = 0.8;
        Unison = 1;
    }

    private sealed class BassVoice : Voice
    {
        private const float VoiceGain = 0.28f;
        private const double DetuneCents = 12.0;

        private readonly BassSynthInstrument _inst;
        private readonly UnisonOscillator _unison = new(MaxUnison);
        private readonly WaveOscillator _sub = new() { Wave = OscWave.Sine };
        private readonly AdsrEnvelope _amp = new();
        private readonly AdsrEnvelope _filt = new();
        private Biquad _filter;
        private float _velocity;
        private static uint _seed = 1;

        public BassVoice(BassSynthInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            var sr = format.SampleRate;
            var freq = MusicalMath.NoteToFrequency(midiNote);

            _unison.SetSampleRate(sr);
            _unison.Seed(_seed++ * 2654435761u + (uint)midiNote);
            _unison.Wave = (OscWave)Math.Clamp(_inst.Wave, 0, 3);
            var voices = Math.Clamp(_inst.Unison, 1, MaxUnison);
            // Keep bass mostly centred — width grows gently with unison count.
            var width = voices <= 1 ? 0.0 : 0.25;
            _unison.Configure(voices, DetuneCents, width, blend: 0.7);
            _unison.SetBaseFrequency(freq);

            _sub.SetSampleRate(sr);
            _sub.SetFrequency(freq * 0.5);
            _sub.ResetPhase();

            _amp.SetSampleRate(sr);
            _amp.AttackSeconds = _inst.AttackSeconds;
            _amp.DecaySeconds = _inst.DecaySeconds;
            _amp.SustainLevel = _inst.SustainLevel;
            _amp.ReleaseSeconds = _inst.ReleaseSeconds;
            _amp.Gate();

            _filt.SetSampleRate(sr);
            _filt.AttackSeconds = _inst.FAttackSeconds;
            _filt.DecaySeconds = _inst.FDecaySeconds;
            _filt.SustainLevel = _inst.FSustainLevel;
            _filt.ReleaseSeconds = _inst.FReleaseSeconds;
            _filt.Gate();

            _filter.Reset();
        }

        public override void Release()
        {
            _amp.Release();
            _filt.Release();
        }

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;
            var maxCut = sr * 0.45;
            var subLvl = (float)_inst.SubLevel;
            var driveAmt = (float)(1.0 + Math.Clamp(_inst.Drive, 0, 1) * 7.0);
            var ampScale = (float)(_inst.Gain * VoiceGain) * _velocity;
            var freq = MusicalMath.NoteToFrequency(Note);

            _unison.Wave = (OscWave)Math.Clamp(_inst.Wave, 0, 3);
            var voices = Math.Clamp(_inst.Unison, 1, MaxUnison);
            var width = voices <= 1 ? 0.0 : 0.25;
            _unison.Configure(voices, DetuneCents, width, blend: 0.7);
            _unison.SetBaseFrequency(freq);
            _sub.SetFrequency(freq * 0.5);

            for (var frame = 0; frame < frames; frame++)
            {
                _unison.Render(out var left, out var right);
                var osc = (left + right) * 0.5f;
                var mix = osc + _sub.Next() * subLvl;

                var fEnv = _filt.Process();
                var octaves = _inst.FilterEnvAmount * 4.0 * fEnv;
                var cutoff = AudioMath.Clamp(_inst.Cutoff * Math.Pow(2.0, octaves), 20.0, maxCut);
                var coeffs = BiquadCoefficients.Compute(FilterMode.LowPass, cutoff, _inst.Resonance, sr);
                var filtered = (float)_filter.Process(coeffs, mix);

                float driven = filtered;
                if (_inst.Drive > 1e-4)
                    driven = WaveShaper.Shape(filtered, ShaperType.Tanh, driveAmt);

                var sample = driven * _amp.Process() * ampScale;

                var baseIndex = frame * channels;
                for (var c = 0; c < channels; c++)
                    buffer[baseIndex + c] += sample;

                if (!_amp.IsActive)
                {
                    IsActive = false;
                    return;
                }
            }
        }
    }
}
