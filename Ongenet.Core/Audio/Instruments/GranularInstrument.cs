using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A granular synthesizer. It plays a user-loaded source sample by continuously spawning short
/// overlapping "grains" extracted from a moving playhead. Each grain is windowed (to avoid clicks),
/// pitched, panned and optionally reversed; many grains layered together form evolving textures.
///
/// Built on the shared <see cref="PolyphonicInstrument"/>/<see cref="Voice"/> framework and the DSP
/// toolkit (<see cref="GrainWindow"/>, <see cref="FastRandom"/>, <see cref="AudioMath"/>,
/// <see cref="MusicalMath"/>, <see cref="Lfo"/>, <see cref="AdsrEnvelope"/>, <see cref="Biquad"/>).
/// Implements <see cref="ISampleHost"/> so the existing inspector "load sample" UI works unchanged.
/// </summary>
public sealed class GranularInstrument : PolyphonicInstrument, ISampleHost
{
    public const string TypeId = "granular";

    /// <summary>MIDI note that plays the source at its written pitch (transposed only by <see cref="PitchSemitones"/>).</summary>
    private const int RootNote = 60; // C4

    private const int MaxStreams = 4;
    private const int GrainsPerVoice = 192; // pool size; spawns are dropped if all are busy

    /// <summary>
    /// Hard ceiling on simultaneously-sounding grains across ALL voices. Bounds CPU so a held chord at
    /// max density degrades gracefully (thinner clouds) instead of underrunning the audio device.
    /// Shared/updated only on the audio thread (voices render sequentially), so a plain int is safe.
    /// </summary>
    private const int MaxActiveGrains = 640;
    private int _activeGrains;

    private volatile AudioSampleBuffer? _sample;
    private volatile float[]? _mono; // precomputed mono mixdown of the source (grains read from this)
    private Parameter[]? _parameters;

    public GranularInstrument() : base(polyphony: 8) { }

    public override string Name => "Granular";

    /// <summary>Cosmetic feed of spawned grains for the UI grain monitor (not used by audio).</summary>
    public GrainMonitor Monitor { get; } = new();

    // --- live parameters (wrapped by Parameters below) ---

    // Core grain controls
    public double GrainSizeMs { get; set; } = 70.0;
    public double DensityHz { get; set; } = 24.0;
    public double Position { get; set; } = 0.0;            // 0..1 scan point into the sample
    public GrainWindowShape Window { get; set; } = GrainWindowShape.Hann;

    // Movement & randomisation
    public double Spray { get; set; } = 0.08;              // 0..1 random position offset (fraction of sample)
    public double PitchRandomSemitones { get; set; } = 0.0;
    public double ScanSpeed { get; set; } = 0.0;           // sample-lengths per second the playhead drifts (signed)
    public int Direction { get; set; } = 0;               // 0 Forward, 1 Reverse, 2 Alternate, 3 Random

    // Pitch & spatialisation
    public double PitchSemitones { get; set; } = 0.0;     // -24..24 transpose
    public double PanSpread { get; set; } = 0.3;          // 0..1 random pan per grain

    // Multi-stream & filter
    public int Streams { get; set; } = 1;                 // 1..MaxStreams
    public double StreamSpread { get; set; } = 0.2;       // per-stream detune + pan offset
    public int FilterTypeIndex { get; set; } = 0;         // 0 Off, 1 LP, 2 HP, 3 BP
    public double Cutoff { get; set; } = 20000.0;
    public double Resonance { get; set; } = 0.7;

    // Amp envelope
    public double AttackSeconds { get; set; } = 0.02;
    public double DecaySeconds { get; set; } = 0.1;
    public double SustainLevel { get; set; } = 0.85;
    public double ReleaseSeconds { get; set; } = 0.4;

    // Modulation (LFO → destination)
    public double LfoRateHz { get; set; } = 0.5;
    public LfoWave LfoShape { get; set; } = LfoWave.Sine;
    public double LfoDepth { get; set; } = 0.0;
    public int LfoDest { get; set; } = 0;                 // 0 Off, 1 Position, 2 Pitch, 3 Cutoff, 4 Pan

    public double Gain { get; set; } = 0.8;

    // --- ISampleHost ---

    public string? SampleName { get; private set; }
    public AudioSampleBuffer? Sample => _sample;

    /// <summary>Mono mixdown of the loaded source (one value per frame); grains read from this.</summary>
    internal float[]? Mono => _mono;

    public void LoadSample(AudioSampleBuffer sample, string name)
    {
        // Precompute a mono mixdown once (with a +1 guard) so each grain read is a single linear
        // interpolation rather than averaging channels with bounds-checked reads per grain per sample.
        _mono = SampleMixdown.ToMono(sample);
        _sample = sample;
        SampleName = name;
    }

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        // Core
        new FloatParameter("Grain", 2.0, 400.0, () => GrainSizeMs, v => GrainSizeMs = v, "0", "ms", skew: 2.0) { Group = "Grain" },
        new FloatParameter("Density", 0.5, 100.0, () => DensityHz, v => DensityHz = v, "0.0", "/s", skew: 2.0) { Group = "Grain" },
        new FloatParameter("Position", 0.0, 1.0, () => Position, v => Position = v, "0.00") { Group = "Grain" },
        new ChoiceParameter("Window", new[] { "Hann", "Triangle", "Tukey", "Gaussian", "Expo", "Rect" },
            () => (int)Window, i => Window = (GrainWindowShape)i) { Group = "Grain" },

        // Movement
        new FloatParameter("Spray", 0.0, 1.0, () => Spray, v => Spray = v, "0.00") { Group = "Movement" },
        new FloatParameter("Pitch Rand", 0.0, 12.0, () => PitchRandomSemitones, v => PitchRandomSemitones = v, "0.0", "st") { Group = "Movement" },
        new FloatParameter("Scan", -2.0, 2.0, () => ScanSpeed, v => ScanSpeed = v, "0.00", "x") { Group = "Movement" },
        new ChoiceParameter("Direction", new[] { "Forward", "Reverse", "Alternate", "Random" },
            () => Direction, i => Direction = i) { Group = "Movement" },

        // Pitch & space
        new FloatParameter("Pitch", -24.0, 24.0, () => PitchSemitones, v => PitchSemitones = v, "0.0", "st") { Group = "Pitch & Space" },
        new FloatParameter("Pan Spread", 0.0, 1.0, () => PanSpread, v => PanSpread = v, "0.00") { Group = "Pitch & Space" },

        // Layers
        new ChoiceParameter("Streams", new[] { "1", "2", "3", "4" },
            () => Math.Clamp(Streams - 1, 0, MaxStreams - 1), i => Streams = i + 1) { Group = "Layers" },
        new FloatParameter("Layer Spread", 0.0, 1.0, () => StreamSpread, v => StreamSpread = v, "0.00") { Group = "Layers" },

        // Filter
        new ChoiceParameter("Filter", new[] { "Off", "Low Pass", "High Pass", "Band Pass" },
            () => FilterTypeIndex, i => FilterTypeIndex = i) { Group = "Filter" },
        new FloatParameter("Cutoff", 20.0, 20000.0, () => Cutoff, v => Cutoff = v, "0", "Hz", skew: 3.0) { Group = "Filter" },
        new FloatParameter("Reso", 0.5, 12.0, () => Resonance, v => Resonance = v, "0.0") { Group = "Filter" },

        // Amp envelope
        new FloatParameter("Attack", 0.001, 4.0, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Decay", 0.001, 4.0, () => DecaySeconds, v => DecaySeconds = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Sustain", 0.0, 1.0, () => SustainLevel, v => SustainLevel = v, "0.00") { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 6.0, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },

        // Modulation
        new FloatParameter("LFO Rate", 0.01, 20.0, () => LfoRateHz, v => LfoRateHz = v, "0.00", "Hz", skew: 2.0) { Group = "Modulation" },
        new ChoiceParameter("LFO Wave", new[] { "Sine", "Triangle", "Saw", "Square" },
            () => (int)LfoShape, i => LfoShape = (LfoWave)i) { Group = "Modulation" },
        new FloatParameter("LFO Depth", 0.0, 1.0, () => LfoDepth, v => LfoDepth = v, "0.00") { Group = "Modulation" },
        new ChoiceParameter("LFO Dest", new[] { "Off", "Position", "Pitch", "Cutoff", "Pan" },
            () => LfoDest, i => LfoDest = i) { Group = "Modulation" },

        new FloatParameter("Gain", 0.0, 1.0, () => Gain, v => Gain = v, "0.00") { Group = "Output" }
    };

    protected override Voice CreateVoice() => new GrainVoice(this);

    public override IInstrument Clone()
    {
        var copy = new GranularInstrument
        {
            GrainSizeMs = GrainSizeMs, DensityHz = DensityHz, Position = Position, Window = Window,
            Spray = Spray, PitchRandomSemitones = PitchRandomSemitones, ScanSpeed = ScanSpeed, Direction = Direction,
            PitchSemitones = PitchSemitones, PanSpread = PanSpread,
            Streams = Streams, StreamSpread = StreamSpread, FilterTypeIndex = FilterTypeIndex,
            Cutoff = Cutoff, Resonance = Resonance,
            AttackSeconds = AttackSeconds, DecaySeconds = DecaySeconds, SustainLevel = SustainLevel, ReleaseSeconds = ReleaseSeconds,
            LfoRateHz = LfoRateHz, LfoShape = LfoShape, LfoDepth = LfoDepth, LfoDest = LfoDest,
            Gain = Gain
        };
        if (_sample is { } s) copy.LoadSample(s, SampleName ?? "sample");
        return copy;
    }

    // --- LFO destinations ---
    private enum Dest { Off, Position, Pitch, Cutoff, Pan }

    // One pooled grain. A value type held in an array and mutated by ref (no per-grain allocation).
    private struct Grain
    {
        public bool Active;
        public double Pos;          // read position in source frames
        public double Rate;         // source frames advanced per output frame (sign = direction)
        public int Age;             // output samples elapsed
        public int Duration;        // grain length in output samples
        public float PanL, PanR;
        public GrainWindowShape Window;
    }

    private sealed class GrainVoice : Voice
    {
        private const float VoiceGain = 0.5f;

        private readonly GranularInstrument _inst;
        private readonly AdsrEnvelope _env = new();
        private readonly Lfo _lfo = new();
        private readonly Grain[] _grains = new Grain[GrainsPerVoice];
        private readonly double[] _streamPhase = new double[MaxStreams];
        private FastRandom _rng;
        private Biquad _filterL;
        private Biquad _filterR;
        private AudioSampleBuffer? _sample;
        private float[]? _mono;     // cached mono mixdown of the source
        private float _velocity;
        private double _playhead;   // current scan position in source frames
        private bool _alt;          // alternating-direction toggle
        private int _activeCount;   // live grains in this voice (mirrors _inst._activeGrains share)
        private static uint _seedCounter = 1;

        public GrainVoice(GranularInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            _sample = _inst.Sample;
            _mono = _inst.Mono;

            // Return this voice's outstanding grains to the global budget before reusing it (voice steal).
            _inst._activeGrains -= _activeCount;
            if (_inst._activeGrains < 0) _inst._activeGrains = 0;
            _activeCount = 0;

            Array.Clear(_grains, 0, _grains.Length);
            Array.Clear(_streamPhase, 0, _streamPhase.Length);
            _alt = false;
            _rng = new FastRandom(_seedCounter++ * 2654435761u + (uint)midiNote);

            var frameCount = _sample?.FrameCount ?? 0;
            _playhead = AudioMath.Clamp(_inst.Position, 0.0, 1.0) * Math.Max(0, frameCount - 1);

            _env.SetSampleRate(format.SampleRate);
            _env.AttackSeconds = _inst.AttackSeconds;
            _env.DecaySeconds = _inst.DecaySeconds;
            _env.SustainLevel = _inst.SustainLevel;
            _env.ReleaseSeconds = _inst.ReleaseSeconds;
            _env.Gate();

            _lfo.Wave = _inst.LfoShape;
            _lfo.SetRate(_inst.LfoRateHz, format.SampleRate);
            _lfo.Reset(0);
            _filterL.Reset();
            _filterR.Reset();

            if (_sample is null || frameCount < 2) IsActive = false;
        }

        public override void Release() => _env.Release();

        public override void Render(Span<float> buffer)
        {
            var sample = _sample;
            var mono = _mono;
            if (sample is null || mono is null) { IsActive = false; return; }

            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;
            var frameCount = sample.FrameCount;
            if (frameCount < 2) { IsActive = false; return; }

            // Snapshot live parameters once per block.
            var dest = (Dest)_inst.LfoDest;
            _lfo.Wave = _inst.LfoShape;
            _lfo.SetRate(_inst.LfoRateHz, sr);
            var lfoDepth = _inst.LfoDepth;

            var density = Math.Max(0.0, _inst.DensityHz) / sr;                 // grains per output sample (per stream)
            var grainSamples = Math.Max(1, (int)(_inst.GrainSizeMs * 0.001 * sr));
            var scanPerSample = _inst.ScanSpeed * frameCount / sr;             // playhead drift
            var streams = Math.Clamp(_inst.Streams, 1, MaxStreams);
            var noteRatio = MusicalMath.SemitonesToRatio(Note - RootNote + _inst.PitchSemitones);
            var srRatio = (double)sample.SampleRate / sr;
            var window = _inst.Window;
            var gain = (float)(_inst.Gain * VoiceGain);

            // Filter (computed per block; modulated by the LFO if routed to Cutoff).
            var filterOn = _inst.FilterTypeIndex > 0;
            var mode = _inst.FilterTypeIndex switch
            {
                1 => FilterMode.LowPass,
                2 => FilterMode.HighPass,
                3 => FilterMode.BandPass,
                _ => FilterMode.Bypass
            };
            var blockLfo = _lfo.Value(0); // sample (no advance) for the per-block cutoff
            var cutoff = _inst.Cutoff;
            if (dest == Dest.Cutoff)
                cutoff *= MusicalMath.SemitonesToRatio(blockLfo * lfoDepth * 48.0); // ±4 octaves at full depth
            cutoff = AudioMath.Clamp(cutoff, 20.0, sr * 0.45);
            var coeffs = filterOn
                ? BiquadCoefficients.Compute(mode, cutoff, _inst.Resonance, sr)
                : BiquadCoefficients.Identity;

            for (var frame = 0; frame < frames; frame++)
            {
                var env = _env.Process();
                var lfo = _lfo.Next() * lfoDepth; // −1..1 scaled by depth

                // Advance + wrap the scan playhead.
                _playhead += scanPerSample;
                _playhead = Wrap(_playhead, frameCount);

                // Schedule grains for each stream while the envelope is still open.
                if (_env.IsActive)
                {
                    for (var s = 0; s < streams; s++)
                    {
                        _streamPhase[s] += density;
                        while (_streamPhase[s] >= 1.0)
                        {
                            _streamPhase[s] -= 1.0;
                            SpawnGrain(s, streams, frameCount, grainSamples, srRatio, noteRatio, lfo, dest);
                        }
                    }
                }

                // Render active grains into this frame (stop once we've handled all live ones).
                float outL = 0f, outR = 0f;
                var remaining = _activeCount;
                for (var i = 0; i < _grains.Length && remaining > 0; i++)
                {
                    ref var g = ref _grains[i];
                    if (!g.Active) continue;
                    remaining--;

                    var w = GrainWindow.Lookup(g.Window, (double)g.Age / g.Duration);
                    var v = ReadMono(mono, g.Pos, frameCount) * w;
                    outL += v * g.PanL;
                    outR += v * g.PanR;

                    g.Pos += g.Rate;
                    if (++g.Age >= g.Duration)
                    {
                        g.Active = false;
                        _activeCount--;
                        _inst._activeGrains--;
                    }
                }

                if (filterOn)
                {
                    outL = (float)_filterL.Process(coeffs, outL);
                    outR = (float)_filterR.Process(coeffs, outR);
                }

                var amp = env * _velocity * gain;
                outL *= amp;
                outR *= amp;

                var baseIndex = frame * channels;
                if (channels == 1)
                {
                    buffer[baseIndex] += (outL + outR) * 0.5f;
                }
                else
                {
                    buffer[baseIndex] += outL;
                    buffer[baseIndex + 1] += outR;
                    for (var c = 2; c < channels; c++) buffer[baseIndex + c] += (outL + outR) * 0.5f;
                }

                // Done when the envelope has closed and every grain has finished its tail.
                if (!_env.IsActive && _activeCount == 0)
                {
                    IsActive = false;
                    return;
                }
            }
        }

        private void SpawnGrain(int stream, int streamCount, long frameCount, int grainSamples,
            double srRatio, double noteRatio, double lfo, Dest dest)
        {
            // Global CPU budget: if the synth is already saturated with grains, drop this spawn so the
            // cloud thins out instead of the audio device underrunning.
            if (_inst._activeGrains >= MaxActiveGrains) return;

            var slot = FindFreeGrain();
            if (slot < 0) return; // this voice's pool is full — drop

            // Per-stream detune + pan offset spread the layers for a lusher cloud.
            var streamCenter = streamCount > 1 ? stream / (double)(streamCount - 1) * 2.0 - 1.0 : 0.0;
            var streamDetune = _inst.StreamSpread * streamCenter; // semitones
            var streamPan = _inst.StreamSpread * streamCenter;

            // Pitch: note + transpose + per-stream detune + random scatter + LFO (if routed to pitch).
            var randSemis = _inst.PitchRandomSemitones * _rng.NextBipolar();
            var lfoSemis = dest == Dest.Pitch ? lfo * 12.0 : 0.0;
            var ratio = noteRatio * MusicalMath.SemitonesToRatio(streamDetune + randSemis + lfoSemis) * srRatio;

            // Start position: scan playhead + LFO position drift + random spray.
            var posLfo = dest == Dest.Position ? lfo * frameCount * 0.5 : 0.0;
            var spray = _inst.Spray * _rng.NextBipolar() * frameCount * 0.5;
            var start = Wrap(_playhead + posLfo + spray, frameCount);

            // Direction.
            var reverse = _inst.Direction switch
            {
                1 => true,
                2 => (_alt = !_alt),
                3 => _rng.Chance(0.5f),
                _ => false
            };
            var mag = Math.Abs(ratio);
            double pos, rate;
            if (reverse)
            {
                // Read backward: start ahead by the grain's span so it stays in range.
                pos = Wrap(start + grainSamples * mag, frameCount);
                rate = -mag;
            }
            else
            {
                pos = start;
                rate = mag;
            }

            // Pan: per-stream centre + LFO (if routed) + random spread.
            var panLfo = dest == Dest.Pan ? lfo : 0.0;
            var pan = AudioMath.Clamp(streamPan + panLfo + _inst.PanSpread * _rng.NextBipolar(), -1.0, 1.0);
            AudioMath.PanGains(pan, out var panL, out var panR);

            ref var g = ref _grains[slot];
            g.Active = true;
            g.Pos = pos;
            g.Rate = rate;
            g.Age = 0;
            g.Duration = grainSamples;
            g.PanL = panL;
            g.PanR = panR;
            g.Window = _inst.Window;
            _activeCount++;
            _inst._activeGrains++;

            // Report to the UI grain monitor (cosmetic).
            _inst.Monitor.Report((float)(start / frameCount), (float)pan,
                (float)(grainSamples / (double)Format.SampleRate), reverse);
        }

        private int FindFreeGrain()
        {
            for (var i = 0; i < _grains.Length; i++)
                if (!_grains[i].Active) return i;
            return -1;
        }

        // Mono read of the precomputed mixdown at a fractional frame position (linear interp).
        // The mono buffer has a +1 guard sample, so reading frame+1 at the end is safe.
        private static float ReadMono(float[] mono, double pos, long frameCount)
        {
            if (pos < 0 || pos >= frameCount) return 0f;
            var f0 = (int)pos;
            var frac = (float)(pos - f0);
            return mono[f0] + (mono[f0 + 1] - mono[f0]) * frac;
        }

        // Wraps a frame position into [0, frameCount).
        private static double Wrap(double pos, long frameCount)
        {
            if (frameCount <= 0) return 0;
            var len = (double)frameCount;
            pos %= len;
            if (pos < 0) pos += len;
            return pos;
        }
    }
}
