using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Frequency shifter via Hilbert single-sideband (<see cref="FreqShifter"/>), blended with dry via Mix.
/// </summary>
public sealed class FreqShiftEffect : IAudioEffect
{
    public const string TypeId = "freq_shifter";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double FrequencyHz { get; set; } = 100.0;
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private FreqShifter[] _shifters = Array.Empty<FreqShifter>();

    public string Name => "Freq Shifter";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Frequency", -2000.0, 2000.0, () => FrequencyHz, v => FrequencyHz = v, "0", "Hz"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var shifters = new FreqShifter[_channels];
        for (var c = 0; c < _channels; c++)
        {
            shifters[c] = new FreqShifter();
            shifters[c].Configure(FrequencyHz, (int)_sampleRate);
            shifters[c].Reset();
        }

        _shifters = shifters;
    }

    public IAudioEffect Clone() => new FreqShiftEffect
    {
        Enabled = Enabled, FrequencyHz = FrequencyHz, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var shifters = _shifters;
        if (shifters.Length == 0) return;

        var channels = Math.Min(_channels < 1 ? 1 : _channels, shifters.Length);
        var mix = (float)Math.Clamp(Mix, 0, 1);

        for (var c = 0; c < channels; c++)
            shifters[c].Configure(FrequencyHz, (int)_sampleRate);

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
