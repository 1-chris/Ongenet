using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Perca — a noise-percussion synthesizer for claps, hi-hats and snaps (the companion to
/// <see cref="KickaInstrument"/>, which covers kicks). Each hit is a one-shot: a noise source
/// (optionally darkened by a colour low-pass) mixed with a tonal layer (sine body or a bank of
/// detuned squares for metallic hat shimmer), shaped by the time-pure <see cref="CurveEnvelope"/>
/// and run through a resonant filter and soft drive. In <b>Clap</b> mode the amplitude envelope is
/// retriggered as 1–4 staggered taps (the classic multi-burst that makes a clap a clap); <b>Hat</b>
/// mode fires a single burst. Two decorrelated noise generators provide stereo width. Because every
/// stage is a pure function of time, the inspector preview (<see cref="IPreviewRenderer"/>) matches
/// playback exactly.
/// </summary>
public sealed class PercaInstrument : PolyphonicInstrument, IPresetProvider, IPreviewRenderer
{
    public const string TypeId = "perca";

    protected override string GetTypeId() => TypeId;

    // The note that plays the patch at its configured tuning (the inspector keyboard starts here).
    private const int ReferenceNote = 60;

    private Parameter[]? _parameters;

    public PercaInstrument() : base(polyphony: 8) => Reset();

    public override string Name => "Perca";

    // --- Mode ---
    public int Mode { get; set; } // 0 Clap (multi-tap), 1 Hat (single burst)

    // --- Noise ---
    public int FilterType { get; set; } // 0 HP, 1 BP, 2 LP
    public double Cutoff { get; set; }
    public double Resonance { get; set; }
    public double Color { get; set; }
    public double Drive { get; set; }

    // --- Envelope ---
    public double AttackMs { get; set; }
    public double DecayMs { get; set; }
    public double Curve { get; set; }

    // --- Clap taps ---
    public int Taps { get; set; }
    public double SpreadMs { get; set; }
    public double TapDecayMs { get; set; }

    // --- Tone layer ---
    public double ToneLevel { get; set; }
    public int ToneType { get; set; } // 0 Sine, 1 Metal
    public double ToneFreq { get; set; }
    public double ToneDecayMs { get; set; }

    // --- Output ---
    public double Gain { get; set; }
    public double Width { get; set; }

    private static readonly string[] ModeNames = { "Clap", "Hat" };
    private static readonly string[] FilterNames = { "HP", "BP", "LP" };
    private static readonly string[] ToneTypeNames = { "Sine", "Metal" };

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Mode", ModeNames, () => Mode, i => Mode = i) { Group = "Main" },

        new ChoiceParameter("Filter", FilterNames, () => FilterType, i => FilterType = i) { Group = "Noise" },
        new FloatParameter("Cutoff", 200, 16000, () => Cutoff, v => Cutoff = v, "0", "Hz", skew: 3.0) { Group = "Noise" },
        new FloatParameter("Reso", 0.5, 8, () => Resonance, v => Resonance = v, "0.00", "", skew: 2.0) { Group = "Noise" },
        new FloatParameter("Color", 0, 1, () => Color, v => Color = v, "0.00") { Group = "Noise" },
        new FloatParameter("Drive", 0, 1, () => Drive, v => Drive = v, "0.00") { Group = "Noise" },

        new FloatParameter("Attack", 0.1, 30, () => AttackMs, v => AttackMs = v, "0.0", "ms", skew: 2.0) { Group = "Envelope" },
        new FloatParameter("Decay", 20, 2000, () => DecayMs, v => DecayMs = v, "0", "ms", skew: 2.0) { Group = "Envelope" },
        new FloatParameter("Curve", 0, 1, () => Curve, v => Curve = v, "0.00") { Group = "Envelope" },

        new FloatParameter("Taps", 1, 4, () => Taps, v => Taps = (int)Math.Round(v), "0") { Group = "Clap" },
        new FloatParameter("Spread", 5, 40, () => SpreadMs, v => SpreadMs = v, "0.0", "ms") { Group = "Clap" },
        new FloatParameter("Tap Decay", 5, 120, () => TapDecayMs, v => TapDecayMs = v, "0", "ms", skew: 2.0) { Group = "Clap" },

        new FloatParameter("Level", 0, 1, () => ToneLevel, v => ToneLevel = v, "0.00") { Group = "Tone" },
        new ChoiceParameter("Type", ToneTypeNames, () => ToneType, i => ToneType = i) { Group = "Tone" },
        new FloatParameter("Freq", 100, 12000, () => ToneFreq, v => ToneFreq = v, "0", "Hz", skew: 3.0) { Group = "Tone" },
        new FloatParameter("Decay", 5, 2000, () => ToneDecayMs, v => ToneDecayMs = v, "0", "ms", skew: 2.0) { Group = "Tone" },

        new FloatParameter("Gain", 0, 1, () => Gain, v => Gain = v, "0.00") { Group = "Output" },
        new FloatParameter("Width", 0, 1, () => Width, v => Width = v, "0.00") { Group = "Output" }
    };

    protected override Voice CreateVoice() => new PercVoice(this);

    // ===== Preview (IPreviewRenderer) =====

    public double PreviewSeconds => 2.0;

    public void RenderPreview(Span<float> mono, int sampleRate)
    {
        mono.Clear();
        if (mono.Length == 0) return;
        var format = new AudioFormat(sampleRate <= 0 ? 44100 : sampleRate, 1);
        var voice = new PercVoice(this);
        voice.Start(ReferenceNote, 1.0f, format);

        const int block = 512;
        var pos = 0;
        while (pos < mono.Length && voice.IsActive)
        {
            var n = Math.Min(block, mono.Length - pos);
            voice.Render(mono.Slice(pos, n));
            pos += n;
        }
    }

    public override IInstrument Clone()
    {
        var c = new PercaInstrument();
        CopyStateTo(c);
        return c;
    }

    private void CopyStateTo(PercaInstrument c)
    {
        c.Mode = Mode;
        c.FilterType = FilterType; c.Cutoff = Cutoff; c.Resonance = Resonance; c.Color = Color; c.Drive = Drive;
        c.AttackMs = AttackMs; c.DecayMs = DecayMs; c.Curve = Curve;
        c.Taps = Taps; c.SpreadMs = SpreadMs; c.TapDecayMs = TapDecayMs;
        c.ToneLevel = ToneLevel; c.ToneType = ToneType; c.ToneFreq = ToneFreq; c.ToneDecayMs = ToneDecayMs;
        c.Gain = Gain; c.Width = Width;
    }

    // ===== Presets =====

    private static readonly string[] PresetNamesList = { "Init", "House Clap", "Closed Hat", "Open Hat", "Dark Snare", "Crash" };

    public IReadOnlyList<string> PresetNames => PresetNamesList;

    public void LoadPreset(int index)
    {
        switch (index)
        {
            case 1: HouseClap(); break;
            case 2: ClosedHat(); break;
            case 3: OpenHat(); break;
            case 4: DarkSnare(); break;
            case 5: Crash(); break;
            default: Reset(); break;
        }
    }

    /// <summary>Init = a neutral short noise hit. All presets start here.</summary>
    private void Reset()
    {
        Mode = 1;
        FilterType = 1; Cutoff = 5000; Resonance = 1.0; Color = 0.0; Drive = 0.0;
        AttackMs = 0.5; DecayMs = 140; Curve = 0.7;
        Taps = 3; SpreadMs = 12; TapDecayMs = 25;
        ToneLevel = 0.0; ToneType = 0; ToneFreq = 900; ToneDecayMs = 80;
        Gain = 0.8; Width = 0.2;
    }

    /// <summary>A smooth deep-house clap: three staggered band-passed bursts into a roomy body,
    /// driven and spread wide for a modern "stadium" clap.</summary>
    private void HouseClap()
    {
        Reset();
        Mode = 0;
        FilterType = 1; Cutoff = 1500; Resonance = 1.8; Color = 0.15; Drive = 0.42;
        AttackMs = 0.4; DecayMs = 380; Curve = 0.65;
        Taps = 3; SpreadMs = 11; TapDecayMs = 28;
        ToneLevel = 0.0;
        Gain = 0.85; Width = 0.6;
    }

    /// <summary>A tight closed hat: bright high-passed noise plus a metallic square bank, driven for
    /// a modern sizzle that bites through a dense mix.</summary>
    private void ClosedHat()
    {
        Reset();
        Mode = 1;
        FilterType = 0; Cutoff = 8400; Resonance = 1.1; Color = 0.0; Drive = 0.28;
        AttackMs = 0.2; DecayMs = 65; Curve = 0.85;
        // The tone layer bypasses the noise filter, so the metal bank sits a little higher and quieter
        // to keep the hat crisp rather than clangy.
        ToneLevel = 0.42; ToneType = 1; ToneFreq = 6800; ToneDecayMs = 45;
        Gain = 0.75; Width = 0.35;
    }

    /// <summary>An open hat: the closed hat with a long sizzling decay, spread wide for the driving
    /// off-beat "jet" of modern trance.</summary>
    private void OpenHat()
    {
        ClosedHat();
        DecayMs = 420; Curve = 0.45;
        ToneLevel = 0.42; ToneDecayMs = 320;
        Gain = 0.7; Width = 0.55;
    }

    /// <summary>A hard, dark snare: a band-passed two-tap noise crack over a full-weight low sine
    /// body (the tone bypasses the noise filter), driven for bite. Built for drum &amp; bass.</summary>
    private void DarkSnare()
    {
        Reset();
        Mode = 0; // two staggered taps give the crack/flam at the front
        FilterType = 1; Cutoff = 1600; Resonance = 0.9; Color = 0.3; Drive = 0.6;
        AttackMs = 0.2; DecayMs = 260; Curve = 0.65;
        Taps = 2; SpreadMs = 8; TapDecayMs = 32;
        ToneLevel = 0.8; ToneType = 0; ToneFreq = 175; ToneDecayMs = 130;
        Gain = 0.95; Width = 0.3;
    }

    /// <summary>A crash-cymbal wash: bright high-passed noise with a long slow-bowing decay and a
    /// faint metallic shimmer, spread wide — the section-boundary splash.</summary>
    private void Crash()
    {
        Reset();
        Mode = 1;
        FilterType = 0; Cutoff = 5200; Resonance = 0.9; Color = 0.05; Drive = 0.2;
        AttackMs = 0.3; DecayMs = 1750; Curve = 0.28;
        ToneLevel = 0.25; ToneType = 1; ToneFreq = 6800; ToneDecayMs = 1500;
        Gain = 0.6; Width = 0.6;
    }

    /// <summary>One percussion hit: noise (+ tone layer) → colour LP → resonant filter → drive → width.</summary>
    private sealed class PercVoice : Voice
    {
        private const float VoiceGain = 0.9f;
        private const int MaxTaps = 4;

        // TR-808-style inharmonic ratios for the metallic (square bank) tone layer.
        private static readonly double[] MetalRatios = { 1.0, 1.5, 2.08, 2.72, 3.4, 4.1 };

        private readonly PercaInstrument _inst;
        private readonly WaveOscillator _noiseA = new();
        private readonly WaveOscillator _noiseB = new();
        private readonly WaveOscillator _toneSine = new();
        private readonly WaveOscillator[] _metal = new WaveOscillator[MetalRatios.Length];
        private readonly OnePole _colorA = new();
        private readonly OnePole _colorB = new();
        private Biquad _filtA;
        private Biquad _filtB;
        private static uint _seed = 1;

        private readonly CurveEnvelope[] _tapEnvs = new CurveEnvelope[MaxTaps];
        private int _taps;
        private CurveEnvelope _toneEnv;
        private double _pitchRatio;
        private long _elapsed, _totalSamples;
        private float _velocity;

        public PercVoice(PercaInstrument inst)
        {
            _inst = inst;
            for (var i = 0; i < _metal.Length; i++) _metal[i] = new WaveOscillator { Wave = OscWave.Square };
        }

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            var sr = format.SampleRate;
            var seed = _seed++ * 2654435761u + (uint)midiNote;

            // Playing away from the reference note shifts the whole patch (filter + tone) in semitones.
            _pitchRatio = MusicalMath.SemitonesToRatio(midiNote - ReferenceNote);

            _noiseA.Wave = OscWave.Noise; _noiseA.SetSampleRate(sr); _noiseA.SeedNoise(seed);
            _noiseB.Wave = OscWave.Noise; _noiseB.SetSampleRate(sr); _noiseB.SeedNoise(seed * 747796405u + 1u);
            _toneSine.Wave = OscWave.Sine; _toneSine.SetSampleRate(sr); _toneSine.ResetPhase(0);

            var toneHz = _inst.ToneFreq * _pitchRatio;
            _toneSine.SetFrequency(AudioMath.Clamp(toneHz, 10, sr * 0.45));
            for (var i = 0; i < _metal.Length; i++)
            {
                _metal[i].SetSampleRate(sr);
                _metal[i].SetFrequency(AudioMath.Clamp(toneHz * MetalRatios[i], 10, sr * 0.45));
                _metal[i].ResetPhase(i / (double)_metal.Length); // stagger phases so the bank doesn't comb
            }

            // Colour: 0 = full-bandwidth white, 1 = dark (low-pass swept down to ~1.5 kHz).
            var colorHz = 18000.0 * Math.Pow(1500.0 / 18000.0, AudioMath.Clamp(_inst.Color, 0, 1));
            _colorA.SetLowpass(AudioMath.Clamp(colorHz, 100, sr * 0.45), sr); _colorA.Reset();
            _colorB.SetLowpass(AudioMath.Clamp(colorHz, 100, sr * 0.45), sr); _colorB.Reset();
            _filtA.Reset();
            _filtB.Reset();

            // Clap mode retriggers the envelope as staggered taps; the last tap carries the full body
            // decay while the earlier ones are short pre-bursts. Hat mode is a single burst.
            var attack = _inst.AttackMs / 1000.0;
            var spread = _inst.SpreadMs / 1000.0;
            _taps = _inst.Mode == 0 ? Math.Clamp(_inst.Taps, 1, MaxTaps) : 1;
            for (var i = 0; i < _taps; i++)
            {
                var isLast = i == _taps - 1;
                var decay = (isLast ? _inst.DecayMs : _inst.TapDecayMs) / 1000.0;
                _tapEnvs[i] = new CurveEnvelope(i * spread, attack, 0, decay, _inst.Curve);
            }

            _toneEnv = new CurveEnvelope((_taps - 1) * spread, attack, 0, _inst.ToneDecayMs / 1000.0, _inst.Curve);

            var total = _tapEnvs[_taps - 1].TotalSeconds;
            if (_inst.ToneLevel > 0) total = Math.Max(total, _toneEnv.TotalSeconds);
            _totalSamples = (long)((total + 0.02) * sr) + 1;
            _elapsed = 0;
        }

        // Percussion hits are one-shots: NoteOff is ignored; the voice ends on its own timeline.
        public override void Release() { }

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;

            var toneLvl = (float)_inst.ToneLevel;
            var metal = _inst.ToneType == 1;
            var drive = (float)_inst.Drive;
            var width = (float)_inst.Width;
            var amp = _velocity * (float)_inst.Gain * VoiceGain;
            var stereo = channels >= 2 && width > 0.001f;

            var mode = _inst.FilterType switch
            {
                0 => FilterMode.HighPass,
                2 => FilterMode.LowPass,
                _ => FilterMode.BandPass
            };
            var coeffs = BiquadCoefficients.Compute(mode,
                AudioMath.Clamp(_inst.Cutoff * _pitchRatio, 20, sr * 0.45),
                AudioMath.Clamp(_inst.Resonance, 0.05, 12), sr);

            for (var frame = 0; frame < frames; frame++)
            {
                var t = _elapsed / (double)sr;

                // Envelope = the loudest active tap (retrigger semantics, like a real clap).
                var env = 0.0;
                for (var i = 0; i < _taps; i++)
                {
                    var e = _tapEnvs[i].Evaluate(t);
                    if (e > env) env = e;
                }

                // Tone layer (sine body or metallic square bank), sharing the filter with the noise.
                var tone = 0.0;
                if (toneLvl > 0)
                {
                    if (metal)
                    {
                        double m = 0;
                        for (var i = 0; i < _metal.Length; i++) m += _metal[i].Next();
                        tone = m * (1.0 / _metal.Length);
                    }
                    else
                    {
                        tone = _toneSine.Next();
                    }

                    tone *= _toneEnv.Evaluate(t) * toneLvl;
                }

                // The tone layer joins AFTER the noise filter, so a band-passed snare crack can sit
                // over a full-weight low body (and the drive still glues the two together).
                var a = (float)(_filtA.Process(coeffs, _colorA.ProcessLP(_noiseA.Next()) * env) + tone);
                if (drive > 0) a = (float)Math.Tanh(a * (1f + drive * 4f));

                if (stereo)
                {
                    // A second decorrelated noise path gives true stereo; width blends mono → wide.
                    var b = (float)(_filtB.Process(coeffs, _colorB.ProcessLP(_noiseB.Next()) * env) + tone);
                    if (drive > 0) b = (float)Math.Tanh(b * (1f + drive * 4f));

                    var mid = 0.5f * (a + b);
                    var l = (mid + width * (a - mid)) * amp;
                    var r = (mid + width * (b - mid)) * amp;
                    var bi = frame * channels;
                    buffer[bi] += l;
                    buffer[bi + 1] += r;
                    for (var c = 2; c < channels; c++) buffer[bi + c] += 0.5f * (l + r);
                }
                else
                {
                    var s = a * amp;
                    var bi = frame * channels;
                    for (var c = 0; c < channels; c++) buffer[bi + c] += s;
                }

                if (++_elapsed >= _totalSamples) { IsActive = false; return; }
            }
        }
    }
}
