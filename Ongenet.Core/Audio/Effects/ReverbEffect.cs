using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A stereo reverb based on the classic Freeverb topology (8 parallel comb filters with damping
/// feeding 4 series all-pass filters, per channel). Parameters: Mix (dry/wet), Room Size,
/// Damping, Width. <see cref="Quality"/> selects a lighter 4-comb/2-allpass variant for insert chains.
/// <see cref="AlgorithmIndex"/> applies factory presets from <see cref="ReverbAlgorithmBank"/>.
/// </summary>
public sealed class ReverbEffect : IAudioEffect
{
    public const string TypeId = "reverb";

    string IAudioEffect.TypeId => TypeId;

    private const float FixedGain = 0.015f;
    private const float ScaleRoom = 0.28f;
    private const float OffsetRoom = 0.7f;
    private const int StereoSpread = 23;

    private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };

    public bool Enabled { get; set; } = true;

    public double Mix { get; set; } = 0.3;
    public double RoomSize { get; set; } = 0.6;
    public double Damping { get; set; } = 0.5;
    public double Width { get; set; } = 1.0;

    /// <summary>0 = full (8 combs), 1 = lite (4 combs, 2 all-pass) for lower CPU on insert chains.</summary>
    public int Quality { get; set; }

    /// <summary>Factory algorithm preset (Room/Hall/Plate/Chamber/Large Hall).</summary>
    public int AlgorithmIndex { get; set; }

    /// <summary>Subtle LFO depth applied to comb damping (0..1).</summary>
    public double ModDepth { get; set; }

    private Comb[] _combL = Array.Empty<Comb>();
    private Comb[] _combR = Array.Empty<Comb>();
    private Allpass[] _allpassL = Array.Empty<Allpass>();
    private Allpass[] _allpassR = Array.Empty<Allpass>();
    private double _lfoPhase;
    private int _lastAlgorithmIndex = -1;

    public string Name => "Reverb";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new FloatParameter("Room Size", 0.0, 1.0, () => RoomSize, v => RoomSize = v),
        new FloatParameter("Damping", 0.0, 1.0, () => Damping, v => Damping = v),
        new FloatParameter("Width", 0.0, 1.0, () => Width, v => Width = v),
        new ChoiceParameter("Quality", new[] { "Full", "Lite" }, () => Quality, v => Quality = v),
        new ChoiceParameter("Algorithm",
            Array.ConvertAll(ReverbAlgorithmBank.Presets, p => p.Name),
            () => AlgorithmIndex, v => AlgorithmIndex = v),
        new FloatParameter("Mod Depth", 0.0, 1.0, () => ModDepth, v => ModDepth = v)
    };

    public void Prepare(AudioFormat format)
    {
        var scale = format.SampleRate / 44100.0;

        var combL = new Comb[CombTuning.Length];
        var combR = new Comb[CombTuning.Length];
        for (var i = 0; i < CombTuning.Length; i++)
        {
            combL[i] = new Comb((int)(CombTuning[i] * scale));
            combR[i] = new Comb((int)((CombTuning[i] + StereoSpread) * scale));
        }

        var allpassL = new Allpass[AllpassTuning.Length];
        var allpassR = new Allpass[AllpassTuning.Length];
        for (var i = 0; i < AllpassTuning.Length; i++)
        {
            allpassL[i] = new Allpass((int)(AllpassTuning[i] * scale));
            allpassR[i] = new Allpass((int)((AllpassTuning[i] + StereoSpread) * scale));
        }

        _combL = combL;
        _combR = combR;
        _allpassL = allpassL;
        _allpassR = allpassR;
        _lfoPhase = 0;
        _lastAlgorithmIndex = -1;
    }

    public IAudioEffect Clone() => new ReverbEffect
    {
        Enabled = Enabled, Mix = Mix, RoomSize = RoomSize, Damping = Damping, Width = Width,
        Quality = Quality, AlgorithmIndex = AlgorithmIndex, ModDepth = ModDepth
    };

    public void Process(Span<float> buffer)
    {
        if (_combL.Length == 0) return;

        ApplyAlgorithmIfChanged();

        var combCount = Quality >= 1 ? 4 : CombTuning.Length;
        var allpassCount = Quality >= 1 ? 2 : AllpassTuning.Length;

        var feedback = (float)(RoomSize * ScaleRoom + OffsetRoom);
        var baseDamp = (float)(Damping * 0.4);
        var modDepth = (float)Math.Clamp(ModDepth, 0, 1) * 0.12f;
        var wet = (float)Mix;
        var dry = 1f - wet;
        var width = (float)Width;
        var wet1 = wet * (width / 2f + 0.5f);
        var wet2 = wet * ((1f - width) / 2f);

        var frames = buffer.Length / 2;
        if (buffer.Length % 2 != 0) frames = buffer.Length;

        if (buffer.Length >= 2 && buffer.Length % 2 == 0)
        {
            for (var f = 0; f < frames; f++)
            {
                var damp = baseDamp + modDepth * MathF.Sin((float)_lfoPhase);
                _lfoPhase += 0.0003;
                if (_lfoPhase > Math.PI * 2) _lfoPhase -= Math.PI * 2;

                for (var i = 0; i < combCount; i++)
                {
                    _combL[i].Feedback = feedback;
                    _combL[i].Damp = damp;
                    _combR[i].Feedback = feedback;
                    _combR[i].Damp = damp;
                }

                var i2 = f * 2;
                float inL = buffer[i2], inR = buffer[i2 + 1];
                var input = (inL + inR) * FixedGain;

                float outL = 0, outR = 0;
                for (var c = 0; c < combCount; c++) outL += _combL[c].Process(input);
                for (var c = 0; c < combCount; c++) outR += _combR[c].Process(input);
                for (var a = 0; a < allpassCount; a++) outL = _allpassL[a].Process(outL);
                for (var a = 0; a < allpassCount; a++) outR = _allpassR[a].Process(outR);

                buffer[i2] = inL * dry + outL * wet1 + outR * wet2;
                buffer[i2 + 1] = inR * dry + outR * wet1 + outL * wet2;
            }
        }
        else
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var damp = baseDamp + modDepth * MathF.Sin((float)_lfoPhase);
                _lfoPhase += 0.0003;
                if (_lfoPhase > Math.PI * 2) _lfoPhase -= Math.PI * 2;

                for (var c = 0; c < combCount; c++)
                {
                    _combL[c].Feedback = feedback;
                    _combL[c].Damp = damp;
                }

                var input = buffer[i] * FixedGain;
                float o = 0;
                for (var c = 0; c < combCount; c++) o += _combL[c].Process(input);
                for (var a = 0; a < allpassCount; a++) o = _allpassL[a].Process(o);
                buffer[i] = buffer[i] * dry + o * wet;
            }
        }
    }

    private void ApplyAlgorithmIfChanged()
    {
        if (AlgorithmIndex == _lastAlgorithmIndex) return;
        _lastAlgorithmIndex = AlgorithmIndex;
        var preset = ReverbAlgorithmBank.Get(AlgorithmIndex);
        RoomSize = preset.RoomSize;
        Damping = preset.Damping;
        Width = preset.Width;
        ModDepth = preset.ModDepth;
    }

    private sealed class Comb
    {
        private readonly float[] _buffer;
        private int _index;
        private float _filterStore;
        public float Feedback;
        public float Damp;

        public Comb(int size) => _buffer = new float[Math.Max(1, size)];

        public float Process(float input)
        {
            var output = _buffer[_index];
            _filterStore = output * (1f - Damp) + _filterStore * Damp;
            _buffer[_index] = input + _filterStore * Feedback;
            if (++_index >= _buffer.Length) _index = 0;
            return output;
        }
    }

    private sealed class Allpass
    {
        private const float Feedback = 0.5f;
        private readonly float[] _buffer;
        private int _index;

        public Allpass(int size) => _buffer = new float[Math.Max(1, size)];

        public float Process(float input)
        {
            var buffered = _buffer[_index];
            var output = -input + buffered;
            _buffer[_index] = input + buffered * Feedback;
            if (++_index >= _buffer.Length) _index = 0;
            return output;
        }
    }
}
