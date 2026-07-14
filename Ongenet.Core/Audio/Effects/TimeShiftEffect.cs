using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Varispeed time manipulation via a per-channel pitch shifter: Speed scales playback rate
/// (pitch and duration change together, like tape), blended with the dry signal.
/// </summary>
public sealed class TimeShiftEffect : IAudioEffect
{
    public const string TypeId = "time_shift";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Speed { get; set; } = 1.0;
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private PitchShifter[] _shifters = Array.Empty<PitchShifter>();

    public string Name => "Time Shift";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Speed", 0.25, 4.0, () => Speed, v => Speed = v, "0.##", "x", 2.0),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var shifters = new PitchShifter[_channels];
        for (var c = 0; c < _channels; c++)
        {
            shifters[c] = new PitchShifter();
            shifters[c].Configure(_sampleRate);
        }

        _shifters = shifters;
    }

    public IAudioEffect Clone() => new TimeShiftEffect
    {
        Enabled = Enabled, Speed = Speed, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var shifters = _shifters;
        if (shifters.Length == 0) return;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, shifters.Length);
        var ratio = Math.Clamp(Speed, 0.25, 4.0);
        for (var c = 0; c < channels; c++) shifters[c].SetRatio(ratio);
        var mix = (float)Math.Clamp(Mix, 0, 1);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var wet = shifters[c].Process(dry);
                buffer[i + c] = dry * (1 - mix) + wet * mix;
            }
        }
    }
}
