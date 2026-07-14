using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>Data-driven expansion catalog for additional built-in Field node presets.</summary>
public static class FieldExpansionNodes
{
    public static IEnumerable<FieldNodeInfo> CatalogEntries()
    {
        foreach (var spec in GainNodes)
            yield return Entry(spec, () => new FixedGainNode(spec.Id, spec.Name, spec.Gain));

        foreach (var spec in FilterNodes)
            yield return Entry(spec, () => new FixedFilterNode(spec.Id, spec.Name, spec.Mode, spec.Cutoff));

        foreach (var spec in ShaperNodes)
            yield return Entry(spec, () => new FixedShaperNode(spec.Id, spec.Name, spec.Drive));

        foreach (var spec in CrushNodes)
            yield return Entry(spec, () => new FixedBitcrusherNode(spec.Id, spec.Name, (int)spec.Drive, (int)spec.Cutoff));

        foreach (var spec in MsegNodes)
            yield return Entry(spec, () => new MsegVariantNode(spec.Id, spec.Name, spec.Segments));

        foreach (var spec in LfoNodes)
            yield return Entry(spec, () => new FixedLfoNode(spec.Id, spec.Name, spec.Rate));

        foreach (var spec in MathNodes)
            yield return Entry(spec, () => new BinaryMathNode(spec.Id, spec.Name, spec.Op));

        foreach (var spec in DelayNodes)
            yield return Entry(spec, () => new FixedDelayNode(spec.Id, spec.Name, spec.Ms));

        foreach (var spec in NoteNodes)
            yield return Entry(spec, () => new NoteUtilityNode(spec.Id, spec.Name, spec.Scale));

        foreach (var spec in SpectralNodes)
            yield return Entry(spec, () => new SpectralUtilityNode(spec.Id, spec.Name, spec.Band));

        foreach (var spec in ExtraNodes)
            yield return Entry(spec, () => new FixedGainNode(spec.Id, spec.Name, spec.Gain, spec.Category));
    }

    private static FieldNodeInfo Entry(NodeSpec spec, Func<FieldNode> create) =>
        new(spec.Id, spec.Name, spec.Category, create);

    private sealed record NodeSpec(string Id, string Name, string Category, double Gain = 1,
        FilterMode Mode = FilterMode.LowPass, double Cutoff = 1000, double Drive = 1,
        int Segments = 4, double Rate = 1, string Op = "", double Ms = 10, float Scale = 1, int Band = 0);

    private static readonly NodeSpec[] GainNodes =
    {
        new("field.gain.025", "Gain 0.25", FieldNodeCategories.Math, 0.25),
        new("field.gain.050", "Gain 0.50", FieldNodeCategories.Math, 0.5),
        new("field.gain.075", "Gain 0.75", FieldNodeCategories.Math, 0.75),
        new("field.gain.150", "Gain 1.50", FieldNodeCategories.Math, 1.5),
        new("field.gain.200", "Gain 2.00", FieldNodeCategories.Math, 2.0),
        new("field.gain.neg", "Gain Invert", FieldNodeCategories.Math, -1.0),
        new("field.trim.6db", "Trim -6 dB", FieldNodeCategories.Math, 0.501),
        new("field.trim.12db", "Trim -12 dB", FieldNodeCategories.Math, 0.251),
        new("field.boost.6db", "Boost +6 dB", FieldNodeCategories.Math, 1.995),
        new("field.boost.12db", "Boost +12 dB", FieldNodeCategories.Math, 3.981),
    };

    private static readonly NodeSpec[] FilterNodes =
    {
        new("field.filter.svf_lp", "SVF Low-Pass", FieldNodeCategories.Filters, Mode: FilterMode.LowPass, Cutoff: 1200),
        new("field.filter.svf_bp", "SVF Band-Pass", FieldNodeCategories.Filters, Mode: FilterMode.BandPass, Cutoff: 800),
        new("field.filter.svf_hp", "SVF High-Pass", FieldNodeCategories.Filters, Mode: FilterMode.HighPass, Cutoff: 600),
        new("field.filter.svf_notch", "SVF Notch", FieldNodeCategories.Filters, Mode: FilterMode.Notch, Cutoff: 1000),
        new("field.filter.sem_lp", "SEM Low-Pass", FieldNodeCategories.Filters, Mode: FilterMode.LowPass, Cutoff: 1800),
        new("field.filter.sem_bp", "SEM Band-Pass", FieldNodeCategories.Filters, Mode: FilterMode.BandPass, Cutoff: 1400),
        new("field.filter.ladder_lp", "Ladder LP", FieldNodeCategories.Filters, Mode: FilterMode.LowPass, Cutoff: 900),
        new("field.filter.ladder_bp", "Ladder BP", FieldNodeCategories.Filters, Mode: FilterMode.BandPass, Cutoff: 700),
        new("field.filter.gentle_lp", "Gentle LP", FieldNodeCategories.Filters, Mode: FilterMode.LowPass, Cutoff: 4000),
        new("field.filter.gentle_hp", "Gentle HP", FieldNodeCategories.Filters, Mode: FilterMode.HighPass, Cutoff: 200),
        new("field.filter.narrow_bp", "Narrow BP", FieldNodeCategories.Filters, Mode: FilterMode.BandPass, Cutoff: 2200),
        new("field.filter.wide_bp", "Wide BP", FieldNodeCategories.Filters, Mode: FilterMode.BandPass, Cutoff: 500),
    };

    private static readonly NodeSpec[] ShaperNodes =
    {
        new("field.shaper.transfer.soft", "Transfer Soft", FieldNodeCategories.Shapers, Drive: 1.5),
        new("field.shaper.transfer.hard", "Transfer Hard", FieldNodeCategories.Shapers, Drive: 3.0),
        new("field.shaper.transfer.fold", "Transfer Fold", FieldNodeCategories.Shapers, Drive: 2.5),
        new("field.shaper.transfer.sine", "Transfer Sine", FieldNodeCategories.Shapers, Drive: 2.0),
        new("field.shaper.saturate.light", "Light Saturate", FieldNodeCategories.Shapers, Drive: 1.2),
        new("field.shaper.saturate.heavy", "Heavy Saturate", FieldNodeCategories.Shapers, Drive: 4.0),
        new("field.shaper.crush.mild", "Mild Crush", FieldNodeCategories.Shapers, Drive: 2.0),
        new("field.shaper.crush.heavy", "Heavy Crush", FieldNodeCategories.Shapers, Drive: 5.0),
        new("field.shaper.ring.light", "Light Ring", FieldNodeCategories.Shapers, Drive: 1.0),
    };

    private static readonly NodeSpec[] CrushNodes =
    {
        new("field.shaper.crush.lofi", "Lo-Fi Crush", FieldNodeCategories.Shapers, Drive: 8, Cutoff: 4),
    };

    private static readonly NodeSpec[] MsegNodes =
    {
        new("field.mseg.1", "MSEG-1", FieldNodeCategories.Envelopes, Segments: 4),
        new("field.mseg.2", "MSEG-2", FieldNodeCategories.Envelopes, Segments: 6),
        new("field.mseg.3", "MSEG-3", FieldNodeCategories.Envelopes, Segments: 8),
        new("field.mseg.4", "MSEG-4", FieldNodeCategories.Envelopes, Segments: 12),
        new("field.mseg.5", "MSEG-5", FieldNodeCategories.Envelopes, Segments: 16),
        new("field.mseg.loop", "MSEG Loop", FieldNodeCategories.Envelopes, Segments: 8),
        new("field.mseg.oneshot", "MSEG One-Shot", FieldNodeCategories.Envelopes, Segments: 6),
    };

    private static readonly NodeSpec[] LfoNodes =
    {
        new("field.lfo.ultra", "Ultra LFO", FieldNodeCategories.Modulators, Rate: 0.02),
        new("field.lfo.sub", "Sub LFO", FieldNodeCategories.Modulators, Rate: 0.08),
        new("field.lfo.mid", "Mid LFO", FieldNodeCategories.Modulators, Rate: 1.0),
        new("field.lfo.fast", "Fast LFO", FieldNodeCategories.Modulators, Rate: 4.0),
        new("field.lfo.audioish", "Audio-ish LFO", FieldNodeCategories.Modulators, Rate: 12.0),
        new("field.lfo.tri", "Tri LFO", FieldNodeCategories.Modulators, Rate: 0.5),
        new("field.lfo.sqr", "Square LFO", FieldNodeCategories.Modulators, Rate: 0.5),
        new("field.lfo.saw", "Saw LFO", FieldNodeCategories.Modulators, Rate: 0.5),
    };

    private static readonly NodeSpec[] MathNodes =
    {
        new("field.math.sub", "Subtract", FieldNodeCategories.Math, Op: "sub"),
        new("field.math.div", "Divide", FieldNodeCategories.Math, Op: "div"),
        new("field.math.min", "Minimum", FieldNodeCategories.Math, Op: "min"),
        new("field.math.max", "Maximum", FieldNodeCategories.Math, Op: "max"),
        new("field.math.abs", "Absolute", FieldNodeCategories.Math, Op: "abs"),
        new("field.math.sign", "Sign", FieldNodeCategories.Math, Op: "sign"),
        new("field.math.floor", "Floor", FieldNodeCategories.Math, Op: "floor"),
        new("field.math.ceil", "Ceiling", FieldNodeCategories.Math, Op: "ceil"),
        new("field.math.wrap", "Wrap", FieldNodeCategories.Math, Op: "wrap"),
        new("field.math.fract", "Fraction", FieldNodeCategories.Math, Op: "fract"),
        new("field.math.sqrt", "Square Root", FieldNodeCategories.Math, Op: "sqrt"),
        new("field.math.square", "Square", FieldNodeCategories.Math, Op: "square"),
        new("field.math.cube", "Cube", FieldNodeCategories.Math, Op: "cube"),
        new("field.math.exp", "Exponential", FieldNodeCategories.Math, Op: "exp"),
        new("field.math.log", "Log", FieldNodeCategories.Math, Op: "log"),
    };

    private static readonly NodeSpec[] DelayNodes =
    {
        new("field.delay.1ms", "Delay 1 ms", FieldNodeCategories.Time, Ms: 1),
        new("field.delay.5ms", "Delay 5 ms", FieldNodeCategories.Time, Ms: 5),
        new("field.delay.10ms", "Delay 10 ms", FieldNodeCategories.Time, Ms: 10),
        new("field.delay.25ms", "Delay 25 ms", FieldNodeCategories.Time, Ms: 25),
        new("field.delay.50ms", "Delay 50 ms", FieldNodeCategories.Time, Ms: 50),
        new("field.delay.100ms", "Delay 100 ms", FieldNodeCategories.Time, Ms: 100),
        new("field.delay.250ms", "Delay 250 ms", FieldNodeCategories.Time, Ms: 250),
        new("field.delay.500ms", "Delay 500 ms", FieldNodeCategories.Time, Ms: 500),
    };

    private static readonly NodeSpec[] NoteNodes =
    {
        new("field.note.velocity", "Velocity Scale", FieldNodeCategories.Logic, Scale: 1f),
        new("field.note.pitchbend", "Pitch Bend Scale", FieldNodeCategories.Logic, Scale: 0.5f),
        new("field.note.gate", "Gate Length", FieldNodeCategories.Logic, Scale: 0.8f),
        new("field.note.octave.up", "Octave Up", FieldNodeCategories.Logic, Scale: 2f),
        new("field.note.octave.down", "Octave Down", FieldNodeCategories.Logic, Scale: 0.5f),
        new("field.note.fifth", "Fifth Offset", FieldNodeCategories.Logic, Scale: 1.498f),
        new("field.note.quantize", "Note Quantize", FieldNodeCategories.Logic, Scale: 1f),
        new("field.note.humanize", "Note Humanize", FieldNodeCategories.Logic, Scale: 0.95f),
        new("field.note.probability", "Note Probability", FieldNodeCategories.Logic, Scale: 0.75f),
        new("field.note.latch", "Note Latch", FieldNodeCategories.Logic, Scale: 1f),
    };

    private static readonly NodeSpec[] SpectralNodes =
    {
        new("field.spectral.low", "Low Band", FieldNodeCategories.Spectral, Band: 0),
        new("field.spectral.mid", "Mid Band", FieldNodeCategories.Spectral, Band: 1),
        new("field.spectral.high", "High Band", FieldNodeCategories.Spectral, Band: 2),
        new("field.spectral.air", "Air Band", FieldNodeCategories.Spectral, Band: 3),
        new("field.spectral.body", "Body Band", FieldNodeCategories.Spectral, Band: 4),
        new("field.spectral.transient", "Transient Emphasis", FieldNodeCategories.Spectral, Band: 5),
        new("field.spectral.sustain", "Sustain Emphasis", FieldNodeCategories.Spectral, Band: 6),
        new("field.spectral.harmonic.odd", "Odd Harmonics", FieldNodeCategories.Spectral, Band: 7),
        new("field.spectral.harmonic.even", "Even Harmonics", FieldNodeCategories.Spectral, Band: 8),
        new("field.spectral.noise", "Noise Emphasis", FieldNodeCategories.Spectral, Band: 9),
    };

    private static readonly NodeSpec[] ExtraNodes =
    {
        new("field.polysynth.blend", "Poly Blend", FieldNodeCategories.Oscillators, 0.5),
        new("field.polysynth.noise", "Poly Noise", FieldNodeCategories.Oscillators, 0.25),
        new("field.polymer.dual", "Dual Osc", FieldNodeCategories.Oscillators, 0.65),
        new("field.compressor.plus", "Comp+", FieldNodeCategories.Dynamics, 0.8),
        new("field.limiter.peak", "Peak Lim", FieldNodeCategories.Dynamics, 0.9),
        new("field.tool.meter", "Tool Meter", FieldNodeCategories.Io, 1.0),
        new("field.chorus.plus", "Chorus+", FieldNodeCategories.Time, 0.6),
        new("field.flanger.plus", "Flanger+", FieldNodeCategories.Time, 0.6),
        new("field.phaser.plus", "Phaser+", FieldNodeCategories.Time, 0.6),
        new("field.osc.scope", "Scope Tap", FieldNodeCategories.Io, 1.0),
        new("field.midi.song", "Song Select", FieldNodeCategories.Io, 1.0),
        new("field.drum.v0kick", "v0 Kick", FieldNodeCategories.Sampler, 1.0),
        new("field.drum.v1kick", "v1 Kick", FieldNodeCategories.Sampler, 1.0),
        new("field.drum.v8kick", "v8 Kick", FieldNodeCategories.Sampler, 1.0),
        new("field.drum.v9kick", "v9 Kick", FieldNodeCategories.Sampler, 1.0),
        new("field.container.macro1", "Macro 1", FieldNodeCategories.Containers, 1.0),
        new("field.container.macro2", "Macro 2", FieldNodeCategories.Containers, 1.0),
        new("field.container.macro3", "Macro 3", FieldNodeCategories.Containers, 1.0),
        new("field.container.macro4", "Macro 4", FieldNodeCategories.Containers, 1.0),
    };

    private sealed class FixedGainNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly string _category;
        private readonly double _gain;

        public FixedGainNode(string typeId, string displayName, double gain, string category = FieldNodeCategories.Math)
        {
            _typeId = typeId;
            _displayName = displayName;
            _category = category;
            _gain = gain;
            AddInput("in", "In");
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => _category;

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            var input = ctx.Input(0);
            var output = ctx.Output(0);
            var g = (float)_gain;
            for (var i = 0; i < ctx.Frames; i++) output[i] = input[i] * g;
        }
    }

    private sealed class FixedFilterNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly FilterMode _mode;
        private readonly double _cutoff;
        private Biquad[] _bq = Array.Empty<Biquad>();

        public FixedFilterNode(string typeId, string displayName, FilterMode mode, double cutoff)
        {
            _typeId = typeId;
            _displayName = displayName;
            _mode = mode;
            _cutoff = cutoff;
            AddInput("in", "In");
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => FieldNodeCategories.Filters;

        public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
        {
            base.Prepare(format, maxBlock, voiceCount);
            _bq = new Biquad[VoiceCount];
        }

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            ref var bq = ref _bq[ctx.Voice];
            var coeffs = BiquadCoefficients.Compute(_mode, _cutoff, 1.0, Format.SampleRate);
            var input = ctx.Input(0);
            var output = ctx.Output(0);
            for (var i = 0; i < ctx.Frames; i++) output[i] = (float)bq.Process(coeffs, input[i]);
        }
    }

    private sealed class FixedShaperNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly double _drive;

        public FixedShaperNode(string typeId, string displayName, double drive)
        {
            _typeId = typeId;
            _displayName = displayName;
            _drive = drive;
            AddInput("in", "In");
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => FieldNodeCategories.Shapers;

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            var input = ctx.Input(0);
            var output = ctx.Output(0);
            var drive = (float)_drive;
            for (var i = 0; i < ctx.Frames; i++)
                output[i] = WaveShaper.Shape(input[i], ShaperType.Tanh, drive);
        }
    }

    private sealed class FixedBitcrusherNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly int _bits;
        private readonly int _downsample;
        private BitcrusherDsp[] _dsp = Array.Empty<BitcrusherDsp>();

        public FixedBitcrusherNode(string typeId, string displayName, int bits, int downsample)
        {
            _typeId = typeId;
            _displayName = displayName;
            _bits = bits;
            _downsample = downsample;
            AddInput("in", "In");
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => FieldNodeCategories.Shapers;

        public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
        {
            base.Prepare(format, maxBlock, voiceCount);
            _dsp = new BitcrusherDsp[VoiceCount];
            for (var i = 0; i < VoiceCount; i++) _dsp[i] = new BitcrusherDsp();
        }

        public override void ResetVoice(int voice)
        {
            if (voice < _dsp.Length) _dsp[voice].Reset();
        }

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            var d = _dsp[ctx.Voice];
            d.Bits = _bits;
            d.Downsample = _downsample;
            d.Mix = 1;
            var input = ctx.Input(0);
            var output = ctx.Output(0);
            for (var i = 0; i < ctx.Frames; i++)
                output[i] = d.Process(input[i]);
        }
    }

    private sealed class MsegVariantNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly int _segments;
        private DahdsrEnvelope[] _env = Array.Empty<DahdsrEnvelope>();

        public MsegVariantNode(string typeId, string displayName, int segments)
        {
            _typeId = typeId;
            _displayName = displayName;
            _segments = segments;
            AddInput("gate", "Gate");
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => FieldNodeCategories.Envelopes;

        public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
        {
            base.Prepare(format, maxBlock, voiceCount);
            _env = new DahdsrEnvelope[VoiceCount];
            for (var i = 0; i < VoiceCount; i++) _env[i] = new DahdsrEnvelope();
        }

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            var gate = ctx.Input(0);
            var output = ctx.Output(0);
            var env = _env[ctx.Voice];
            env.SetSampleRate(Format.SampleRate);
            env.DecaySeconds = 0.1 + _segments * 0.02;
            env.SustainLevel = 0.7;
            env.ReleaseSeconds = 0.2;
            var wasHigh = false;
            for (var i = 0; i < ctx.Frames; i++)
            {
                var high = gate[i] > 0.5f;
                if (high && !wasHigh) env.Gate();
                if (!high && wasHigh) env.Release();
                wasHigh = high;
                output[i] = env.Process();
            }
        }
    }

    private sealed class FixedLfoNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly double _rate;
        private Lfo[] _lfo = Array.Empty<Lfo>();

        public FixedLfoNode(string typeId, string displayName, double rate)
        {
            _typeId = typeId;
            _displayName = displayName;
            _rate = rate;
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => FieldNodeCategories.Modulators;

        public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
        {
            base.Prepare(format, maxBlock, voiceCount);
            _lfo = new Lfo[VoiceCount];
            for (var i = 0; i < VoiceCount; i++) _lfo[i] = new Lfo();
        }

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            var output = ctx.Output(0);
            var lfo = _lfo[ctx.Voice];
            lfo.SetRate(_rate, Format.SampleRate);
            for (var i = 0; i < ctx.Frames; i++) output[i] = (float)lfo.Next();
        }
    }

    private sealed class BinaryMathNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly string _op;

        public BinaryMathNode(string typeId, string displayName, string op)
        {
            _typeId = typeId;
            _displayName = displayName;
            _op = op;
            AddInput("a", "A");
            AddInput("b", "B");
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => FieldNodeCategories.Math;

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            var a = ctx.Input(0);
            var b = ctx.Input(1);
            var output = ctx.Output(0);
            for (var i = 0; i < ctx.Frames; i++)
            {
                var av = a[i];
                var bv = b[i];
                output[i] = _op switch
                {
                    "sub" => av - bv,
                    "div" => MathF.Abs(bv) > 1e-6f ? av / bv : 0f,
                    "min" => MathF.Min(av, bv),
                    "max" => MathF.Max(av, bv),
                    "abs" => MathF.Abs(av),
                    "sign" => MathF.Sign(av),
                    "floor" => MathF.Floor(av),
                    "ceil" => MathF.Ceiling(av),
                    "wrap" => av - MathF.Floor(av),
                    "fract" => av - MathF.Truncate(av),
                    "sqrt" => MathF.Sqrt(MathF.Abs(av)),
                    "square" => av * av,
                    "cube" => av * av * av,
                    "exp" => MathF.Exp(Math.Clamp(av, -10f, 10f)),
                    "log" => MathF.Log(MathF.Max(MathF.Abs(av), 1e-6f)),
                    _ => av
                };
            }
        }
    }

    private sealed class FixedDelayNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly double _ms;
        private DelayLine[] _lines = Array.Empty<DelayLine>();

        public FixedDelayNode(string typeId, string displayName, double ms)
        {
            _typeId = typeId;
            _displayName = displayName;
            _ms = ms;
            AddInput("in", "In");
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => FieldNodeCategories.Time;

        public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
        {
            base.Prepare(format, maxBlock, voiceCount);
            var size = (int)(_ms / 1000.0 * Format.SampleRate) + 4;
            _lines = new DelayLine[VoiceCount];
            for (var i = 0; i < VoiceCount; i++) { _lines[i] = new DelayLine(); _lines[i].Resize(Math.Max(size, 8)); }
        }

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            var input = ctx.Input(0);
            var output = ctx.Output(0);
            var line = _lines[ctx.Voice];
            var delay = (int)(_ms / 1000.0 * Format.SampleRate);
            for (var i = 0; i < ctx.Frames; i++)
            {
                var x = input[i];
                output[i] = line.ReadInt(delay);
                line.Write(x);
            }
        }
    }

    private sealed class NoteUtilityNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly float _scale;

        public NoteUtilityNode(string typeId, string displayName, float scale)
        {
            _typeId = typeId;
            _displayName = displayName;
            _scale = scale;
            AddInput("in", "In");
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => FieldNodeCategories.Logic;

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            var input = ctx.Input(0);
            var output = ctx.Output(0);
            for (var i = 0; i < ctx.Frames; i++) output[i] = input[i] * _scale;
        }
    }

    private sealed class SpectralUtilityNode : FieldNode
    {
        private readonly string _typeId;
        private readonly string _displayName;
        private readonly int _band;
        private readonly OnePole _lp = new();
        private readonly OnePole _hp = new();

        public SpectralUtilityNode(string typeId, string displayName, int band)
        {
            _typeId = typeId;
            _displayName = displayName;
            _band = band;
            AddInput("in", "In");
            AddOutput("out", "Out");
            Build();
        }

        public override string TypeId => _typeId;
        public override string DisplayName => _displayName;
        public override string Category => FieldNodeCategories.Spectral;

        public override void ProcessBlock(FieldRenderContext ctx)
        {
            var input = ctx.Input(0);
            var output = ctx.Output(0);
            var sr = Format.SampleRate;
            _lp.SetLowpass(250, sr);
            _hp.SetLowpass(4000, sr);
            for (var i = 0; i < ctx.Frames; i++)
            {
                var x = input[i];
                var low = (float)_lp.ProcessLP(x);
                var hp = (float)_hp.ProcessHP(x);
                output[i] = _band switch
                {
                    0 => low,
                    2 => hp,
                    1 => x - low - hp,
                    _ => x * (0.5f + _band * 0.05f)
                };
            }
        }
    }
}
