using System;

namespace Ongenet.Core.Audio.Files;

/// <summary>Anti-aliased decimator from qm-dsp (factor 8).</summary>
internal sealed class QueenMaryDecimator
{
    private readonly int _inputLength;
    private readonly int _outputLength;
    private readonly int _decFactor;
    private readonly double[] _decBuffer;

    private readonly double[] _b;
    private readonly double[] _a;

    private double _input;
    private double _output;
    private double _o1, _o2, _o3, _o4, _o5, _o6, _o7;

    public QueenMaryDecimator(int inputLength, int decFactor = 8)
    {
        _inputLength = inputLength;
        _decFactor = decFactor;
        _outputLength = inputLength / decFactor;
        _decBuffer = new double[inputLength];

        _b = new double[8];
        _a = new double[8];
        if (decFactor == 8)
        {
            _b[0] = 0.060111378492136; _b[1] = -0.257323420830598; _b[2] = 0.420583503165928;
            _b[3] = -0.222750785197418; _b[4] = -0.222750785197418; _b[5] = 0.420583503165928;
            _b[6] = -0.257323420830598; _b[7] = 0.060111378492136;
            _a[0] = 1; _a[1] = -5.667654878577432; _a[2] = 14.062452278088417;
            _a[3] = -19.737303840697738; _a[4] = 16.889698874608641; _a[5] = -8.796600612325928;
            _a[6] = 2.577553446979888; _a[7] = -0.326903916815751;
        }
        else
        {
            _b[0] = 1;
            _a[0] = 1;
        }
    }

    public int OutputLength => _outputLength;

    public void Process(ReadOnlySpan<float> src, Span<double> dst)
    {
        if (_decFactor == 1)
        {
            for (var i = 0; i < _outputLength; i++)
                dst[i] = src[i];
            return;
        }

        DoAntiAlias(src, _decBuffer);
        for (var i = 0; i < _outputLength; i++)
            dst[i] = _decBuffer[_decFactor * i];
    }

    private void DoAntiAlias(ReadOnlySpan<float> src, double[] dst)
    {
        for (var i = 0; i < _inputLength; i++)
        {
            _input = src[i];
            _output = _input * _b[0] + _o1;
            _o1 = _input * _b[1] - _output * _a[1] + _o2;
            _o2 = _input * _b[2] - _output * _a[2] + _o3;
            _o3 = _input * _b[3] - _output * _a[3] + _o4;
            _o4 = _input * _b[4] - _output * _a[4] + _o5;
            _o5 = _input * _b[5] - _output * _a[5] + _o6;
            _o6 = _input * _b[6] - _output * _a[6] + _o7;
            _o7 = _input * _b[7] - _output * _a[7];
            dst[i] = _output;
        }
    }
}
