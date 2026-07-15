using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Linear-phase mastering EQ: high-pass, four peaking bands, and a high shelf applied via a
/// symmetric FIR (zero-phase around the group delay). Reports 128 samples of latency for PDC.
/// </summary>
public sealed class LinearPhaseEqEffect : IAudioEffect, ILatencyProvider
{
    public const string TypeId = "linear_phase_eq";
    private const int FirHalf = 128; // total taps = 2*FirHalf+1 → latency FirHalf samples

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Linear-Phase EQ (128-sample latency)";
    public bool Enabled { get; set; } = true;

    public double LowFreq { get; set; } = 80;
    public double LowGainDb { get; set; }
    public double LowMidFreq { get; set; } = 400;
    public double LowMidGainDb { get; set; }
    public double HighMidFreq { get; set; } = 3000;
    public double HighMidGainDb { get; set; }
    public double HighFreq { get; set; } = 10000;
    public double HighGainDb { get; set; }
    public bool HighPassEnabled { get; set; }
    public double HighPassHz { get; set; } = 25;
    public double HighShelfFreq { get; set; } = 12000;
    public double HighShelfGainDb { get; set; }

    public int ReportedLatencySamples => FirHalf;

    private int _channels = 2;
    private double _sampleRate = 48000;
    private float[] _kernel = Array.Empty<float>();
    private float[][] _delay = Array.Empty<float[]>();
    private int _write;
    private double _lastSig = double.NaN;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new BoolParameter("High-Pass", () => HighPassEnabled, v => HighPassEnabled = v)
            { Group = "Filters", Description = "Linear-phase high-pass; this effect reports 128 samples of latency for compensation." },
        new FloatParameter("High-Pass Freq", 10, 200, () => HighPassHz, v => HighPassHz = v, "0", "Hz", 2.0)
            { Group = "Filters" },
        new FloatParameter("Low Freq", 30, 300, () => LowFreq, v => LowFreq = v, "0", "Hz", 2.0) { Group = "Bands" },
        new FloatParameter("Low Gain", -18, 18, () => LowGainDb, v => LowGainDb = v, "0.#", "dB") { Group = "Bands" },
        new FloatParameter("Low-Mid Freq", 200, 1200, () => LowMidFreq, v => LowMidFreq = v, "0", "Hz", 2.0) { Group = "Bands" },
        new FloatParameter("Low-Mid Gain", -18, 18, () => LowMidGainDb, v => LowMidGainDb = v, "0.#", "dB") { Group = "Bands" },
        new FloatParameter("High-Mid Freq", 1000, 8000, () => HighMidFreq, v => HighMidFreq = v, "0", "Hz", 2.0) { Group = "Bands" },
        new FloatParameter("High-Mid Gain", -18, 18, () => HighMidGainDb, v => HighMidGainDb = v, "0.#", "dB") { Group = "Bands" },
        new FloatParameter("High Freq", 4000, 18000, () => HighFreq, v => HighFreq = v, "0", "Hz", 2.0) { Group = "Bands" },
        new FloatParameter("High Gain", -18, 18, () => HighGainDb, v => HighGainDb = v, "0.#", "dB") { Group = "Bands" },
        new FloatParameter("High Shelf Freq", 3000, 18000, () => HighShelfFreq, v => HighShelfFreq = v, "0", "Hz", 2.0)
            { Group = "Shelves" },
        new FloatParameter("High Shelf Gain", -18, 18, () => HighShelfGainDb, v => HighShelfGainDb = v, "0.#", "dB")
            { Group = "Shelves", Description = "Linear-phase shelf; total effect latency remains 128 samples." }
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 48000;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var delay = new float[_channels][];
        for (var c = 0; c < _channels; c++)
            delay[c] = new float[FirHalf * 2 + 1];
        _delay = delay;
        _write = 0;
        RebuildKernel(force: true);
    }

    public IAudioEffect Clone() => new LinearPhaseEqEffect
    {
        Enabled = Enabled,
        LowFreq = LowFreq, LowGainDb = LowGainDb,
        LowMidFreq = LowMidFreq, LowMidGainDb = LowMidGainDb,
        HighMidFreq = HighMidFreq, HighMidGainDb = HighMidGainDb,
        HighFreq = HighFreq, HighGainDb = HighGainDb,
        HighPassEnabled = HighPassEnabled, HighPassHz = HighPassHz,
        HighShelfFreq = HighShelfFreq, HighShelfGainDb = HighShelfGainDb
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        RebuildKernel(force: false);
        var delay = _delay;
        var kernel = _kernel;
        var ch = Math.Min(_channels, delay.Length);
        if (ch <= 0 || kernel.Length == 0) return;
        var frames = buffer.Length / ch;
        var taps = kernel.Length;
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < ch; c++)
            {
                var idx = f * ch + c;
                delay[c][_write] = buffer[idx];
                double acc = 0;
                for (var t = 0; t < taps; t++)
                {
                    var di = _write - t;
                    if (di < 0) di += taps;
                    acc += delay[c][di] * kernel[t];
                }
                buffer[idx] = (float)acc;
            }
            _write++;
            if (_write >= taps) _write = 0;
        }
    }

    private void RebuildKernel(bool force)
    {
        var sig = LowFreq + LowGainDb * 10 + LowMidFreq + LowMidGainDb * 10
                  + HighMidFreq + HighMidGainDb * 10 + HighFreq + HighGainDb * 10
                  + (HighPassEnabled ? 1 : 0) + HighPassHz + HighShelfFreq + HighShelfGainDb * 10
                  + _sampleRate;
        if (!force && Math.Abs(sig - _lastSig) < 1e-9) return;
        _lastSig = sig;

        var taps = FirHalf * 2 + 1;
        var kernel = new float[taps];
        // Frequency sampling: evaluate desired linear-phase magnitude, IDFT via cosine (real even).
        const int bins = 256;
        var mag = new double[bins];
        for (var k = 0; k < bins; k++)
        {
            var hz = k * (_sampleRate * 0.5) / Math.Max(1, bins - 1);
            var g = 1.0;
            g *= BandGain(hz, LowFreq, LowGainDb, 0.7);
            g *= BandGain(hz, LowMidFreq, LowMidGainDb, 1.0);
            g *= BandGain(hz, HighMidFreq, HighMidGainDb, 1.0);
            g *= BandGain(hz, HighFreq, HighGainDb, 0.7);
            if (HighPassEnabled)
                g *= HighPassGain(hz, HighPassHz);
            g *= HighShelfGain(hz, HighShelfFreq, HighShelfGainDb);
            mag[k] = g;
        }

        for (var n = 0; n < taps; n++)
        {
            double acc = 0;
            var n0 = n - FirHalf;
            for (var k = 0; k < bins; k++)
            {
                var omega = Math.PI * k / Math.Max(1, bins - 1);
                acc += mag[k] * Math.Cos(omega * n0);
            }
            // Hann window to reduce ringing
            var w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (taps - 1));
            kernel[n] = (float)(acc / bins * w);
        }

        // Match either the desired DC response, or Nyquist when the high-pass intentionally removes DC.
        double response = 0;
        for (var i = 0; i < taps; i++)
            response += kernel[i] * (HighPassEnabled && (i & 1) != 0 ? -1.0 : 1.0);
        if (Math.Abs(response) > 1e-8)
        {
            var referenceHz = HighPassEnabled ? _sampleRate * 0.5 : 0.0;
            var wanted = BandGain(referenceHz, LowFreq, LowGainDb, 0.7)
                         * BandGain(referenceHz, LowMidFreq, LowMidGainDb, 1.0)
                         * BandGain(referenceHz, HighMidFreq, HighMidGainDb, 1.0)
                         * BandGain(referenceHz, HighFreq, HighGainDb, 0.7)
                         * (HighPassEnabled ? HighPassGain(referenceHz, HighPassHz) : 1.0)
                         * HighShelfGain(referenceHz, HighShelfFreq, HighShelfGainDb);
            var scale = wanted / response;
            for (var i = 0; i < taps; i++) kernel[i] = (float)(kernel[i] * scale);
        }
        _kernel = kernel;
    }

    private static double BandGain(double hz, double freq, double gainDb, double q)
    {
        if (Math.Abs(gainDb) < 0.001) return 1.0;
        var bw = freq / Math.Max(0.1, q);
        var x = (hz - freq) / bw;
        var w = Math.Exp(-0.5 * x * x);
        return Math.Pow(10.0, (gainDb * w) / 20.0);
    }

    private static double HighPassGain(double hz, double cutoff)
    {
        cutoff = Math.Max(1.0, cutoff);
        var ratio = hz / cutoff;
        return ratio / Math.Sqrt(1.0 + ratio * ratio);
    }

    private static double HighShelfGain(double hz, double freq, double gainDb)
    {
        if (Math.Abs(gainDb) < 0.001) return 1.0;
        freq = Math.Max(1.0, freq);
        var x = Math.Log(Math.Max(hz, 1.0) / freq) * 3.0;
        var shelf = 1.0 / (1.0 + Math.Exp(-x));
        return Math.Pow(10.0, gainDb * shelf / 20.0);
    }
}
