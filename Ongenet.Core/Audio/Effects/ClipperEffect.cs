using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A transparent mastering soft clipper / saturator. Sitting just before the brickwall limiter, it
/// shaves the absolute microscopic peaks of the waveform cleanly (a smooth tanh curve that stays
/// near-linear for the bulk of the signal and asymptotes toward the ceiling), taming sharp transient
/// drums so the final limiter has far less work to do — letting the track sit louder without pumping.
/// Drive is gentle (1–2 dB); the ceiling keeps a small safety margin.
/// </summary>
public sealed class ClipperEffect : IAudioEffect
{
    public const string TypeId = "clipper";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    /// <summary>Input drive (dB) pushed into the soft-clip curve.</summary>
    public double DriveDb { get; set; } = 1.5;

    /// <summary>The soft ceiling (dBFS) the waveform asymptotes toward.</summary>
    public double CeilingDb { get; set; } = -0.3;

    public string Name => "Clipper";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Drive", 0.0, 12.0, () => DriveDb, v => DriveDb = v, "0.#", "dB"),
        new FloatParameter("Ceiling", -6.0, 0.0, () => CeilingDb, v => CeilingDb = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format) { }

    public IAudioEffect Clone() => new ClipperEffect
    {
        Enabled = Enabled, DriveDb = DriveDb, CeilingDb = CeilingDb
    };

    public void Process(Span<float> buffer)
    {
        var drive = (float)AudioMath.Db2Lin(DriveDb);
        var ceiling = (float)AudioMath.Db2Lin(CeilingDb);
        if (ceiling <= 1e-6f) ceiling = 1e-6f;

        for (var i = 0; i < buffer.Length; i++)
        {
            // tanh(z) ≈ z for small z (transparent) and → 1 for large z (peaks fold to the ceiling).
            var z = buffer[i] * drive / ceiling;
            buffer[i] = (float)Math.Tanh(z) * ceiling;
        }
    }
}
