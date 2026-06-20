using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A single-channel "tape stop": input is written to a ring buffer and read back at a variable speed.
/// As <c>stop</c> rises toward 1 the read rate falls below the write rate, so the read head lags and
/// the pitch glides down — the vinyl/tape power-down slur. At full stop the output mutes; when
/// <c>stop</c> returns to 0 the read head rapidly catches back up to real time. Reusable by any
/// instrument/effect that wants a varispeed slowdown. Hold one per channel.
/// </summary>
public sealed class TapeStopProcessor
{
    private readonly DelayLine _line = new();
    private double _readDelay;   // samples the read head is behind the write head
    private int _maxDelay;
    private const double CatchUp = 8.0; // samples/sample re-sync rate when released

    public void Prepare(double sampleRate)
    {
        var sr = sampleRate > 0 ? sampleRate : 44100.0;
        var size = (int)(sr * 2.0) + 8; // up to ~2 s of slowdown tail
        _line.Resize(size);
        _maxDelay = size - 4;
        _readDelay = 0;
    }

    public void Reset()
    {
        _line.Clear();
        _readDelay = 0;
    }

    /// <summary>
    /// Processes one sample. <paramref name="stop"/> in 0..1: 0 = real time, 1 = fully stopped.
    /// </summary>
    public float Process(float x, double stop)
    {
        _line.Write(x);

        stop = stop < 0 ? 0 : stop > 1 ? 1 : stop;
        var y = _line.ReadFrac(_readDelay);

        var speed = 1.0 - stop;                 // read advances this much per output sample
        _readDelay += 1.0 - speed;              // ⇒ lag grows by stop each sample
        if (stop <= 1e-4 && _readDelay > 0)     // released: snap the read head back to now
            _readDelay = Math.Max(0.0, _readDelay - CatchUp);

        if (_readDelay >= _maxDelay) { _readDelay = _maxDelay; return 0f; } // tape fully halted
        return y;
    }
}
