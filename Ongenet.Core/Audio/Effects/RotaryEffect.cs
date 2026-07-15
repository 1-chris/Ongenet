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
    private RotarySpeaker _rotary = new();

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
        var sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var rotary = new RotarySpeaker();
        rotary.Configure((int)sampleRate);
        rotary.Reset();
        ApplySpeed(rotary);

        // Publish a fully-configured instance — RebuildTracks can call Prepare from the UI thread
        // while Process runs on the audio worker pool (e.g. after "Render clip to new track").
        _sampleRate = sampleRate;
        _channels = channels;
        _rotary = rotary;
    }

    public IAudioEffect Clone() => new RotaryEffect
    {
        Enabled = Enabled, SpeedIndex = SpeedIndex, SpeedHz = SpeedHz, DriveDb = DriveDb, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var rotary = _rotary;
        rotary.SetDrive(DriveDb);
        rotary.Mix = (float)Math.Clamp(Mix, 0, 1);
        ApplySpeed(rotary);

        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            if (channels >= 2)
            {
                rotary.Process(buffer[i], buffer[i + 1], out var outL, out var outR);
                buffer[i] = outL;
                buffer[i + 1] = outR;
            }
            else
            {
                rotary.Process(buffer[i], buffer[i], out var outL, out _);
                buffer[i] = outL;
            }
        }
    }

    private void ApplySpeed(RotarySpeaker rotary)
    {
        if (SpeedHz > 0.01)
            rotary.SetSpeedHz(SpeedHz, SpeedHz * 0.85);
        else
            rotary.SetSpeed(SpeedIndex >= 1 ? 1.0 : 0.0);
    }
}
