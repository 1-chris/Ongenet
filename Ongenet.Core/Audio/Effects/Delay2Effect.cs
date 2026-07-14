using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Stereo offset delay: left and right delay times differ by OffsetMs for a widening slap/echo.
/// </summary>
public sealed class Delay2Effect : IAudioEffect
{
    public const string TypeId = "delay2";

    string IAudioEffect.TypeId => TypeId;

    private const double MaxDelaySeconds = 2.0;

    public bool Enabled { get; set; } = true;

    public double TimeMs { get; set; } = 250.0;
    public double OffsetMs { get; set; } = 30.0;
    public double Feedback { get; set; } = 0.3;
    public double Mix { get; set; } = 0.35;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private DelayLine[] _lines = Array.Empty<DelayLine>();

    public string Name => "Delay 2";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Time", 1.0, 2000.0, () => TimeMs, v => TimeMs = v, "0", "ms", 2.0),
        new FloatParameter("Offset", 0.0, 200.0, () => OffsetMs, v => OffsetMs = v, "0", "ms"),
        new FloatParameter("Feedback", 0.0, 0.95, () => Feedback, v => Feedback = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var size = (int)(MaxDelaySeconds * _sampleRate) + 4;
        var lines = new DelayLine[_channels];
        for (var c = 0; c < _channels; c++) { lines[c] = new DelayLine(); lines[c].Resize(size); }
        _lines = lines;
    }

    public IAudioEffect Clone() => new Delay2Effect
    {
        Enabled = Enabled, TimeMs = TimeMs, OffsetMs = OffsetMs, Feedback = Feedback, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var lines = _lines;
        if (lines.Length == 0) return;
        var channels = Math.Min(_channels, lines.Length);
        var lineSize = lines[0].Size;
        if (lineSize <= 1) return;

        var baseDelay = TimeMs / 1000.0 * _sampleRate;
        var offset = OffsetMs / 1000.0 * _sampleRate;
        var delayL = Math.Clamp((int)Math.Max(1, baseDelay), 1, lineSize - 1);
        var delayR = Math.Clamp((int)Math.Max(1, baseDelay + offset), 1, lineSize - 1);
        var fb = (float)Math.Clamp(Feedback, 0, 0.95);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var frames = buffer.Length / channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var delay = c == 1 ? delayR : delayL;
                var dry = buffer[i + c];
                var delayed = lines[c].ReadInt(delay);
                buffer[i + c] = dry * (1 - mix) + delayed * mix;
                lines[c].Write(dry + delayed * fb);
            }
        }
    }
}
