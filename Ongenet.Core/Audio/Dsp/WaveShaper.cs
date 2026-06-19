using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>The shaping curves available to <see cref="WaveShaper"/>.</summary>
public enum ShaperType
{
    /// <summary>Smooth analog-style saturation (tanh).</summary>
    Tanh,

    /// <summary>Brickwall clip to ±1.</summary>
    HardClip,

    /// <summary>Wavefolder: reflects out-of-range values back into ±1, adding harmonics.</summary>
    Foldback,

    /// <summary>Sine wavefolder for aggressive, metallic distortion (hardstyle).</summary>
    SineFold
}

/// <summary>
/// Stateless waveshaping with built-in makeup compensation: as <c>drive</c> rises, a unit-amplitude
/// input stays roughly unity at the output, so increasing drive changes timbre without blowing up the
/// level (essential for level-matched presets and a stable preview). Reusable by any instrument/effect.
/// </summary>
public static class WaveShaper
{
    /// <summary>
    /// Shapes <paramref name="x"/> through <paramref name="type"/> with a linear <paramref name="drive"/>
    /// (1 = unity). Tanh/HardClip are normalised so a full-scale input stays near ±1; Foldback/SineFold
    /// are inherently bounded to ±1. <paramref name="bias"/> (≈[-0.5, 0.5]) is added before shaping to
    /// break the curve's odd symmetry and introduce even harmonics (a richer, "fatter" distortion);
    /// it adds DC, so callers that stack stages must DC-block downstream. Stays stateless.
    /// </summary>
    public static float Shape(float x, ShaperType type, float drive, float bias = 0f)
    {
        if (drive < 1e-6f) drive = 1e-6f;
        x += bias;

        switch (type)
        {
            case ShaperType.HardClip:
            {
                var comp = Math.Min(1f, drive); // restore unity when drive < 1, keep clipped above
                return Math.Clamp(x * drive, -1f, 1f) / comp;
            }

            case ShaperType.Foldback:
                return Foldback(x * drive);

            case ShaperType.SineFold:
                return (float)Math.Sin(x * drive);

            default: // Tanh
            {
                var comp = (float)Math.Tanh(drive); // normalise so x=±1 maps near ±1
                return (float)Math.Tanh(x * drive) / comp;
            }
        }
    }

    private static float Foldback(float x)
    {
        // Reflect back into [-1, 1] (guarded against extreme inputs).
        for (var guard = 0; guard < 8 && (x > 1f || x < -1f); guard++)
        {
            if (x > 1f) x = 2f - x;
            else if (x < -1f) x = -2f - x;
        }

        return x;
    }
}
