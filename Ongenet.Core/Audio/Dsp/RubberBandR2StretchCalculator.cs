using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Per-chunk output hop calculator modelled on Rubber Band R2
/// <c>StretchCalculator::calculateSingle</c> for unity time ratio.
/// </summary>
internal sealed class RubberBandR2StretchCalculator
{
    private readonly int _sampleRate;
    private readonly int _defaultIncrement;
    private float _prevDf;
    private int _transientAmnesty;
    private long _inFrameCounter;
    private long _outFrameCounter;
    private long _checkpointIn;
    private long _checkpointOut;
    private double _prevRatio = 1.0;
    private double _prevTimeRatio = 1.0;
    private bool _justReset = true;

    public RubberBandR2StretchCalculator(int sampleRate, int defaultIncrement)
    {
        _sampleRate = sampleRate;
        _defaultIncrement = defaultIncrement;
    }

    public IReadOnlyList<int> BuildIncrements(double pitchScale, int inputIncrement, int analysisWindow,
        int synthesisWindow, IReadOnlyList<float> spectralFlux)
    {
        var effectivePitchRatio = 1.0 / pitchScale;
        var timeRatio = 1.0;
        var increments = new List<int>(spectralFlux.Count);

        Reset();
        for (var i = 0; i < spectralFlux.Count; i++)
        {
            increments.Add(CalculateSingle(timeRatio, effectivePitchRatio, spectralFlux[i], inputIncrement,
                analysisWindow, synthesisWindow));
        }

        return increments;
    }

    private void Reset()
    {
        _prevDf = 0;
        _transientAmnesty = 0;
        _inFrameCounter = 0;
        _outFrameCounter = 0;
        _checkpointIn = 0;
        _checkpointOut = 0;
        _prevRatio = 1.0;
        _prevTimeRatio = 1.0;
        _justReset = true;
    }

    private int CalculateSingle(double timeRatio, double effectivePitchRatio, float df, int inIncrement,
        int analysisWindow, int synthesisWindow)
    {
        var ratio = timeRatio / effectivePitchRatio;
        var increment = inIncrement <= 0 ? _defaultIncrement : inIncrement;
        var outIncrement = (int)Math.Round(increment * ratio);
        var isTransient = false;

        if (!_justReset && Math.Abs(ratio - _prevRatio) > 1e-9)
        {
            var toCheckpoint = ExpectedOutFrame(_inFrameCounter, _prevTimeRatio);
            _checkpointIn = _inFrameCounter;
            _checkpointOut = toCheckpoint;
        }

        _justReset = false;
        _prevRatio = ratio;
        _prevTimeRatio = timeRatio;

        var intended = ExpectedOutFrame(_inFrameCounter + analysisWindow / 4, timeRatio);
        var projected = (long)Math.Round(_outFrameCounter + synthesisWindow / 4.0 * effectivePitchRatio);
        var divergence = projected - intended;

        const float transientThreshold = 0.35f;
        if (df > _prevDf * 1.1f && df > transientThreshold)
        {
            if (divergence is <= 1000 and >= -1000)
                isTransient = true;
        }

        _prevDf = df;

        if (_transientAmnesty > 0)
        {
            if (isTransient) isTransient = false;
            _transientAmnesty--;
        }

        if (isTransient)
        {
            _transientAmnesty = (int)Math.Ceiling(_sampleRate / (20.0 * increment));
            outIncrement = increment;
        }
        else
        {
            double recovery;
            if (divergence is > 1000 or < -1000)
                recovery = divergence / ((_sampleRate / 10.0) / increment);
            else if (divergence is > 100 or < -100)
                recovery = divergence / ((_sampleRate / 20.0) / increment);
            else
                recovery = divergence / 4.0;

            var incr = (int)Math.Round(outIncrement - recovery);
            if (incr < 1) incr = 1;
            outIncrement = incr;
        }

        _inFrameCounter += increment;
        _outFrameCounter += outIncrement;

        if (isTransient)
            return -outIncrement;

        return outIncrement;
    }

    private long ExpectedOutFrame(long inFrame, double timeRatio)
        => (long)Math.Round(_checkpointOut + (inFrame - _checkpointIn) * timeRatio);
}
