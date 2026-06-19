using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A feedback delay (echo): a per-channel ring buffer with adjustable delay time, feedback amount,
/// and dry/wet mix.
/// </summary>
public sealed class DelayEffect : IAudioEffect
{
    public const string TypeId = "delay";

    string IAudioEffect.TypeId => TypeId;

    private const double MaxDelaySeconds = 2.0;

    public bool Enabled { get; set; } = true;

    public double TimeMs { get; set; } = 300.0;
    public double Feedback { get; set; } = 0.4;
    public double Mix { get; set; } = 0.35;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private DelayLine[] _lines = Array.Empty<DelayLine>();
    private int _size;

    public string Name => "Delay";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Time", 1.0, 2000.0, () => TimeMs, v => TimeMs = v, "0", "ms", 2.0),
        new FloatParameter("Feedback", 0.0, 0.95, () => Feedback, v => Feedback = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _size = (int)(MaxDelaySeconds * _sampleRate) + 4;
        _lines = new DelayLine[_channels];
        for (var c = 0; c < _channels; c++) { _lines[c] = new DelayLine(); _lines[c].Resize(_size); }
    }

    public IAudioEffect Clone() => new DelayEffect
    {
        Enabled = Enabled, TimeMs = TimeMs, Feedback = Feedback, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        if (_lines.Length == 0) return;
        var channels = _channels;
        var delay = Math.Clamp((int)(TimeMs / 1000.0 * _sampleRate), 1, _size - 1);
        var fb = (float)Math.Clamp(Feedback, 0, 0.95);
        var mix = (float)Math.Clamp(Mix, 0, 1);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var delayed = _lines[c].ReadInt(delay);
                buffer[i + c] = dry * (1 - mix) + delayed * mix;
                _lines[c].Write(dry + delayed * fb);
            }
        }
    }
}
