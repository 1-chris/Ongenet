using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Per-channel time-domain pitch shifter with dry/wet mix.
/// </summary>
public sealed class PitchShiftEffect : IAudioEffect
{
    public const string TypeId = "pitch_shifter";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Semitones { get; set; }
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private PitchShifter[] _shifters = Array.Empty<PitchShifter>();

    public string Name => "Pitch Shifter";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Semitones", -24.0, 24.0, () => Semitones, v => Semitones = v, "0.#"),
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

    public IAudioEffect Clone() => new PitchShiftEffect
    {
        Enabled = Enabled, Semitones = Semitones, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var shifters = _shifters;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, shifters.Length);
        if (channels <= 0) return;

        var ratio = MusicalMath.SemitonesToRatio(Semitones);
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
