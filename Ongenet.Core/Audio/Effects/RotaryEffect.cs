using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Leslie rotary-speaker emulation: horn and drum rotors with Doppler pitch wobble and amplitude
/// tremolo, plus optional pre-drive and dry/wet mix.
/// </summary>
public sealed class RotaryEffect : IAudioEffect
{
    public const string TypeId = "rotary";

    string IAudioEffect.TypeId => TypeId;

    private static readonly string[] SpeedNames = { "Slow", "Fast" };

    public bool Enabled { get; set; } = true;

    public int SpeedIndex { get; set; }
    public double SpeedHz { get; set; }
    public double DriveDb { get; set; }
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private readonly RotarySpeaker _rotary = new();

    public string Name => "Rotary";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Speed", SpeedNames, () => SpeedIndex, v => SpeedIndex = v),
        new FloatParameter("Speed Hz", 0.0, 12.0, () => SpeedHz, v => SpeedHz = v, "0.##", "Hz"),
        new FloatParameter("Drive", 0.0, 24.0, () => DriveDb, v => DriveDb = v, "0.#", "dB"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _rotary.Configure((int)_sampleRate);
        _rotary.Reset();
        ApplySpeed();
    }

    public IAudioEffect Clone() => new RotaryEffect
    {
        Enabled = Enabled, SpeedIndex = SpeedIndex, SpeedHz = SpeedHz, DriveDb = DriveDb, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        _rotary.SetDrive(DriveDb);
        _rotary.Mix = (float)Math.Clamp(Mix, 0, 1);
        ApplySpeed();

        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            if (channels >= 2)
            {
                _rotary.Process(buffer[i], buffer[i + 1], out var outL, out var outR);
                buffer[i] = outL;
                buffer[i + 1] = outR;
            }
            else
            {
                _rotary.Process(buffer[i], buffer[i], out var outL, out _);
                buffer[i] = outL;
            }
        }
    }

    private void ApplySpeed()
    {
        if (SpeedHz > 0.01)
            _rotary.SetSpeedHz(SpeedHz, SpeedHz * 0.85);
        else
            _rotary.SetSpeed(SpeedIndex >= 1 ? 1.0 : 0.0);
    }
}
