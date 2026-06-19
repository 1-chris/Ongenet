using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Monophonic fundamental-frequency detector using the YIN algorithm (difference function →
/// cumulative-mean-normalised difference → absolute threshold → parabolic interpolation). Feed it
/// mono samples with <see cref="Push"/>; call <see cref="Detect"/> to read the latest pitch in Hz
/// (0 when the input is unvoiced or below the clarity threshold). Reusable for auto-tune, a tuner
/// display, or any pitch-aware feature.
///
/// YIN is O(maxLag²); at the full sample rate that's a heavy spike for one audio callback (fine for
/// large ALSA buffers, but blows a small JACK buffer's real-time deadline → xruns). So the input is
/// anti-alias-filtered and decimated to a low working rate (~9 kHz) before analysis, which cuts the
/// cost ~25× while keeping ample resolution for 70 Hz–1 kHz pitch. <see cref="Push"/> still takes one
/// full-rate sample per call; decimation is internal.
/// </summary>
public sealed class PitchDetector
{
    private double _workingRate = 44100.0; // sample rate after decimation
    private int _decim = 1;                // decimation factor
    private int _decimCount;
    private Biquad _antiAlias;             // pre-decimation low-pass state
    private BiquadCoefficients _aaCoef = BiquadCoefficients.Identity;

    private int _minLag = 44;       // workingRate / maxHz
    private int _maxLag = 630;      // workingRate / minHz
    private float[] _buffer = Array.Empty<float>();
    private float[] _diff = Array.Empty<float>();
    private int _writeCount;        // decimated samples pushed (for fill detection)
    private int _write;

    /// <summary>Clarity threshold: detections with clarity below this are treated as unvoiced.</summary>
    public double ClarityThreshold { get; set; } = 0.5;

    /// <summary>YIN absolute threshold for picking the period dip (lower = stricter).</summary>
    public double YinThreshold { get; set; } = 0.15;

    public void Configure(double sampleRate, double minHz = 70.0, double maxHz = 1000.0)
    {
        var sr = sampleRate > 0 ? sampleRate : 44100.0;

        // Decimate to ~9 kHz so YIN stays cheap (well within a small JACK callback). Keep the working
        // rate comfortably above 2× maxHz so high pitches are still resolvable.
        _decim = Math.Max(1, (int)Math.Round(sr / 9000.0));
        _workingRate = sr / _decim;
        _decimCount = 0;

        // Anti-alias low-pass just under the decimated Nyquist, applied before downsampling.
        _antiAlias.Reset();
        _aaCoef = BiquadCoefficients.Compute(FilterMode.LowPass, _workingRate * 0.40, 0.707, sr);

        _maxLag = (int)Math.Ceiling(_workingRate / Math.Max(20.0, minHz));
        _minLag = (int)Math.Floor(_workingRate / Math.Max(minHz + 1.0, maxHz));
        if (_minLag < 2) _minLag = 2;

        // Window holds the integration block (W) plus the max lag. Use 3·maxLag so W = 2·maxLag,
        // i.e. ~2 periods of the lowest pitch in the integration window — enough for a stable f0
        // (a too-short window makes the detected pitch jitter, which stops auto-tune from pinning).
        var size = Math.Max(128, _maxLag * 3);
        _buffer = new float[size];
        _diff = new float[_maxLag + 1];
        _write = 0;
        _writeCount = 0;
    }

    /// <summary>Resets the analysis buffer.</summary>
    public void Reset()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _write = 0;
        _writeCount = 0;
        _decimCount = 0;
        _antiAlias.Reset();
    }

    /// <summary>Adds one mono (full-rate) sample; it is anti-alias filtered and decimated internally.</summary>
    public void Push(float sample)
    {
        if (_buffer.Length == 0) return;
        var filtered = (float)_antiAlias.Process(in _aaCoef, sample);
        if (++_decimCount < _decim) return; // keep only every _decim-th filtered sample
        _decimCount = 0;

        _buffer[_write] = filtered;
        if (++_write >= _buffer.Length) _write = 0;
        if (_writeCount < int.MaxValue) _writeCount++;
    }

    /// <summary>Runs YIN over the current window; returns f0 in Hz, or 0 if unvoiced/uncertain.</summary>
    public double Detect()
    {
        var n = _buffer.Length;
        if (n == 0 || _writeCount < n) return 0.0; // not enough audio yet

        // Linearise the ring buffer into chronological order (oldest first).
        // x[i] for i = 0..n-1, where the integration window W = n - maxLag.
        var w = n - _maxLag;
        if (w < _maxLag) return 0.0;

        // Difference function d[tau] = Σ_{j} (x[j] - x[j+tau])^2
        for (var tau = _minLag; tau <= _maxLag; tau++)
        {
            double sum = 0;
            for (var j = 0; j < w; j++)
            {
                var a = Sample(j);
                var b = Sample(j + tau);
                var delta = a - b;
                sum += delta * delta;
            }

            _diff[tau] = (float)sum;
        }

        // Cumulative mean normalised difference: d'[tau] = d[tau] / ((1/tau) Σ_{k=1..tau} d[k]).
        double running = 0;
        var bestTau = -1;
        float bestVal = float.MaxValue;
        var prevCmnd = 1.0;        // d'[minLag-1] approximation for local-min test
        for (var tau = _minLag; tau <= _maxLag; tau++)
        {
            running += _diff[tau];
            var cmnd = running > 1e-12 ? _diff[tau] * tau / running : 1.0;

            // Absolute threshold: first local minimum that dips below YinThreshold.
            if (cmnd < YinThreshold && cmnd <= prevCmnd)
            {
                // Walk to the bottom of this dip.
                var t = tau;
                while (t + 1 <= _maxLag)
                {
                    var nextRunning = running + _diff[t + 1];
                    var nextCmnd = nextRunning > 1e-12 ? _diff[t + 1] * (t + 1) / nextRunning : 1.0;
                    if (nextCmnd < cmnd) { cmnd = nextCmnd; running = nextRunning; t++; }
                    else break;
                }

                bestTau = t;
                bestVal = (float)cmnd;
                break;
            }

            if (cmnd < bestVal) { bestVal = (float)cmnd; bestTau = tau; }
            prevCmnd = cmnd;
        }

        if (bestTau < _minLag) return 0.0;

        var clarity = 1.0 - bestVal;
        if (clarity < ClarityThreshold) return 0.0;

        var period = ParabolicInterp(bestTau);
        return period > 0 ? _workingRate / period : 0.0;
    }

    // Reads chronological sample i (0 = oldest in the window).
    private float Sample(int i)
    {
        var idx = _write + i; // _write points at the oldest slot (next to be overwritten)
        if (idx >= _buffer.Length) idx -= _buffer.Length;
        return _buffer[idx];
    }

    // Refines the integer lag with a parabolic fit over d'[tau-1..tau+1] (uses raw diff as proxy).
    private double ParabolicInterp(int tau)
    {
        if (tau <= _minLag || tau >= _maxLag) return tau;
        double s0 = _diff[tau - 1], s1 = _diff[tau], s2 = _diff[tau + 1];
        var denom = 2.0 * (2.0 * s1 - s0 - s2);
        if (Math.Abs(denom) < 1e-12) return tau;
        return tau + (s2 - s0) / denom;
    }
}
