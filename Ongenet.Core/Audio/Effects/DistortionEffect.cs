using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A waveshaping distortion: input drive into a shaping curve (soft/hard/foldback), output level,
/// and a dry/wet mix. Stateless per sample.
/// </summary>
public sealed class DistortionEffect : IAudioEffect
{
    public const string TypeId = "distortion";

    private static readonly string[] ModeNames = { "Soft", "Hard", "Foldback" };

    public bool Enabled { get; set; } = true;

    public double DriveDb { get; set; } = 12.0;
    public double OutputDb { get; set; }
    public double Mix { get; set; } = 1.0;
    public int Mode { get; set; }

    public string Name => "Distortion";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Drive", 0.0, 48.0, () => DriveDb, v => DriveDb = v, "0.#", "dB"),
        new FloatParameter("Output", -24.0, 6.0, () => OutputDb, v => OutputDb = v, "0.#", "dB"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new ChoiceParameter("Mode", ModeNames, () => Mode, v => Mode = v)
    };

    public void Prepare(AudioFormat format) { }

    public IAudioEffect Clone() => new DistortionEffect
    {
        Enabled = Enabled, DriveDb = DriveDb, OutputDb = OutputDb, Mix = Mix, Mode = Mode
    };

    public void Process(Span<float> buffer)
    {
        var drive = (float)AudioMath.Db2Lin(DriveDb);
        var output = (float)AudioMath.Db2Lin(OutputDb);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var mode = Mode;

        for (var i = 0; i < buffer.Length; i++)
        {
            var dry = buffer[i];
            var shaped = Shape(dry * drive, mode);
            buffer[i] = (dry * (1 - mix) + shaped * mix) * output;
        }
    }

    private static float Shape(float x, int mode) => mode switch
    {
        1 => Math.Clamp(x, -1f, 1f),                 // hard clip
        2 => Foldback(x),                            // foldback
        _ => (float)Math.Tanh(x)                     // soft clip
    };

    private static float Foldback(float x)
    {
        // Reflect back into [-1, 1].
        for (var guard = 0; guard < 8 && (x > 1f || x < -1f); guard++)
        {
            if (x > 1f) x = 2f - x;
            else if (x < -1f) x = -2f - x;
        }

        return x;
    }
}
