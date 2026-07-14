using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A feedback comb filter with stereo offset: Delay, Feedback, Mix, and Stereo width.
/// </summary>
public sealed class CombEffect : IAudioEffect
{
    public const string TypeId = "comb";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double DelayMs { get; set; } = 8.0;
    public double Feedback { get; set; } = 0.5;
    public double Mix { get; set; } = 0.5;
    public double Stereo { get; set; } = 0.3;

    private int _channels = 2;
    private int _sampleRate = 44100;
    private readonly CombFilter _comb = new();

    public string Name => "Comb";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Delay", 0.1, 30.0, () => DelayMs, v => DelayMs = v, "0.#", "ms"),
        new FloatParameter("Feedback", 0.0, 0.9, () => Feedback, v => Feedback = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new FloatParameter("Stereo", 0.0, 0.5, () => Stereo, v => Stereo = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _comb.Reset();
        _comb.Configure(DelayMs, Stereo, Feedback, Mix, _sampleRate);
    }

    public IAudioEffect Clone() => new CombEffect
    {
        Enabled = Enabled, DelayMs = DelayMs, Feedback = Feedback, Mix = Mix, Stereo = Stereo
    };

    public void Process(Span<float> buffer)
    {
        _comb.Configure(DelayMs, Stereo, Feedback, Mix, _sampleRate);
        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;

        if (channels >= 2)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                _comb.Process(buffer[i], buffer[i + 1], out var l, out var r);
                buffer[i] = l;
                buffer[i + 1] = r;
            }

            return;
        }

        // Mono: feed both comb inputs the same sample and average the outputs.
        for (var frame = 0; frame < frames; frame++)
        {
            var dry = buffer[frame];
            _comb.Process(dry, dry, out var l, out var r);
            buffer[frame] = (l + r) * 0.5f;
        }
    }
}
