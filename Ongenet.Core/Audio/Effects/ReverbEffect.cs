using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A stereo reverb based on the classic Freeverb topology (8 parallel comb filters with damping
/// feeding 4 series all-pass filters, per channel). Parameters: Mix (dry/wet), Room Size,
/// Damping, Width. The first built-in effect.
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

    private Comb[] _combL = Array.Empty<Comb>();
    private Comb[] _combR = Array.Empty<Comb>();
    private Allpass[] _allpassL = Array.Empty<Allpass>();
    private Allpass[] _allpassR = Array.Empty<Allpass>();

    public string Name => "Reverb";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new FloatParameter("Room Size", 0.0, 1.0, () => RoomSize, v => RoomSize = v),
        new FloatParameter("Damping", 0.0, 1.0, () => Damping, v => Damping = v),
        new FloatParameter("Width", 0.0, 1.0, () => Width, v => Width = v)
    };

    public void Prepare(AudioFormat format)
    {
        var scale = format.SampleRate / 44100.0;
        _combL = new Comb[CombTuning.Length];
        _combR = new Comb[CombTuning.Length];
        for (var i = 0; i < CombTuning.Length; i++)
        {
            _combL[i] = new Comb((int)(CombTuning[i] * scale));
            _combR[i] = new Comb((int)((CombTuning[i] + StereoSpread) * scale));
        }

        _allpassL = new Allpass[AllpassTuning.Length];
        _allpassR = new Allpass[AllpassTuning.Length];
        for (var i = 0; i < AllpassTuning.Length; i++)
        {
            _allpassL[i] = new Allpass((int)(AllpassTuning[i] * scale));
            _allpassR[i] = new Allpass((int)((AllpassTuning[i] + StereoSpread) * scale));
        }
    }

    public IAudioEffect Clone() => new ReverbEffect { Enabled = Enabled, Mix = Mix, RoomSize = RoomSize, Damping = Damping, Width = Width };

    public void Process(Span<float> buffer)
    {
        if (_combL.Length == 0) return;

        // Read parameters once per block so live edits take effect.
        var feedback = (float)(RoomSize * ScaleRoom + OffsetRoom);
        var damp = (float)(Damping * 0.4);
        foreach (var c in _combL) { c.Feedback = feedback; c.Damp = damp; }
        foreach (var c in _combR) { c.Feedback = feedback; c.Damp = damp; }

        var wet = (float)Mix;
        var dry = 1f - wet;
        var width = (float)Width;
        var wet1 = wet * (width / 2f + 0.5f);
        var wet2 = wet * ((1f - width) / 2f);

        var frames = buffer.Length / 2;
        if (buffer.Length % 2 != 0) frames = buffer.Length; // mono fallback handled below

        if (buffer.Length >= 2 && buffer.Length % 2 == 0)
        {
            for (var f = 0; f < frames; f++)
            {
                var i = f * 2;
                float inL = buffer[i], inR = buffer[i + 1];
                var input = (inL + inR) * FixedGain;

                float outL = 0, outR = 0;
                foreach (var c in _combL) outL += c.Process(input);
                foreach (var c in _combR) outR += c.Process(input);
                foreach (var a in _allpassL) outL = a.Process(outL);
                foreach (var a in _allpassR) outR = a.Process(outR);

                buffer[i] = inL * dry + outL * wet1 + outR * wet2;
                buffer[i + 1] = inR * dry + outR * wet1 + outL * wet2;
            }
        }
        else
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var input = buffer[i] * FixedGain;
                float o = 0;
                foreach (var c in _combL) o += c.Process(input);
                foreach (var a in _allpassL) o = a.Process(o);
                buffer[i] = buffer[i] * dry + o * wet;
            }
        }
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
