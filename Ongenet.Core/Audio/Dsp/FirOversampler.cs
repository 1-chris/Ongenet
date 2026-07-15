using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// FIR 2×/4× oversampler: zero-insert upsample + halfband low-pass, or low-pass + decimate.
/// 4× is two cascaded 2× stages. Allocation-free after <see cref="Prepare"/>.
/// Half-band kernels have structural zeros on odd taps — those multiplies are skipped.
/// </summary>
public sealed class FirOversampler
{
    // Symmetric halfband-ish interpolator for 2× (odd taps are structural zeros except none).
    private static readonly float[] Kernel2 =
    {
        -0.00184609f, 0.0f, 0.01589891f, 0.0f, -0.06850512f, 0.0f, 0.30448279f, 0.5f,
        0.30448279f, 0.0f, -0.06850512f, 0.0f, 0.01589891f, 0.0f, -0.00184609f
    };

    private int _factor = 1;
    private Stage? _stageA;
    private Stage? _stageB; // second 2× for 4×
    private float[] _scratch = Array.Empty<float>();
    private float[] _scratch2 = Array.Empty<float>();

    public int Factor => _factor;

    public void Prepare(int factor, int maxBaseSamples = 4096)
    {
        _factor = factor is 2 or 4 ? factor : 1;
        if (_factor == 1)
        {
            _stageA = null;
            _stageB = null;
            return;
        }

        _stageA = new Stage();
        _stageA.Prepare();
        if (_factor == 4)
        {
            _stageB = new Stage();
            _stageB.Prepare();
        }
        else _stageB = null;

        var need = Math.Max(maxBaseSamples, 1) * _factor;
        if (_scratch.Length < need) _scratch = new float[need];
        if (_scratch2.Length < need) _scratch2 = new float[need];
        Reset();
    }

    public void Reset()
    {
        _stageA?.Reset();
        _stageB?.Reset();
    }

    public int Upsample(ReadOnlySpan<float> input, Span<float> output)
    {
        if (_factor <= 1 || _stageA is null)
        {
            input.CopyTo(output);
            return input.Length;
        }

        if (_factor == 2)
            return _stageA.Upsample(input, output);

        var midLen = input.Length * 2;
        if (_scratch.Length < midLen) _scratch = new float[midLen];
        _stageA.Upsample(input, _scratch.AsSpan(0, midLen));
        return _stageB!.Upsample(_scratch.AsSpan(0, midLen), output);
    }

    public int Downsample(ReadOnlySpan<float> input, Span<float> output)
    {
        if (_factor <= 1 || _stageA is null)
        {
            input.CopyTo(output);
            return input.Length;
        }

        if (_factor == 2)
            return _stageA.Downsample(input, output);

        var midLen = input.Length / 2;
        if (_scratch.Length < midLen) _scratch = new float[midLen];
        _stageB!.Downsample(input, _scratch.AsSpan(0, midLen));
        return _stageA.Downsample(_scratch.AsSpan(0, midLen), output);
    }

    public Span<float> UpsampleToScratch(ReadOnlySpan<float> input)
    {
        var need = input.Length * Math.Max(1, _factor);
        if (_scratch2.Length < need) _scratch2 = new float[need];
        var len = Upsample(input, _scratch2);
        return _scratch2.AsSpan(0, len);
    }

    public float PeakAfterUpsample(ReadOnlySpan<float> mono)
    {
        if (_factor <= 1 || _stageA is null)
        {
            float peak = 0;
            for (var i = 0; i < mono.Length; i++)
            {
                var a = MathF.Abs(mono[i]);
                if (a > peak) peak = a;
            }
            return peak;
        }

        if (_factor == 2)
            return _stageA.PeakAfterUpsample(mono);

        var midLen = mono.Length * 2;
        if (_scratch.Length < midLen) _scratch = new float[midLen];
        _stageA.Upsample(mono, _scratch.AsSpan(0, midLen));
        return _stageB!.PeakAfterUpsample(_scratch.AsSpan(0, midLen));
    }

    private sealed class Stage
    {
        private float[] _kernelUp = Array.Empty<float>();
        private float[] _kernelDn = Array.Empty<float>();
        private float[] _upHist = Array.Empty<float>();
        private int _upPos;
        private float[] _dnHist = Array.Empty<float>();
        private int _dnPos;

        public void Prepare()
        {
            _kernelUp = (float[])Kernel2.Clone();
            _kernelDn = (float[])Kernel2.Clone();
            NormalizeSum(_kernelUp, 2f); // gain Factor after zero-insert
            NormalizeSum(_kernelDn, 1f);
            var n = _kernelUp.Length;
            if (_upHist.Length != n) _upHist = new float[n];
            if (_dnHist.Length != n) _dnHist = new float[n];
            Reset();
        }

        public void Reset()
        {
            Array.Clear(_upHist);
            Array.Clear(_dnHist);
            _upPos = 0;
            _dnPos = 0;
        }

        private static void NormalizeSum(float[] k, float targetSum)
        {
            double sum = 0;
            for (var i = 0; i < k.Length; i++) sum += k[i];
            if (Math.Abs(sum) < 1e-12) return;
            var scale = (float)(targetSum / sum);
            for (var i = 0; i < k.Length; i++) k[i] *= scale;
        }

        private static void Push(float sample, float[] hist, ref int pos)
        {
            pos++;
            if (pos >= hist.Length) pos = 0;
            hist[pos] = sample;
        }

        /// <summary>General FIR output at the current history position.</summary>
        private static float FilterCurrent(float[] hist, int pos, float[] kernel)
        {
            var n = kernel.Length;
            float y = 0;
            for (var k = 0; k < n; k++)
            {
                var c = kernel[k];
                if (c == 0f) continue;
                var idx = pos - k;
                if (idx < 0) idx += n;
                y += hist[idx] * c;
            }
            return y;
        }

        /// <summary>
        /// Even polyphase arm of the half-band interpolator. At an inserted source sample the odd
        /// centre tap points at an inserted zero, so only the eight even taps contribute.
        /// </summary>
        private static float FilterEvenArm(float[] hist, int pos, float[] kernel)
        {
            var n = kernel.Length;
            float y = 0;
            for (var k = 0; k < n; k += 2)
            {
                var idx = pos - k;
                if (idx < 0) idx += n;
                y += hist[idx] * kernel[k];
            }
            return y;
        }

        private static int WrappedIndex(int pos, int delay, int length)
        {
            var index = pos - delay;
            return index < 0 ? index + length : index;
        }

        public int Upsample(ReadOnlySpan<float> input, Span<float> output)
        {
            var outLen = input.Length * 2;
            if (output.Length < outLen) throw new ArgumentException("output too short");
            var oi = 0;
            for (var i = 0; i < input.Length; i++)
            {
                Push(input[i], _upHist, ref _upPos);
                output[oi++] = FilterEvenArm(_upHist, _upPos, _kernelUp);
                Push(0f, _upHist, ref _upPos);
                output[oi++] = _upHist[WrappedIndex(_upPos, 7, _upHist.Length)] * _kernelUp[7];
            }
            return outLen;
        }

        public int Downsample(ReadOnlySpan<float> input, Span<float> output)
        {
            var outFrames = input.Length / 2;
            if (output.Length < outFrames) throw new ArgumentException("output too short");
            var oi = 0;
            var i = 0;
            for (; i + 1 < input.Length; i += 2)
            {
                // The first phase is discarded by decimation; only update history.
                Push(input[i], _dnHist, ref _dnPos);
                Push(input[i + 1], _dnHist, ref _dnPos);
                output[oi++] = FilterCurrent(_dnHist, _dnPos, _kernelDn);
            }
            if (i < input.Length) Push(input[i], _dnHist, ref _dnPos);
            return oi;
        }

        public float PeakAfterUpsample(ReadOnlySpan<float> mono)
        {
            float p = 0;
            for (var i = 0; i < mono.Length; i++)
            {
                Push(mono[i], _upHist, ref _upPos);
                var y0 = FilterEvenArm(_upHist, _upPos, _kernelUp);
                var a0 = MathF.Abs(y0);
                if (a0 > p) p = a0;
                Push(0f, _upHist, ref _upPos);
                var y1 = _upHist[WrappedIndex(_upPos, 7, _upHist.Length)] * _kernelUp[7];
                var a1 = MathF.Abs(y1);
                if (a1 > p) p = a1;
            }
            return p;
        }
    }
}
