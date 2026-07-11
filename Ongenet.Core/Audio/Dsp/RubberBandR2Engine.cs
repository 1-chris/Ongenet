using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Pure .NET port of Rubber Band R2 offline pitch shift (unity tempo): study pass, adaptive
/// chunk increments, laminar <c>modifyChunk</c>, Hann overlap-add, Hermite resample.
/// </summary>
internal sealed class RubberBandR2Engine
{
    private readonly RubberBandR2Config _config;
    private readonly RubberBandWindow _analysisWindow;
    private readonly RubberBandWindow _synthesisWindow;
    private readonly int _binCount;
    private readonly int _halfSize;

    private readonly double[] _re;
    private readonly double[] _im;
    private readonly double[] _mag;
    private readonly double[] _phase;
    private readonly double[] _prevPhase;
    private readonly double[] _prevError;
    private readonly double[] _unwrappedPhase;
    private readonly float[] _grain;
    private readonly float[] _accumulator;
    private readonly float[] _windowAccumulator;

    private int _prevShiftIncrement;
    private int _accumulatorFill;
    private bool _unchanged = true;
    private double _unityResetLow = 16000.0;

    private IReadOnlyList<int> _outputIncrements = Array.Empty<int>();
    private float[] _prevStudyMag = Array.Empty<float>();

    public RubberBandR2Engine(RubberBandR2Config config)
    {
        _config = config;
        _analysisWindow = new RubberBandWindow(config.AnalysisWindowSize);
        _synthesisWindow = new RubberBandWindow(config.SynthesisWindowSize);
        _binCount = config.FftSize / 2 + 1;
        _halfSize = config.FftSize / 2;

        _re = new double[config.FftSize];
        _im = new double[config.FftSize];
        _mag = new double[_binCount];
        _phase = new double[_binCount];
        _prevPhase = new double[_binCount];
        _prevError = new double[_binCount];
        _unwrappedPhase = new double[_binCount];
        _grain = new float[config.SynthesisWindowSize];
        var accSize = config.SynthesisWindowSize * 2;
        _accumulator = new float[accSize];
        _windowAccumulator = new float[accSize];
    }

    public float[] PitchShift(float[] input, IProgress<double>? progress = null,
        int channelIndex = 0, int channelCount = 1)
    {
        if (input.Length == 0 || Math.Abs(_config.PitchScale - 1.0) < 1e-9)
            return input;

        var pad = _config.AnalysisWindowSize / 2;
        var padded = new float[input.Length + pad];
        Array.Copy(input, 0, padded, pad, input.Length);

        var flux = Study(padded);
        var calculator = new RubberBandR2StretchCalculator(_config.SampleRate, _config.InputIncrement);
        _outputIncrements = calculator.BuildIncrements(_config.PitchScale, _config.InputIncrement,
            _config.AnalysisWindowSize, _config.SynthesisWindowSize, flux);

        var stretched = ProcessPadded(padded, progress, channelIndex, channelCount);
        return ResampleStretched(stretched, input.Length, _config.PitchScale);
    }

    private static float[] ResampleStretched(float[] stretched, int targetLength, double pitchScale)
    {
        if (targetLength <= 0) return Array.Empty<float>();
        if (stretched.Length <= 0) return new float[targetLength];
        if (targetLength == 1) return [SampleAt(stretched, 0)];

        var output = new float[targetLength];
        var maxSrc = stretched.Length - 1;
        for (var f = 0; f < targetLength; f++)
        {
            var srcPos = f * pitchScale;
            if (srcPos >= maxSrc)
            {
                output[f] = stretched[maxSrc];
                continue;
            }

            var i = (int)Math.Floor(srcPos);
            var t = (float)(srcPos - i);
            var ym1 = i > 0 ? stretched[i - 1] : stretched[i];
            var y0 = stretched[i];
            var y1 = stretched[i + 1];
            var y2 = i + 2 <= maxSrc ? stretched[i + 2] : stretched[i + 1];
            output[f] = HermiteInterpolator.Sample(ym1, y0, y1, y2, t);
        }

        return output;
    }

    private static float SampleAt(float[] input, double pos)
    {
        if (input.Length <= 0) return 0;
        if (pos <= 0) return input[0];
        var maxSrc = input.Length - 1;
        if (pos >= maxSrc) return input[maxSrc];
        var i = (int)Math.Floor(pos);
        var t = (float)(pos - i);
        var ym1 = i > 0 ? input[i - 1] : input[i];
        var y0 = input[i];
        var y1 = input[i + 1];
        var y2 = i + 2 <= maxSrc ? input[i + 2] : input[i + 1];
        return HermiteInterpolator.Sample(ym1, y0, y1, y2, t);
    }

    private List<float> Study(float[] padded)
    {
        var flux = new List<float>();
        _prevStudyMag = new float[_binCount];
        var window = new float[_config.AnalysisWindowSize];
        var magBuf = new float[_binCount];

        var pos = 0;
        while (pos + _config.AnalysisWindowSize <= padded.Length)
        {
            Array.Copy(padded, pos, window, 0, _config.AnalysisWindowSize);
            _analysisWindow.Cut(window);
            ForwardMagnitude(window, magBuf);

            var df = SpectralFlux(magBuf);
            flux.Add(df);

            pos += _config.InputIncrement;
        }

        return flux;
    }

    private float[] ProcessPadded(float[] padded, IProgress<double>? progress, int channelIndex,
        int channelCount)
    {
        var output = new List<float>((int)Math.Ceiling(padded.Length * _config.EffectiveRatio) + _config.FftSize);
        var chunk = 0;
        var pos = 0;
        var draining = false;

        Array.Clear(_accumulator);
        Array.Clear(_windowAccumulator);
        _accumulatorFill = 0;
        _prevShiftIncrement = 0;
        _unchanged = true;

        while (true)
        {
            var hasInput = pos + _config.AnalysisWindowSize <= padded.Length;
            if (!hasInput) draining = true;

            if (hasInput)
            {
                AnalyseChunk(padded, pos);
                pos += _config.InputIncrement;
            }

            GetIncrements(chunk, out var phaseIncrement, out var shiftIncrement, out var phaseReset);

            if (chunk >= _outputIncrements.Count && draining && _accumulatorFill == 0)
                break;

            if (hasInput)
            {
                ModifyChunk(phaseIncrement, phaseReset);
                SynthesiseChunk(shiftIncrement);
            }
            else if (_accumulatorFill == 0)
            {
                break;
            }

            if (draining)
            {
                if (shiftIncrement <= 0) shiftIncrement = _config.MeanOutputIncrement;
                if (_accumulatorFill <= shiftIncrement)
                    shiftIncrement = Math.Max(1, _accumulatorFill);
            }

            if (shiftIncrement > 0 && _accumulatorFill > 0)
                WriteChunk(output, shiftIncrement);

            chunk++;

            if (progress is not null && chunk % 8 == 0)
            {
                var t = Math.Min(0.99, (double)pos / padded.Length);
                progress.Report(t / channelCount + (double)channelIndex / channelCount);
            }

            if (draining && _accumulatorFill == 0)
                break;
        }

        progress?.Report(1.0);
        return output.ToArray();
    }

    private void AnalyseChunk(float[] input, int pos)
    {
        var window = new float[_config.AnalysisWindowSize];
        var count = Math.Min(_config.AnalysisWindowSize, input.Length - pos);
        Array.Copy(input, pos, window, 0, count);
        CutShiftAndFold(window);
        ForwardPolar(_re, _im, _mag, _phase);
    }

    private void CutShiftAndFold(float[] src)
    {
        _analysisWindow.Cut(src);
        var fftSize = _config.FftSize;
        var hs = _halfSize;
        Array.Clear(_re);
        Array.Clear(_im);
        for (var i = 0; i < hs; i++)
            _re[i] = src[i + hs];
        for (var i = 0; i < hs; i++)
            _re[i + hs] = src[i];
    }

    private void ForwardPolar(double[] re, double[] im, double[] mag, double[] phase)
    {
        Fft.Forward(re, im);
        for (var k = 0; k < _binCount; k++)
        {
            mag[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
            phase[k] = Math.Atan2(im[k], re[k]);
        }
    }

    private void ForwardMagnitude(float[] windowed, float[] magOut)
    {
        Array.Clear(_re);
        Array.Clear(_im);
        var n = Math.Min(windowed.Length, _config.FftSize);
        Array.Copy(windowed, _re, n);
        Fft.Forward(_re, _im);
        for (var k = 0; k < _binCount; k++)
            magOut[k] = (float)Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
    }

    private float SpectralFlux(float[] mag)
    {
        double flux = 0;
        for (var k = 0; k < _binCount; k++)
        {
            var d = mag[k] - _prevStudyMag[k];
            if (d > 0) flux += d;
            _prevStudyMag[k] = mag[k];
        }

        return (float)(flux / Math.Max(1, _binCount));
    }

    private void GetIncrements(int chunk, out int phaseIncrement, out int shiftIncrement, out bool phaseReset)
    {
        phaseReset = false;
        if (_outputIncrements.Count == 0)
        {
            phaseIncrement = _config.InputIncrement;
            shiftIncrement = _config.MeanOutputIncrement;
            if (chunk == 0) phaseReset = true;
            return;
        }

        var idx = Math.Min(chunk, _outputIncrements.Count - 1);
        var phaseInc = _outputIncrements[idx];
        var shiftInc = phaseInc;
        if (idx + 1 < _outputIncrements.Count)
            shiftInc = _outputIncrements[idx + 1];

        if (phaseInc < 0)
        {
            phaseInc = -phaseInc;
            phaseReset = true;
        }

        if (shiftInc < 0) shiftInc = -shiftInc;

        if (_prevShiftIncrement == 0)
            phaseIncrement = shiftInc;
        else
            phaseIncrement = _prevShiftIncrement;

        shiftIncrement = shiftInc;
        _prevShiftIncrement = shiftInc;

        if (chunk == 0) phaseReset = true;
    }

    private void ModifyChunk(int outputIncrement, bool phaseReset)
    {
        var fftSize = _config.FftSize;
        var increment = _config.InputIncrement;
        var count = fftSize / 2;
        var rate = _config.SampleRate;
        var r = _config.EffectiveRatio;

        var bandlimited = true;
        var bandlow = (int)Math.Round(150.0 * fftSize / rate);
        var bandhigh = (int)Math.Round(1000.0 * fftSize / rate);

        var freq0 = 600.0;
        var freq1 = 1200.0;
        var freq2 = 12000.0;
        if (r > 1.0)
        {
            var rf0 = 600 + 600 * Math.Pow(r - 1, 3) * 2;
            var f1ratio = freq1 / freq0;
            var f2ratio = freq2 / freq0;
            freq0 = Math.Max(freq0, rf0);
            freq1 = freq0 * f1ratio;
            freq2 = freq0 * f2ratio;
        }

        var limit0 = (int)Math.Round(freq0 * fftSize / rate);
        var limit1 = (int)Math.Round(freq1 * fftSize / rate);
        var limit2 = (int)Math.Round(freq2 * fftSize / rate);
        limit1 = Math.Max(limit1, limit0);
        limit2 = Math.Max(limit2, limit1);

        var unchanged = _unchanged && outputIncrement == increment;
        var fullReset = phaseReset;

        if (Math.Abs(r - 1.0) < 1e-6 && !phaseReset)
        {
            phaseReset = true;
            bandlimited = true;
            bandlow = (int)Math.Round(_unityResetLow * fftSize / rate);
            bandhigh = count;
        }
        else if (Math.Abs(r - 1.0) >= 1e-6)
        {
            _unityResetLow = 16000.0;
        }

        if (Math.Abs(r - 1.0) < 1e-6)
            _unityResetLow *= 0.9;

        const double maxdist = 8.0;
        const int lookback = 1;
        var distance = 0.0;
        var prevInstability = 0.0;
        var prevDirection = false;

        for (var i = count; i >= 0; i -= lookback)
        {
            var resetThis = phaseReset;
            if (bandlimited && resetThis && i > bandlow && i < bandhigh)
            {
                resetThis = false;
                fullReset = false;
            }

            var p = _phase[i];
            var perr = 0.0;
            var outphase = p;

            var mi = maxdist;
            if (i <= limit0) mi = 0.0;
            else if (i <= limit1) mi = 1.0;
            else if (i <= limit2) mi = 3.0;

            if (!resetThis)
            {
                var omega = 2.0 * Math.PI * increment * i / fftSize;
                var ep = _prevPhase[i] + omega;
                perr = PrincArg(p - ep);

                var instability = Math.Abs(perr - _prevError[i]);
                var direction = perr > _prevError[i];

                var inherit = false;
                if (distance < mi && i != count && i != bandhigh && i != bandlow &&
                    instability > prevInstability && direction == prevDirection)
                    inherit = true;

                var advance = outputIncrement * ((omega + perr) / increment);
                if (inherit)
                {
                    var inherited = _unwrappedPhase[i + lookback] - _prevPhase[i + lookback];
                    advance = (advance * distance + inherited * (maxdist - distance)) / maxdist;
                    outphase = p + advance;
                    distance += 1.0;
                }
                else
                {
                    outphase = _unwrappedPhase[i] + advance;
                    distance = 0.0;
                }

                prevInstability = instability;
                prevDirection = direction;
            }
            else
            {
                distance = 0.0;
            }

            _prevError[i] = perr;
            _prevPhase[i] = p;
            _phase[i] = outphase;
            _unwrappedPhase[i] = outphase;
        }

        if (fullReset) unchanged = true;
        _unchanged = unchanged;
    }

    private void SynthesiseChunk(int shiftIncrement)
    {
        for (var k = 0; k < _binCount; k++)
        {
            _re[k] = _mag[k] * Math.Cos(_phase[k]);
            _im[k] = _mag[k] * Math.Sin(_phase[k]);
        }

        for (var k = 1; k < _halfSize; k++)
        {
            _re[_config.FftSize - k] = _re[k];
            _im[_config.FftSize - k] = -_im[k];
        }

        Fft.Inverse(_re, _im);

        var hs = _halfSize;
        for (var i = 0; i < hs; i++)
            _grain[i] = (float)_re[i + hs];
        for (var i = 0; i < hs; i++)
            _grain[hs + i] = (float)_re[i];

        _synthesisWindow.Cut(_grain);

        var wsz = _config.SynthesisWindowSize;
        for (var i = 0; i < wsz; i++)
            _accumulator[i] += _grain[i];
        _accumulatorFill = Math.Max(_accumulatorFill, wsz);

        _synthesisWindow.AddToAccumulator(_windowAccumulator, _analysisWindow.Area * 1.5f);
        _unchanged = false;
    }

    private void WriteChunk(List<float> output, int shiftIncrement)
    {
        var si = Math.Min(shiftIncrement, _accumulatorFill);
        for (var i = 0; i < si; i++)
        {
            if (_windowAccumulator[i] > 1e-10f)
                _accumulator[i] /= _windowAccumulator[i];
        }

        for (var i = 0; i < si; i++)
            output.Add(_accumulator[i]);

        var remain = _accumulatorFill - si;
        if (remain > 0)
        {
            Array.Copy(_accumulator, si, _accumulator, 0, remain);
            Array.Copy(_windowAccumulator, si, _windowAccumulator, 0, remain);
        }

        Array.Clear(_accumulator, remain, _accumulator.Length - remain);
        Array.Clear(_windowAccumulator, remain, _windowAccumulator.Length - remain);
        _accumulatorFill = remain;
    }

    private static double PrincArg(double phase)
    {
        while (phase > Math.PI) phase -= 2.0 * Math.PI;
        while (phase < -Math.PI) phase += 2.0 * Math.PI;
        return phase;
    }
}
