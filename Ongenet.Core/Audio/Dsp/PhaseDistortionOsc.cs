using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A Casio-CZ–style phase-distortion oscillator. A linear phase ramp is warped through a two-segment
/// "kink" (the read pointer races through the first part of a cosine cycle, then crawls through the rest)
/// before a cosine lookup. At zero distortion the warp is the identity and the output is a pure cosine;
/// as it increases the waveform sharpens toward a resonant, saw/pulse-like tone with a formant that
/// sweeps up — the signature CZ timbre. Single phase accumulator, allocation- and table-free
/// <see cref="Process"/>. Hold one per voice. Reusable.
/// </summary>
public sealed class PhaseDistortionOsc
{
    private double _phase; // linear phase [0,1)

    public void Reset(double phase = 0.0) => _phase = phase - Math.Floor(phase);

    /// <summary>
    /// Produces the next sample in [-1, 1] and advances the phase. <paramref name="distortAmount"/> is
    /// 0 (pure cosine) → 1 (maximum brightening).
    /// </summary>
    public float Process(double freqHz, double distortAmount, int sampleRate)
    {
        var inc = sampleRate > 0 ? Math.Max(0.0, freqHz) / sampleRate : 0.0;
        var p = _phase;

        _phase += inc;
        if (_phase >= 1.0) _phase -= 1.0;

        // Move the segment break from the centre (0.5, no distortion) toward the start, which compresses
        // the first half-cycle and stretches the second — the classic PD phase warp.
        var d = AudioMath.Clamp(distortAmount, 0.0, 1.0);
        var brk = 0.5 - 0.5 * d * 0.98; // keep it just off 0 so the slope stays finite

        double warped;
        if (p < brk)
            warped = p * (0.5 / brk);
        else
            warped = 0.5 + (p - brk) * (0.5 / (1.0 - brk));

        // -cos so the wave starts at -1 and the distortion reads as a rising formant.
        return (float)-Math.Cos(2.0 * Math.PI * warped);
    }
}
