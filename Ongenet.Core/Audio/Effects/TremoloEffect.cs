using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Amplitude modulation: Tremolo (both channels dip together) or Auto-Pan (the signal sweeps across
/// the stereo field, constant-power). Rate, depth and LFO waveform are adjustable.
/// </summary>
public sealed class TremoloEffect : IAudioEffect
{
    public const string TypeId = "tremolo";

    private static readonly string[] WaveNames = { "Sine", "Triangle", "Square", "Saw" };
    private static readonly string[] ModeNames = { "Tremolo", "Auto-Pan" };

    public bool Enabled { get; set; } = true;

    public double RateHz { get; set; } = 4.0;
    public double Depth { get; set; } = 0.6;
    public int WaveformIndex { get; set; }
    public int Mode { get; set; }

    private double _sampleRate = 44100.0;
    private int _channels = 2;
    private readonly Lfo _lfo = new();

    public string Name => "Tremolo";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Rate", 0.05, 20.0, () => RateHz, v => RateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Depth", 0.0, 1.0, () => Depth, v => Depth = v),
        new ChoiceParameter("Waveform", WaveNames, () => WaveformIndex, v => WaveformIndex = v),
        new ChoiceParameter("Mode", ModeNames, () => Mode, v => Mode = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _lfo.Reset();
    }

    public IAudioEffect Clone() => new TremoloEffect
    {
        Enabled = Enabled, RateHz = RateHz, Depth = Depth, WaveformIndex = WaveformIndex, Mode = Mode
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        var depth = (float)Math.Clamp(Depth, 0, 1);
        _lfo.Wave = (LfoWave)Math.Clamp(WaveformIndex, 0, 3);
        _lfo.SetRate(RateHz, _sampleRate);
        var autoPan = Mode == 1 && channels >= 2;

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var lfo = _lfo.Next(); // [-1, 1]

            if (autoPan)
            {
                var (gl, gr) = Mixing.StripGains(1.0, depth * lfo);
                const float center = 1.41421356f; // √2 so a centred pan is unity
                buffer[i] *= gl * center;
                buffer[i + 1] *= gr * center;
            }
            else
            {
                var m = 0.5f + 0.5f * (float)lfo;      // 0..1
                var gain = 1f - depth + depth * m;     // 1 at peak, (1-depth) at trough
                for (var c = 0; c < channels; c++) buffer[i + c] *= gain;
            }
        }
    }
}
