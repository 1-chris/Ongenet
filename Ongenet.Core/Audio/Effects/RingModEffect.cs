using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Classic ring modulator via <see cref="RingModulator"/>: multiplies the input by a sine carrier.
/// </summary>
public sealed class RingModEffect : IAudioEffect
{
    public const string TypeId = "ring_mod";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double FrequencyHz { get; set; } = 440.0;
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private RingModulator[] _mods = Array.Empty<RingModulator>();

    public string Name => "Ring Mod";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Frequency", 1.0, 8000.0, () => FrequencyHz, v => FrequencyHz = v, "0", "Hz", 2.0),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var mods = new RingModulator[_channels];
        for (var c = 0; c < _channels; c++)
        {
            mods[c] = new RingModulator();
            mods[c].Configure(FrequencyHz, (int)_sampleRate);
            mods[c].Reset();
        }

        _mods = mods;
    }

    public IAudioEffect Clone() => new RingModEffect
    {
        Enabled = Enabled, FrequencyHz = FrequencyHz, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var mods = _mods;
        if (mods.Length == 0) return;

        var channels = Math.Min(_channels < 1 ? 1 : _channels, mods.Length);
        var mix = (float)Math.Clamp(Mix, 0, 1);

        for (var c = 0; c < channels; c++)
        {
            mods[c].Configure(FrequencyHz, (int)_sampleRate);
            mods[c].Mix = mix;
        }

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
                buffer[i + c] = mods[c].Process(buffer[i + c]);
        }
    }
}
