using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A uniformly-partitioned FFT convolution reverb (mono impulse applied to a stereo signal). The impulse
/// response is chopped into equal blocks; each block is pre-transformed once, and every audio block the
/// engine transforms the incoming samples, accumulates the products against a frequency-domain delay line
/// of past input spectra, and inverse-transforms with overlap-add. This is O(partitions) per sample —
/// cheap enough for realtime with hundreds-of-ms tails, where a direct time-domain FIR would not be. If no
/// impulse is loaded it synthesises an exponential-decay noise burst so it always sounds like a room.
/// <see cref="Process"/> is allocation-free; the FFT/IFFT run only once per <see cref="LatencySamples"/>
/// block. Reusable by any reverb/space effect.
/// </summary>
public sealed class ConvolutionReverb
{
    private const int BlockSize = 256;      // power of two → FFT size 512
    private const int FftSize = BlockSize * 2;

    private int _sampleRate = 44100;

    // Frequency-domain IR partitions.
    private double[][] _irRe = Array.Empty<double[]>();
    private double[][] _irIm = Array.Empty<double[]>();
    private int _partitions;

    // Frequency-domain delay line of past input spectra, per channel.
    private double[][] _fdlReL = Array.Empty<double[]>();
    private double[][] _fdlImL = Array.Empty<double[]>();
    private double[][] _fdlReR = Array.Empty<double[]>();
    private double[][] _fdlImR = Array.Empty<double[]>();
    private int _writeSlot;

    // Scratch (reused every block, never allocated in Process).
    private readonly double[] _fftRe = new double[FftSize];
    private readonly double[] _fftIm = new double[FftSize];
    private readonly double[] _accRe = new double[FftSize];
    private readonly double[] _accIm = new double[FftSize];

    // Per-block IO ring and overlap-add tails.
    private readonly float[] _inBlockL = new float[BlockSize];
    private readonly float[] _inBlockR = new float[BlockSize];
    private readonly float[] _outBlockL = new float[BlockSize];
    private readonly float[] _outBlockR = new float[BlockSize];
    private readonly double[] _overlapL = new double[BlockSize];
    private readonly double[] _overlapR = new double[BlockSize];
    private int _pos;

    private float _mix = 1f;

    /// <summary>Wet/dry blend (0 = dry, 1 = fully wet).</summary>
    public float Mix { get => _mix; set => _mix = AudioMath.Clamp(value, 0f, 1f); }

    /// <summary>The added latency in samples (one processing block).</summary>
    public int LatencySamples => BlockSize;

    /// <summary>
    /// Prepares the engine and installs a synthesised default impulse (exponential-decay noise) of
    /// <paramref name="lengthSeconds"/>. Call <see cref="LoadImpulse"/> afterwards to replace it.
    /// </summary>
    public void Configure(int sampleRate, double lengthSeconds = 0.5)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 44100;
        LoadImpulse(GenerateDefaultImpulse(_sampleRate, lengthSeconds));
    }

    /// <summary>Loads a mono impulse response, partitioning and pre-transforming it.</summary>
    public void LoadImpulse(float[] ir)
    {
        var len = ir is { Length: > 0 } ? ir.Length : 1;
        _partitions = (len + BlockSize - 1) / BlockSize;

        _irRe = new double[_partitions][];
        _irIm = new double[_partitions][];
        _fdlReL = new double[_partitions][];
        _fdlImL = new double[_partitions][];
        _fdlReR = new double[_partitions][];
        _fdlImR = new double[_partitions][];

        for (var p = 0; p < _partitions; p++)
        {
            var re = new double[FftSize];
            var im = new double[FftSize];
            var start = p * BlockSize;
            var count = Math.Min(BlockSize, len - start);
            for (var i = 0; i < count; i++) re[i] = ir![start + i];
            Fft.Forward(re, im);
            _irRe[p] = re;
            _irIm[p] = im;

            _fdlReL[p] = new double[FftSize];
            _fdlImL[p] = new double[FftSize];
            _fdlReR[p] = new double[FftSize];
            _fdlImR[p] = new double[FftSize];
        }

        Reset();
    }

    public void Reset()
    {
        _writeSlot = 0;
        _pos = 0;
        Array.Clear(_overlapL, 0, _overlapL.Length);
        Array.Clear(_overlapR, 0, _overlapR.Length);
        Array.Clear(_outBlockL, 0, _outBlockL.Length);
        Array.Clear(_outBlockR, 0, _outBlockR.Length);
        Array.Clear(_inBlockL, 0, _inBlockL.Length);
        Array.Clear(_inBlockR, 0, _inBlockR.Length);
        for (var p = 0; p < _partitions; p++)
        {
            Array.Clear(_fdlReL[p], 0, FftSize);
            Array.Clear(_fdlImL[p], 0, FftSize);
            Array.Clear(_fdlReR[p], 0, FftSize);
            Array.Clear(_fdlImR[p], 0, FftSize);
        }
    }

    /// <summary>
    /// Processes <paramref name="frames"/> stereo-interleaved frames in place. The wet signal is delayed
    /// by <see cref="LatencySamples"/> relative to the dry (inherent to block convolution).
    /// </summary>
    public void Process(float[] buffer, int frames)
    {
        if (_partitions == 0) return;

        for (var f = 0; f < frames; f++)
        {
            var idx = f * 2;
            var dryL = buffer[idx];
            var dryR = buffer[idx + 1];

            _inBlockL[_pos] = dryL;
            _inBlockR[_pos] = dryR;

            var wetL = _outBlockL[_pos];
            var wetR = _outBlockR[_pos];
            _pos++;

            if (_pos >= BlockSize)
            {
                ProcessBlock();
                _pos = 0;
            }

            buffer[idx] = dryL + (wetL - dryL) * _mix;
            buffer[idx + 1] = dryR + (wetR - dryR) * _mix;
        }
    }

    private void ProcessBlock()
    {
        // Store this block's input spectrum for both channels into the frequency-domain delay line, then
        // accumulate against every IR partition and overlap-add the result.
        TransformInput(_inBlockL, _fdlReL, _fdlImL);
        ConvolveChannel(_fdlReL, _fdlImL, _overlapL, _outBlockL);

        TransformInput(_inBlockR, _fdlReR, _fdlImR);
        ConvolveChannel(_fdlReR, _fdlImR, _overlapR, _outBlockR);

        _writeSlot++;
        if (_writeSlot >= _partitions) _writeSlot = 0;
    }

    private void TransformInput(float[] inBlock, double[][] fdlRe, double[][] fdlIm)
    {
        for (var i = 0; i < BlockSize; i++) { _fftRe[i] = inBlock[i]; _fftIm[i] = 0.0; }
        for (var i = BlockSize; i < FftSize; i++) { _fftRe[i] = 0.0; _fftIm[i] = 0.0; }
        Fft.Forward(_fftRe, _fftIm);
        Array.Copy(_fftRe, fdlRe[_writeSlot], FftSize);
        Array.Copy(_fftIm, fdlIm[_writeSlot], FftSize);
    }

    private void ConvolveChannel(double[][] fdlRe, double[][] fdlIm, double[] overlap, float[] outBlock)
    {
        Array.Clear(_accRe, 0, FftSize);
        Array.Clear(_accIm, 0, FftSize);

        for (var p = 0; p < _partitions; p++)
        {
            var slot = _writeSlot - p;
            if (slot < 0) slot += _partitions;

            var xr = fdlRe[slot];
            var xi = fdlIm[slot];
            var hr = _irRe[p];
            var hi = _irIm[p];

            for (var k = 0; k < FftSize; k++)
            {
                _accRe[k] += xr[k] * hr[k] - xi[k] * hi[k];
                _accIm[k] += xr[k] * hi[k] + xi[k] * hr[k];
            }
        }

        Fft.Inverse(_accRe, _accIm);

        // First half is this block's output (add the previous block's overlap tail); second half becomes
        // the new tail.
        for (var i = 0; i < BlockSize; i++)
            outBlock[i] = (float)(_accRe[i] + overlap[i]);
        for (var i = 0; i < BlockSize; i++)
            overlap[i] = _accRe[BlockSize + i];
    }

    private static float[] GenerateDefaultImpulse(int sampleRate, double lengthSeconds)
    {
        var len = Math.Max(BlockSize, (int)(Math.Clamp(lengthSeconds, 0.05, 10.0) * sampleRate));
        var ir = new float[len];
        var rng = new FastRandom(0xC0FFEEu);
        var decay = 5.0 / len; // ~ -43 dB by the tail end
        double sum = 0.0;
        for (var i = 0; i < len; i++)
        {
            var env = Math.Exp(-decay * i);
            var v = rng.NextBipolar() * (float)env;
            ir[i] = v;
            sum += v * (double)v;
        }

        // Normalise to unit energy so swapping IR lengths doesn't jump the level.
        var norm = sum > 1e-12 ? (float)(1.0 / Math.Sqrt(sum)) : 1f;
        for (var i = 0; i < len; i++) ir[i] *= norm;
        return ir;
    }
}
