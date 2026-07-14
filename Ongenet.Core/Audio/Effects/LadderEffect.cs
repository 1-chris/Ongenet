using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A four-pole resonant low-pass ladder: four one-pole stages in series with resonance feedback
/// and optional input drive.
/// </summary>
public sealed class LadderEffect : IAudioEffect
{
    public const string TypeId = "ladder";

    string IAudioEffect.TypeId => TypeId;

    private const int Poles = 4;

    public bool Enabled { get; set; } = true;

    public double Cutoff { get; set; } = 1000.0;
    public double Resonance { get; set; } = 0.5;
    public double Drive { get; set; } = 1.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private OnePole[][] _poles = Array.Empty<OnePole[]>();
    private float[] _lastOut = Array.Empty<float>();
    private double _lastCutoff = double.NaN, _lastSr = double.NaN;

    public string Name => "Ladder";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Cutoff", 20.0, 20000.0, () => Cutoff, v => Cutoff = v, "0", "Hz", 3.0),
        new FloatParameter("Resonance", 0.0, 1.0, () => Resonance, v => Resonance = v),
        new FloatParameter("Drive", 1.0, 24.0, () => Drive, v => Drive = v, "0.0")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var poles = new OnePole[_channels][];
        for (var c = 0; c < _channels; c++)
        {
            poles[c] = new OnePole[Poles];
            for (var p = 0; p < Poles; p++) poles[c][p] = new OnePole();
        }

        _poles = poles;
        _lastOut = new float[_channels];
        _lastCutoff = double.NaN;
    }

    public IAudioEffect Clone() => new LadderEffect
    {
        Enabled = Enabled, Cutoff = Cutoff, Resonance = Resonance, Drive = Drive
    };

    public void Process(Span<float> buffer)
    {
        var poles = _poles;
        var lastOut = _lastOut;
        if (poles.Length == 0 || lastOut.Length == 0) return;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, Math.Min(poles.Length, lastOut.Length));
        var cutoff = Math.Clamp(Cutoff, 20, _sampleRate * 0.45);

        if (cutoff != _lastCutoff || _sampleRate != _lastSr)
        {
            for (var c = 0; c < channels; c++)
                for (var p = 0; p < Poles; p++)
                    poles[c][p].SetLowpass(cutoff, _sampleRate);
            _lastCutoff = cutoff;
            _lastSr = _sampleRate;
        }

        var drive = (float)Math.Max(1e-6, Drive);
        var fb = (float)Math.Clamp(Resonance, 0, 1) * 3.8f;
        var frames = buffer.Length / channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var x = WaveShaper.Shape(buffer[i + c], ShaperType.Tanh, drive);
                var stage = poles[c];
                var s = x - fb * lastOut[c];
                for (var p = 0; p < Poles; p++)
                    s = (float)stage[p].ProcessLP(s);
                lastOut[c] = s;
                buffer[i + c] = s;
            }
        }
    }
}
