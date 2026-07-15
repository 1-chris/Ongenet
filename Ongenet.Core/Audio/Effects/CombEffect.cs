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
    private CombFilter _comb = new();

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
        var sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var comb = new CombFilter();
        comb.Configure(DelayMs, Stereo, Feedback, Mix, sampleRate);
        comb.Reset();

        // Publish a fully-configured instance — RebuildTracks can call Prepare from the UI thread
        // while Process runs on the audio worker pool (e.g. after "Render clip to new track").
        _sampleRate = sampleRate;
        _channels = channels;
        _comb = comb;
    }

    public IAudioEffect Clone() => new CombEffect
    {
        Enabled = Enabled, DelayMs = DelayMs, Feedback = Feedback, Mix = Mix, Stereo = Stereo
    };

    public void Process(Span<float> buffer)
    {
        var comb = _comb;
        var sampleRate = _sampleRate;
        comb.Configure(DelayMs, Stereo, Feedback, Mix, sampleRate);
        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;

        if (channels >= 2)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                comb.Process(buffer[i], buffer[i + 1], out var l, out var r);
                buffer[i] = l;
                buffer[i + 1] = r;
            }

            return;
        }

        // Mono: feed both comb inputs the same sample and average the outputs.
        for (var frame = 0; frame < frames; frame++)
        {
            var dry = buffer[frame];
            comb.Process(dry, dry, out var l, out var r);
            buffer[frame] = (l + r) * 0.5f;
        }
    }
}
