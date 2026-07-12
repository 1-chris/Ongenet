using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Interleaved delay line for plugin delay compensation (PDC).</summary>
public sealed class PdcDelayLine
{
    private float[] _buffer = Array.Empty<float>();
    private int _writePos;
    private int _delaySamples;
    private int _channels = 2;

    public int DelaySamples => _delaySamples;

    public void Configure(int delaySamples, int channels, int maxBlockFrames)
    {
        _channels = channels < 1 ? 1 : channels;
        _delaySamples = Math.Max(0, delaySamples);
        var needed = (_delaySamples + maxBlockFrames) * _channels;
        if (_buffer.Length < needed) _buffer = new float[needed];
        _writePos = 0;
        Array.Clear(_buffer, 0, _buffer.Length);
    }

    /// <summary>Reads <paramref name="buffer"/> delayed by <see cref="DelaySamples"/> and writes the input into the line.</summary>
    public void Process(Span<float> buffer, int frames)
    {
        if (_delaySamples <= 0 || frames <= 0) return;
        var ch = _channels;
        var cap = _buffer.Length / ch;
        if (cap <= _delaySamples) return;

        for (var frame = 0; frame < frames; frame++)
        {
            var readPos = _writePos - _delaySamples;
            if (readPos < 0) readPos += cap;
            for (var c = 0; c < ch; c++)
            {
                var i = frame * ch + c;
                var delayed = _buffer[readPos * ch + c];
                _buffer[_writePos * ch + c] = buffer[i];
                buffer[i] = delayed;
            }

            _writePos++;
            if (_writePos >= cap) _writePos = 0;
        }
    }
}
