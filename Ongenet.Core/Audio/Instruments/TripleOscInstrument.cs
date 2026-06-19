using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A triple-oscillator subtractive synth in the spirit of FL Studio's 3x Osc. Three
/// <see cref="WaveOscillator"/>s (each with its own waveform, coarse/fine tuning, phase offset and
/// invert) are mixed, optionally amplitude-modulated by OSC 3, then shaped by a filter and an amp
/// envelope. OSC 1 is locked at full level; OSC 2/3 mix relative to it. A loaded sample becomes the
/// "Custom" waveform (via <see cref="ISampleHost"/>). Built on the shared instrument framework.
/// </summary>
public sealed class TripleOscInstrument : PolyphonicInstrument, ISampleHost
{
    public const string TypeId = "3xosc";

    protected override string GetTypeId() => TypeId;

    private volatile float[]? _customTable;
    private volatile AudioSampleBuffer? _loadedSample; // retained so a project save can embed it
    private Parameter[]? _parameters;

    public TripleOscInstrument() : base(polyphony: 16) { }

    public override string Name => "3x Osc";

    // Per-oscillator settings (OSC 1 has no level — it is locked at 1.0).
    public int Wave1 { get; set; } = (int)OscWave.Saw;
    public int Wave2 { get; set; } = (int)OscWave.Saw;
    public int Wave3 { get; set; } = (int)OscWave.Saw;
    public double Coarse1 { get; set; }
    public double Coarse2 { get; set; }
    public double Coarse3 { get; set; }
    public double Fine1 { get; set; }   // cents
    public double Fine2 { get; set; }
    public double Fine3 { get; set; }
    public double Phase1 { get; set; }  // 0..1
    public double Phase2 { get; set; }
    public double Phase3 { get; set; }
    public bool Invert1 { get; set; }
    public bool Invert2 { get; set; }
    public bool Invert3 { get; set; }
    public double Level2 { get; set; }  // 0..1, relative to OSC 1
    public double Level3 { get; set; }

    /// <summary>When true, OSC 3 amplitude-modulates OSC 1 + 2 instead of being mixed in directly.</summary>
    public bool AmOsc3 { get; set; }

    // Filter
    public int FilterTypeIndex { get; set; }   // 0 Off, 1 LP, 2 HP, 3 BP
    public double Cutoff { get; set; } = 20000.0;
    public double Resonance { get; set; } = 0.7;

    // Amp envelope
    public double AttackSeconds { get; set; } = 0.005;
    public double DecaySeconds { get; set; } = 0.1;
    public double SustainLevel { get; set; } = 0.8;
    public double ReleaseSeconds { get; set; } = 0.2;

    public double Gain { get; set; } = 0.8;

    // --- ISampleHost: the loaded sample becomes the Custom waveform cycle ---
    public string? SampleName { get; private set; }
    public AudioSampleBuffer? CurrentSample => _loadedSample;
    internal float[]? CustomTable => _customTable;

    public void LoadSample(AudioSampleBuffer sample, string name)
    {
        _customTable = SampleMixdown.ToMono(sample);
        _loadedSample = sample;
        SampleName = name;
    }

    private static readonly string[] WaveNames = { "Sine", "Triangle", "Saw", "Square", "Noise", "Custom" };

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Wave", WaveNames, () => Wave1, i => Wave1 = i) { Group = "OSC 1" },
        new FloatParameter("Coarse", -24, 24, () => Coarse1, v => Coarse1 = Math.Round(v), "0", "st") { Group = "OSC 1" },
        new FloatParameter("Fine", -100, 100, () => Fine1, v => Fine1 = v, "0", "ct") { Group = "OSC 1" },
        new FloatParameter("Phase", 0, 1, () => Phase1, v => Phase1 = v, "0.00") { Group = "OSC 1" },
        new BoolParameter("Invert", () => Invert1, v => Invert1 = v) { Group = "OSC 1" },

        new ChoiceParameter("Wave", WaveNames, () => Wave2, i => Wave2 = i) { Group = "OSC 2" },
        new FloatParameter("Coarse", -24, 24, () => Coarse2, v => Coarse2 = Math.Round(v), "0", "st") { Group = "OSC 2" },
        new FloatParameter("Fine", -100, 100, () => Fine2, v => Fine2 = v, "0", "ct") { Group = "OSC 2" },
        new FloatParameter("Phase", 0, 1, () => Phase2, v => Phase2 = v, "0.00") { Group = "OSC 2" },
        new BoolParameter("Invert", () => Invert2, v => Invert2 = v) { Group = "OSC 2" },
        new FloatParameter("Level", 0, 1, () => Level2, v => Level2 = v, "0.00") { Group = "OSC 2" },

        new ChoiceParameter("Wave", WaveNames, () => Wave3, i => Wave3 = i) { Group = "OSC 3" },
        new FloatParameter("Coarse", -24, 24, () => Coarse3, v => Coarse3 = Math.Round(v), "0", "st") { Group = "OSC 3" },
        new FloatParameter("Fine", -100, 100, () => Fine3, v => Fine3 = v, "0", "ct") { Group = "OSC 3" },
        new FloatParameter("Phase", 0, 1, () => Phase3, v => Phase3 = v, "0.00") { Group = "OSC 3" },
        new BoolParameter("Invert", () => Invert3, v => Invert3 = v) { Group = "OSC 3" },
        new FloatParameter("Level", 0, 1, () => Level3, v => Level3 = v, "0.00") { Group = "OSC 3" },

        new BoolParameter("AM (OSC 3)", () => AmOsc3, v => AmOsc3 = v) { Group = "Mix" },

        new ChoiceParameter("Filter", new[] { "Off", "Low Pass", "High Pass", "Band Pass" },
            () => FilterTypeIndex, i => FilterTypeIndex = i) { Group = "Filter" },
        new FloatParameter("Cutoff", 20, 20000, () => Cutoff, v => Cutoff = v, "0", "Hz", skew: 3.0) { Group = "Filter" },
        new FloatParameter("Reso", 0.5, 12, () => Resonance, v => Resonance = v, "0.0") { Group = "Filter" },

        new FloatParameter("Attack", 0.001, 4, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Decay", 0.001, 4, () => DecaySeconds, v => DecaySeconds = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Sustain", 0, 1, () => SustainLevel, v => SustainLevel = v, "0.00") { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 6, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },

        new FloatParameter("Gain", 0, 1, () => Gain, v => Gain = v, "0.00") { Group = "Output" }
    };

    protected override Voice CreateVoice() => new OscVoice(this);

    public override IInstrument Clone()
    {
        var copy = new TripleOscInstrument
        {
            Wave1 = Wave1, Wave2 = Wave2, Wave3 = Wave3,
            Coarse1 = Coarse1, Coarse2 = Coarse2, Coarse3 = Coarse3,
            Fine1 = Fine1, Fine2 = Fine2, Fine3 = Fine3,
            Phase1 = Phase1, Phase2 = Phase2, Phase3 = Phase3,
            Invert1 = Invert1, Invert2 = Invert2, Invert3 = Invert3,
            Level2 = Level2, Level3 = Level3, AmOsc3 = AmOsc3,
            FilterTypeIndex = FilterTypeIndex, Cutoff = Cutoff, Resonance = Resonance,
            AttackSeconds = AttackSeconds, DecaySeconds = DecaySeconds, SustainLevel = SustainLevel, ReleaseSeconds = ReleaseSeconds,
            Gain = Gain
        };
        copy._customTable = _customTable;
        copy._loadedSample = _loadedSample;
        copy.SampleName = SampleName;
        return copy;
    }

    private sealed class OscVoice : Voice
    {
        private const float VoiceGain = 0.25f;

        private readonly TripleOscInstrument _inst;
        private readonly WaveOscillator _osc1 = new();
        private readonly WaveOscillator _osc2 = new();
        private readonly WaveOscillator _osc3 = new();
        private readonly AdsrEnvelope _env = new();
        private Biquad _filter;
        private float _velocity;
        private static uint _seed = 1;

        public OscVoice(TripleOscInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            var sr = format.SampleRate;
            var seed = _seed++ * 2654435761u + (uint)midiNote;

            Setup(_osc1, sr, seed); Setup(_osc2, sr, seed ^ 0xABCDu); Setup(_osc3, sr, seed ^ 0x1234u);

            _env.SetSampleRate(sr);
            _env.AttackSeconds = _inst.AttackSeconds;
            _env.DecaySeconds = _inst.DecaySeconds;
            _env.SustainLevel = _inst.SustainLevel;
            _env.ReleaseSeconds = _inst.ReleaseSeconds;
            _env.Gate();
            _filter.Reset();
        }

        private void Setup(WaveOscillator osc, int sr, uint seed)
        {
            osc.SetSampleRate(sr);
            osc.CustomTable = _inst.CustomTable;
            osc.SeedNoise(seed);
            osc.ResetPhase(0);
        }

        public override void Release() => _env.Release();

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;
            var baseFreq = MusicalMath.NoteToFrequency(Note);

            // Per-block: apply live waveforms, tuning and phase/invert, then set frequencies.
            _osc1.Wave = (OscWave)_inst.Wave1; _osc1.PhaseOffset = _inst.Phase1; _osc1.Invert = _inst.Invert1;
            _osc2.Wave = (OscWave)_inst.Wave2; _osc2.PhaseOffset = _inst.Phase2; _osc2.Invert = _inst.Invert2;
            _osc3.Wave = (OscWave)_inst.Wave3; _osc3.PhaseOffset = _inst.Phase3; _osc3.Invert = _inst.Invert3;
            _osc1.SetFrequency(baseFreq * MusicalMath.SemitonesToRatio(_inst.Coarse1 + _inst.Fine1 / 100.0));
            _osc2.SetFrequency(baseFreq * MusicalMath.SemitonesToRatio(_inst.Coarse2 + _inst.Fine2 / 100.0));
            _osc3.SetFrequency(baseFreq * MusicalMath.SemitonesToRatio(_inst.Coarse3 + _inst.Fine3 / 100.0));

            var am = _inst.AmOsc3;
            var lvl2 = (float)_inst.Level2;
            var lvl3 = (float)_inst.Level3;
            var amp = (float)(_inst.Gain * VoiceGain) * _velocity;

            var filterOn = _inst.FilterTypeIndex > 0;
            var mode = _inst.FilterTypeIndex switch
            {
                1 => FilterMode.LowPass,
                2 => FilterMode.HighPass,
                3 => FilterMode.BandPass,
                _ => FilterMode.Bypass
            };
            var coeffs = filterOn
                ? BiquadCoefficients.Compute(mode, AudioMath.Clamp(_inst.Cutoff, 20, sr * 0.45), _inst.Resonance, sr)
                : BiquadCoefficients.Identity;

            for (var frame = 0; frame < frames; frame++)
            {
                var o1 = _osc1.Next();
                var o2 = _osc2.Next();
                var o3 = _osc3.Next();

                float mix;
                if (am)
                {
                    // OSC 3 acts as a (unipolar) amplitude modulator of OSC 1 + 2.
                    var mod = o3 * 0.5f + 0.5f;
                    mix = (o1 + o2 * lvl2) * mod;
                }
                else
                {
                    mix = o1 + o2 * lvl2 + o3 * lvl3;
                }

                var sample = mix * _env.Process() * amp;
                if (filterOn) sample = (float)_filter.Process(coeffs, sample);

                var baseIndex = frame * channels;
                for (var c = 0; c < channels; c++) buffer[baseIndex + c] += sample;

                if (!_env.IsActive) { IsActive = false; return; }
            }
        }
    }
}
