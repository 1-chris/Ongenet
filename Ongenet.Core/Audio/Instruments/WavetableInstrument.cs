using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A fully-featured wavetable synthesizer. Each voice scans a <see cref="Wavetable"/> (morphing across
/// single-cycle frames) with detuned unison, a wavefolder, a multimode resonant filter and an ADSR amp
/// envelope; a global LFO sweeps the scan position (the "wavetable position" automation that makes the
/// sound move). The table is either one of the built-in procedural presets or a loaded sample sliced into
/// frames (<see cref="ISampleHost"/>). Reuses <see cref="AdsrEnvelope"/>, <see cref="Biquad"/>,
/// <see cref="Lfo"/>, <see cref="OnePole"/> and <see cref="MusicalMath"/>.
/// </summary>
public sealed class WavetableInstrument : PolyphonicInstrument, ISampleHost, IProjectStatefulComponent
{
    public const string TypeId = "wavetable";
    protected override string GetTypeId() => TypeId;

    // --- Oscillator / scan ---
    public double Position { get; set; } = 0.0;       // 0..1 wavetable scan
    public int Warp { get; set; } = 0;                // 0 Fold, 1 PWM, 2 Bend (driven by Shape)
    public double Shape { get; set; } = 0.0;          // 0..1 warp amount
    public int UnisonVoices { get; set; } = 1;        // 1..MaxUnison
    public double DetuneCents { get; set; } = 15.0;   // unison spread (cents)
    public double Spread { get; set; } = 0.5;         // unison stereo width

    // --- Filter ---
    public int FilterType { get; set; } = 0;          // 0 LP, 1 HP, 2 BP
    public double Cutoff { get; set; } = 18000.0;
    public double Resonance { get; set; } = 0.7;

    // --- Amp envelope ---
    public double AttackSeconds { get; set; } = 0.005;
    public double DecaySeconds { get; set; } = 0.12;
    public double SustainLevel { get; set; } = 0.8;
    public double ReleaseSeconds { get; set; } = 0.25;

    // --- LFO → position ---
    public int LfoWaveIndex { get; set; } = 0;        // Sine/Triangle/Saw/Square
    public double LfoRate { get; set; } = 0.3;        // Hz
    public double LfoDepth { get; set; } = 0.0;       // 0..1 added to position

    // --- Output ---
    public double Level { get; set; } = 0.8;

    private const int MaxUnison = 7;

    private volatile Wavetable _table;
    private int _revision;
    private readonly Lfo _lfo = new();
    private AudioSampleBuffer? _loadedSample;
    private WavetablePreset _preset = WavetablePreset.Basic;
    private int _seed;

    public WavetableInstrument() : base(16)
    {
        _table = WavetableGenerator.BuildPreset(WavetablePreset.Basic);
    }

    public override string Name => "Wavetable";

    /// <summary>The current table (immutable; swapped by reference on rebuild). Read by voices + the UI.</summary>
    public Wavetable Table => _table;

    /// <summary>Bumped whenever the table is rebuilt, so the 3D view knows to re-extract its geometry.</summary>
    public int TableRevision => _revision;

    /// <summary>The effective scan position (incl. LFO) for the current block — what the 3D view highlights.</summary>
    public float DisplayPosition { get; private set; }

    /// <summary>The block's scan position read by voices (set in <see cref="Render"/> before voices render).</summary>
    public float BlockPosition { get; private set; }

    public FilterMode FilterModeValue => FilterType switch { 1 => FilterMode.HighPass, 2 => FilterMode.BandPass, _ => FilterMode.LowPass };

    // --- ISampleHost ---
    public string? SampleName { get; private set; }
    public AudioSampleBuffer? CurrentSample => _loadedSample;

    public void LoadSample(AudioSampleBuffer sample, string name)
    {
        _loadedSample = sample;
        SampleName = name;
        SetTable(WavetableGenerator.FromSample(sample));
    }

    /// <summary>Regenerates the table from a built-in procedural preset (the inspector's preset buttons).</summary>
    public void LoadPreset(WavetablePreset preset, int seed = 0)
    {
        _loadedSample = null;
        _preset = preset;
        _seed = preset == WavetablePreset.Random ? (seed == 0 ? Environment.TickCount : seed) : 0;
        SampleName = preset.ToString();
        SetTable(WavetableGenerator.BuildPreset(preset, seed: _seed));
    }

    private void SetTable(Wavetable table)
    {
        _table = table;
        System.Threading.Interlocked.Increment(ref _revision);
    }

    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Position", 0.0, 1.0, () => Position, v => Position = v, "0%", "", 1.0) { Group = "Oscillator" },
        new ChoiceParameter("Warp", new[] { "Fold", "PWM", "Bend" }, () => Warp, v => Warp = v) { Group = "Oscillator" },
        new FloatParameter("Shape", 0.0, 1.0, () => Shape, v => Shape = v, "0%", "", 1.0) { Group = "Oscillator" },
        new FloatParameter("Unison", 1, MaxUnison, () => UnisonVoices, v => UnisonVoices = (int)Math.Round(v), "0", "") { Group = "Oscillator" },
        new FloatParameter("Detune", 0.0, 50.0, () => DetuneCents, v => DetuneCents = v, "0", "ct") { Group = "Oscillator" },
        new FloatParameter("Spread", 0.0, 1.0, () => Spread, v => Spread = v, "0%", "", 1.0) { Group = "Oscillator" },

        new ChoiceParameter("Filter", new[] { "Low-pass", "High-pass", "Band-pass" }, () => FilterType, v => FilterType = v) { Group = "Filter" },
        new FloatParameter("Cutoff", 20.0, 18000.0, () => Cutoff, v => Cutoff = v, "0", "Hz", 3.0) { Group = "Filter" },
        new FloatParameter("Reso", 0.5, 16.0, () => Resonance, v => Resonance = v, "0.0", "", 2.0) { Group = "Filter" },

        new FloatParameter("Attack", 0.001, 3.0, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s", 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Decay", 0.001, 3.0, () => DecaySeconds, v => DecaySeconds = v, "0.000", "s", 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Sustain", 0.0, 1.0, () => SustainLevel, v => SustainLevel = v, "0%", "", 1.0) { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 5.0, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s", 2.0) { Group = "Amp Envelope" },

        new ChoiceParameter("LFO Wave", new[] { "Sine", "Triangle", "Saw", "Square" }, () => LfoWaveIndex, v => LfoWaveIndex = v) { Group = "LFO" },
        new FloatParameter("LFO Rate", 0.01, 20.0, () => LfoRate, v => LfoRate = v, "0.00", "Hz", 2.0) { Group = "LFO" },
        new FloatParameter("LFO Depth", 0.0, 1.0, () => LfoDepth, v => LfoDepth = v, "0%", "", 1.0) { Group = "LFO" },

        new FloatParameter("Level", 0.0, 1.0, () => Level, v => Level = v, "0%", "", 1.0) { Group = "Output" }
    };

    protected override Voice CreateVoice() => new WavetableVoice(this);

    public override void Render(Span<float> buffer)
    {
        var sr = Format.SampleRate < 1 ? 44100 : Format.SampleRate;
        var channels = Format.Channels < 1 ? 1 : Format.Channels;

        // Advance the global scan LFO across the block and compute the position voices read this block.
        _lfo.Wave = (LfoWave)Math.Clamp(LfoWaveIndex, 0, 3);
        _lfo.SetRate(LfoRate, sr);
        var lfoVal = _lfo.Value(0);
        BlockPosition = (float)Math.Clamp(Position + LfoDepth * lfoVal, 0.0, 1.0);
        DisplayPosition = BlockPosition;

        RenderVoices(buffer);

        var frames = buffer.Length / channels;
        for (var i = 0; i < frames; i++) _lfo.Advance();
    }

    public override IInstrument Clone()
    {
        var c = new WavetableInstrument
        {
            Position = Position, Warp = Warp, Shape = Shape, UnisonVoices = UnisonVoices, DetuneCents = DetuneCents, Spread = Spread,
            FilterType = FilterType, Cutoff = Cutoff, Resonance = Resonance,
            AttackSeconds = AttackSeconds, DecaySeconds = DecaySeconds, SustainLevel = SustainLevel, ReleaseSeconds = ReleaseSeconds,
            LfoWaveIndex = LfoWaveIndex, LfoRate = LfoRate, LfoDepth = LfoDepth, Level = Level
        };
        if (_loadedSample is not null) c.LoadSample(_loadedSample, SampleName ?? "");
        else c.LoadPreset(_preset, _seed);
        return c;
    }

    // The table SOURCE (preset/seed, or whether a sample is embedded) isn't a generic Parameter, so persist
    // it. The raw sample itself is embedded automatically via ISampleHost; here we only record how to rebuild
    // the table when there's no sample (a preset choice + its random seed).
    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteBool(_loadedSample is not null);
        writer.WriteInt((int)_preset);
        writer.WriteInt(_seed);
    }

    public void ReadProjectState(OngenReader reader)
    {
        var fromSample = reader.ReadBool();
        var preset = (WavetablePreset)reader.ReadInt();
        var seed = reader.ReadInt();
        // If a sample was embedded, LoadSample has already (re)built the table — leave it. Otherwise rebuild
        // the procedural preset (with its saved seed for Random) so it sounds identical on reload.
        if (!fromSample)
        {
            _preset = preset;
            _seed = seed;
            SetTable(WavetableGenerator.BuildPreset(preset, seed: seed));
        }
    }

    private static float Fold(float x)
    {
        // Triangle wavefolder: reflect values outside [-1,1] back in (adds harmonics as the drive rises).
        var guard = 0;
        while (x > 1f && guard++ < 8) x = 2f - x;
        guard = 0;
        while (x < -1f && guard++ < 8) x = -2f - x;
        return x;
    }

    // PWM-style phase warp: move the cycle's midpoint, squeezing/stretching the two halves (pulse width).
    private static float WarpPwm(float p, float amount)
    {
        var pw = 0.5f + amount * 0.49f;
        return p < pw ? p * 0.5f / pw : 0.5f + (p - pw) * 0.5f / (1f - pw);
    }

    // Phase-distortion bend: skew the phase so the waveform leans (a formant-ish shift).
    private static float WarpBend(float p, float amount)
    {
        var w = p + amount * 0.5f * MathF.Sin(2f * MathF.PI * p);
        return w - MathF.Floor(w);
    }

    /// <summary>One wavetable voice: detuned unison morph oscillators → wavefold → filter → ADSR.</summary>
    private sealed class WavetableVoice : Voice
    {
        private const float VoiceGain = 0.5f;

        private readonly WavetableInstrument _inst;
        private readonly AdsrEnvelope _env = new();
        private readonly OnePole _posSmooth = new();
        private readonly Random _rng = new();
        private readonly float[] _phase = new float[MaxUnison];
        private Biquad _filterL, _filterR;
        private float _velocity;
        private double _baseFreq;
        private int _sampleRate = 44100;

        public WavetableVoice(WavetableInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            _sampleRate = format.SampleRate < 1 ? 44100 : format.SampleRate;
            _baseFreq = MusicalMath.NoteToFrequency(midiNote);

            for (var i = 0; i < MaxUnison; i++) _phase[i] = (float)_rng.NextDouble(); // spread for width
            _filterL.Reset();
            _filterR.Reset();
            _posSmooth.SetSmoothTime(5.0, _sampleRate);
            _posSmooth.Reset(_inst.BlockPosition);

            _env.SetSampleRate(_sampleRate);
            _env.AttackSeconds = _inst.AttackSeconds;
            _env.DecaySeconds = _inst.DecaySeconds;
            _env.SustainLevel = _inst.SustainLevel;
            _env.ReleaseSeconds = _inst.ReleaseSeconds;
            _env.Gate();
        }

        public override void Release() => _env.Release();

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var table = _inst.Table; // snapshot (immutable)

            var unison = Math.Clamp(_inst.UnisonVoices, 1, MaxUnison);
            var detune = (float)_inst.DetuneCents;
            var spread = (float)Math.Clamp(_inst.Spread, 0.0, 1.0);
            var warp = _inst.Warp;
            var shape = (float)Math.Clamp(_inst.Shape, 0.0, 1.0);
            var shapeDrive = 1f + shape * 5f;
            var level = (float)Math.Clamp(_inst.Level, 0.0, 1.0) * VoiceGain * _velocity;
            var blockPos = _inst.BlockPosition;
            var uGain = 1f / MathF.Sqrt(unison);

            Span<float> inc = stackalloc float[MaxUnison];
            Span<float> panL = stackalloc float[MaxUnison];
            Span<float> panR = stackalloc float[MaxUnison];
            for (var i = 0; i < unison; i++)
            {
                var t = unison == 1 ? 0f : (float)i / (unison - 1) * 2f - 1f; // -1..+1
                var ratio = (float)MusicalMath.CentsToRatio(t * detune);
                inc[i] = (float)(_baseFreq * ratio / _sampleRate);
                var pan = t * spread;
                var angle = (pan + 1f) * 0.25f * MathF.PI;
                panL[i] = MathF.Cos(angle);
                panR[i] = MathF.Sin(angle);
            }

            var coeffs = BiquadCoefficients.Compute(_inst.FilterModeValue,
                Math.Clamp(_inst.Cutoff, 20.0, _sampleRate * 0.45), Math.Max(0.5, _inst.Resonance), _sampleRate);

            for (var frame = 0; frame < frames; frame++)
            {
                var pos = (float)_posSmooth.ProcessLP(blockPos);

                float l = 0f, r = 0f;
                for (var i = 0; i < unison; i++)
                {
                    var ph = _phase[i];
                    if (warp == 1) ph = WarpPwm(ph, shape);
                    else if (warp == 2) ph = WarpBend(ph, shape);

                    var s = table.Read(pos, ph, inc[i]); // band-limited read (alias-free)
                    if (warp == 0) s = Fold(s * shapeDrive);

                    l += s * panL[i];
                    r += s * panR[i];
                    _phase[i] += inc[i];
                    if (_phase[i] >= 1f) _phase[i] -= 1f;
                }

                l = (float)_filterL.Process(coeffs, l * uGain);
                r = (float)_filterR.Process(coeffs, r * uGain);

                var amp = _env.Process() * level;
                l *= amp;
                r *= amp;

                var bi = frame * channels;
                if (channels >= 2)
                {
                    buffer[bi] += l;
                    buffer[bi + 1] += r;
                    for (var c = 2; c < channels; c++) buffer[bi + c] += (l + r) * 0.5f;
                }
                else
                {
                    buffer[bi] += (l + r) * 0.5f;
                }

                if (!_env.IsActive)
                {
                    IsActive = false;
                    return;
                }
            }
        }
    }
}
