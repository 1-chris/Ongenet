using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>R2 offline sizing derived from Rubber Band <c>calculateSizes</c> (non-realtime branch).</summary>
internal readonly record struct RubberBandR2Config(
    int SampleRate,
    int FftSize,
    int AnalysisWindowSize,
    int SynthesisWindowSize,
    int InputIncrement,
    int MeanOutputIncrement,
    double PitchScale,
    double TimeRatio)
{
    public double EffectiveRatio => TimeRatio * PitchScale;

    public static RubberBandR2Config Create(int sampleRate, double pitchScale, double timeRatio, int expectedFrames)
    {
        if (pitchScale <= 0) pitchScale = 1.0;
        if (timeRatio <= 0) timeRatio = 1.0;

        const int defaultIncrement = 256;
        var rateMultiple = sampleRate / 48000.0;
        var baseFftSize = RoundUp((int)Math.Round(2048 * rateMultiple));

        var windowSize = baseFftSize;
        var inputIncrement = defaultIncrement;
        int outputIncrement;
        var r = timeRatio * pitchScale;

        if (r < 1.0)
        {
            inputIncrement = windowSize / 4;
            while (inputIncrement >= 512) inputIncrement /= 2;
            outputIncrement = Math.Max(1, (int)Math.Floor(inputIncrement * r));
            if (outputIncrement < 1)
            {
                outputIncrement = 1;
                inputIncrement = RoundUp((int)Math.Ceiling(outputIncrement / r));
                windowSize = inputIncrement * 4;
            }
        }
        else
        {
            outputIncrement = windowSize / 6;
            inputIncrement = Math.Max(1, (int)(outputIncrement / r));
            while (outputIncrement > 1024 && inputIncrement > 1)
            {
                outputIncrement /= 2;
                inputIncrement = Math.Max(1, (int)(outputIncrement / r));
            }

            while (inputIncrement < 1)
            {
                outputIncrement *= 2;
                inputIncrement = Math.Max(1, (int)(outputIncrement / r));
            }

            windowSize = Math.Max(windowSize, RoundUp(outputIncrement * 6));
            if (r > 5)
                while (windowSize < 8192)
                    windowSize *= 2;
        }

        if (expectedFrames > 0)
        {
            while (inputIncrement * 4 > expectedFrames && inputIncrement > 1)
                inputIncrement /= 2;
        }

        var fftSize = RoundUpToPowerOfTwo(windowSize);
        while (expectedFrames > 0 && fftSize > expectedFrames && fftSize > 256)
            fftSize >>= 1;

        windowSize = fftSize;
        return new RubberBandR2Config(sampleRate, fftSize, fftSize, fftSize, inputIncrement,
            Math.Max(1, (int)Math.Round(inputIncrement * r)), pitchScale, timeRatio);
    }

    private static int RoundUp(int n) => n <= 0 ? 1 : n;

    private static int RoundUpToPowerOfTwo(int n)
    {
        var p = 1;
        while (p < n) p <<= 1;
        return p;
    }
}
