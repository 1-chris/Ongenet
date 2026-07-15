using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Spectral match EQ: captures a target magnitude spectrum and applies multi-band peaking EQ
/// toward it when <see cref="Blend"/> &gt; 0. Uses log-spaced analysis bins mapped onto 12 peaking
/// bands for better mid-frequency resolution than a flat FFT partition.
/// </summary>
public sealed class MatchEqEffect : IAudioEffect
{
    public const string TypeId = "match_eq";
    public const int TargetBandCount = 48;
    public const int EqBandCount = 12;

    private static readonly double[] EqFrequencies =
    {
        40, 80, 150, 250, 400, 650, 1000, 1600, 2500, 4000, 6500, 10000
    };

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Match EQ";
    public bool Enabled { get; set; } = true;

    public double Blend { get; set; }
    public double Smoothness { get; set; } = 0.5;
    public bool HasTarget => _hasTarget;
    public bool CaptureArmed
    {
        get => _captureArmed;
        set
        {
            if (_captureArmed == value) return;
            if (value) BeginStreamingCapture();
            else EndStreamingCapture();
        }
    }

    private readonly float[] _targetMagDb = new float[TargetBandCount];
    private readonly float[] _bandTargetDb = new float[EqBandCount];
    private readonly float[] _smoothedGainDb = new float[EqBandCount];
    private readonly float[] _captureSpectrumDb = new float[TargetBandCount];
    private readonly double[] _captureSumDb = new double[TargetBandCount];
    private readonly EqBand[] _bands = new EqBand[EqBandCount];
    private bool _hasTarget;
    private bool _captureArmed;
    private int _captureBlocks;
    private int _channels = 2;
    private double _sampleRate = 44100.0;

    public MatchEqEffect()
    {
        for (var i = 0; i < EqBandCount; i++)
            _bands[i] = new EqBand(EqBandType.Bell, EqFrequencies[i], 0, 1.1);
    }

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Blend", 0.0, 1.0, () => Blend, v => Blend = v, "0.##"),
        new FloatParameter("Smoothness", 0.0, 1.0, () => Smoothness, v => Smoothness = v, "0.##"),
        new BoolParameter("Capture Armed", () => CaptureArmed, v => CaptureArmed = v)
            { Group = "Capture", Description = "Capture a new live spectral target while enabled; toggle off to finish." }
    };

    public void SetTargetSpectrum(ReadOnlySpan<float> magDb)
    {
        var n = Math.Min(magDb.Length, TargetBandCount);
        for (var i = 0; i < n; i++) _targetMagDb[i] = magDb[i];
        for (var i = n; i < TargetBandCount; i++) _targetMagDb[i] = 0f;
        MapTargetToEqBands();
        _hasTarget = true;
    }

    public void CopyTargetSpectrum(Span<float> dest)
    {
        var n = Math.Min(dest.Length, TargetBandCount);
        for (var i = 0; i < n; i++) dest[i] = _targetMagDb[i];
    }

    public void CopyBandGainsDb(Span<float> dest)
    {
        var n = Math.Min(dest.Length, EqBandCount);
        for (var i = 0; i < n; i++) dest[i] = _smoothedGainDb[i];
    }

    public static ReadOnlySpan<double> BandFrequencies => EqFrequencies;

    public void CaptureTargetFrom(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        if (channels < 1) channels = 1;
        var frames = interleaved.Length / channels;
        if (frames < 64) return;

        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var i = f * channels;
            float sum = 0;
            for (var c = 0; c < channels; c++) sum += interleaved[i + c];
            mono[f] = sum / channels;
        }

        AnalyzeMagnitudeSpectrum(mono, sampleRate, _targetMagDb);
        MapTargetToEqBands();
        _hasTarget = true;
    }

    public void BeginStreamingCapture()
    {
        Array.Clear(_captureSumDb);
        _captureBlocks = 0;
        _captureArmed = true;
        _hasTarget = false;
    }

    public void UpdateStreamingCapture(ReadOnlySpan<float> interleaved)
    {
        if (!_captureArmed) return;
        var channels = Math.Max(1, _channels);
        var frames = interleaved.Length / channels;
        if (frames < 64) return;

        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
                sum += interleaved[f * channels + c];
            mono[f] = sum / channels;
        }
        AnalyzeMagnitudeSpectrum(mono, (int)_sampleRate, _captureSpectrumDb);
        for (var i = 0; i < TargetBandCount; i++)
        {
            _captureSumDb[i] += _captureSpectrumDb[i];
            _targetMagDb[i] = (float)(_captureSumDb[i] / (_captureBlocks + 1));
        }
        _captureBlocks++;
        MapTargetToEqBands();
        _hasTarget = true;
    }

    public void EndStreamingCapture()
    {
        _captureArmed = false;
        if (_captureBlocks <= 0) return;
        MapTargetToEqBands();
        _hasTarget = true;
    }

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        for (var i = 0; i < EqBandCount; i++)
            _bands[i].Prepare(_channels);
    }

    public IAudioEffect Clone()
    {
        var fx = new MatchEqEffect { Enabled = Enabled, Blend = Blend, Smoothness = Smoothness };
        if (_hasTarget) fx.SetTargetSpectrum(_targetMagDb);
        for (var i = 0; i < EqBandCount; i++)
            fx._smoothedGainDb[i] = _smoothedGainDb[i];
        return fx;
    }

    public void Process(Span<float> buffer)
    {
        if (_captureArmed)
            UpdateStreamingCapture(buffer);
        if (!_hasTarget || Blend <= 1e-6) return;

        var blend = (float)Math.Clamp(Blend, 0, 1);
        var smooth = 0.01f + (float)Math.Clamp(Smoothness, 0, 1) * 0.25f;

        for (var b = 0; b < EqBandCount; b++)
        {
            var target = _bandTargetDb[b] * blend;
            _smoothedGainDb[b] += (target - _smoothedGainDb[b]) * smooth;
            _bands[b].Frequency = EqFrequencies[b];
            _bands[b].GainDb = Math.Clamp(_smoothedGainDb[b], -18, 18);
            _bands[b].EnsureCoeffs(_sampleRate);
        }

        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        for (var f = 0; f < frames; f++)
        {
            var i = f * ch;
            for (var c = 0; c < ch; c++)
            {
                var s = buffer[i + c];
                for (var b = 0; b < EqBandCount; b++)
                    s = _bands[b].Process(c, s);
                buffer[i + c] = s;
            }
        }
    }

    private void MapTargetToEqBands()
    {
        // Map log-spaced FFT magnitude bands onto EQ centres by weighted proximity.
        for (var b = 0; b < EqBandCount; b++)
        {
            var eqHz = EqFrequencies[b];
            double sum = 0, wSum = 0;
            for (var i = 0; i < TargetBandCount; i++)
            {
                var t = i / (double)(TargetBandCount - 1);
                var hz = 30.0 * Math.Pow(20000.0 / 30.0, t);
                var dist = Math.Abs(Math.Log2(hz / eqHz));
                var w = Math.Exp(-dist * dist * 4.0);
                sum += _targetMagDb[i] * w;
                wSum += w;
            }
            _bandTargetDb[b] = (float)(sum / Math.Max(wSum, 1e-9));
        }

        float mean = 0;
        for (var i = 0; i < EqBandCount; i++) mean += _bandTargetDb[i];
        mean /= EqBandCount;
        for (var i = 0; i < EqBandCount; i++)
            _bandTargetDb[i] = Math.Clamp(_bandTargetDb[i] - mean, -12f, 12f);
    }

    private static void AnalyzeMagnitudeSpectrum(ReadOnlySpan<float> mono, int sampleRate, Span<float> dest)
    {
        const int fftSize = 2048;
        var n = Math.Min(mono.Length, fftSize);
        var re = new float[fftSize];
        var im = new float[fftSize];
        for (var i = 0; i < n; i++)
            re[i] = mono[i] * (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Math.Max(1, fftSize - 1)));

        FftInPlace(re, im);
        var nyquist = sampleRate * 0.5;
        for (var b = 0; b < dest.Length; b++)
        {
            var t0 = b / (double)dest.Length;
            var t1 = (b + 1) / (double)dest.Length;
            var hz0 = 30.0 * Math.Pow(Math.Min(nyquist, 20000.0) / 30.0, t0);
            var hz1 = 30.0 * Math.Pow(Math.Min(nyquist, 20000.0) / 30.0, t1);
            var bin0 = Math.Max(1, (int)(hz0 / sampleRate * fftSize));
            var bin1 = Math.Min(fftSize / 2 - 1, Math.Max(bin0 + 1, (int)(hz1 / sampleRate * fftSize)));
            double sum = 0;
            for (var i = bin0; i < bin1; i++)
            {
                var m = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
                sum += m;
            }
            var avg = sum / Math.Max(1, bin1 - bin0);
            dest[b] = (float)(20.0 * Math.Log10(Math.Max(avg, 1e-9)));
        }
    }

    private static void FftInPlace(float[] re, float[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; j >= bit; bit >>= 1) j -= bit;
            j += bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var wlenRe = (float)Math.Cos(ang);
            var wlenIm = (float)Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                float wRe = 1, wIm = 0;
                for (var j = 0; j < len / 2; j++)
                {
                    var uRe = re[i + j];
                    var uIm = im[i + j];
                    var vRe = re[i + j + len / 2] * wRe - im[i + j + len / 2] * wIm;
                    var vIm = re[i + j + len / 2] * wIm + im[i + j + len / 2] * wRe;
                    re[i + j] = uRe + vRe;
                    im[i + j] = uIm + vIm;
                    re[i + j + len / 2] = uRe - vRe;
                    im[i + j + len / 2] = uIm - vIm;
                    var nextWRe = wRe * wlenRe - wIm * wlenIm;
                    wIm = wRe * wlenIm + wIm * wlenRe;
                    wRe = nextWRe;
                }
            }
        }
    }
}
