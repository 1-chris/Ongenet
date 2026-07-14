using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Test tone generator: a <see cref="WaveOscillator"/> writes into the buffer (replaces content)
/// at Frequency / LevelDb / Wave.
/// </summary>
public sealed class TestToneEffect : IAudioEffect
{
    public const string TypeId = "test_tone";

    string IAudioEffect.TypeId => TypeId;

    private static readonly string[] WaveNames = { "Sine", "Triangle", "Saw", "Square", "Noise" };

    public bool Enabled { get; set; } = true;

    public double Frequency { get; set; } = 440.0;
    public double LevelDb { get; set; } = -12.0;
    public int Wave { get; set; }

    private int _channels = 2;
    private readonly WaveOscillator _osc = new();

    public string Name => "Test Tone";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Frequency", 20.0, 20000.0, () => Frequency, v => Frequency = v, "0", "Hz", 3.0),
        new FloatParameter("Level", -60.0, 0.0, () => LevelDb, v => LevelDb = v, "0.#", "dB"),
        new ChoiceParameter("Wave", WaveNames, () => Wave, v => Wave = v)
    };

    public void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _osc.SetSampleRate(format.SampleRate > 0 ? format.SampleRate : 44100);
        _osc.ResetPhase();
    }

    public IAudioEffect Clone() => new TestToneEffect
    {
        Enabled = Enabled, Frequency = Frequency, LevelDb = LevelDb, Wave = Wave
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        _osc.Wave = (OscWave)Math.Clamp(Wave, 0, 4);
        _osc.SetFrequency(Math.Clamp(Frequency, 1.0, 20000.0));
        var level = (float)AudioMath.Db2Lin(LevelDb);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = _osc.Next() * level;
            var i = frame * channels;
            for (var c = 0; c < channels; c++) buffer[i + c] = sample;
        }
    }
}
