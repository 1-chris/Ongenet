using System;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio;

/// <summary>
/// Default <see cref="IAuditionPlayer"/>. Holds the buffer being previewed plus a fractional read position;
/// the UI thread sets it via <see cref="Play"/>/<see cref="Stop"/> while the audio thread reads + advances
/// it in <see cref="Mix"/> (linear-resampled to the device rate). State is exchanged through volatile
/// fields — adequate for a single preview voice that doesn't need sample-accurate hand-off.
/// </summary>
public sealed class AuditionPlayer : IAuditionPlayer
{
    private const float Gain = 0.9f;

    private volatile AudioSampleBuffer? _buffer;
    private volatile bool _playing;
    private double _pos;            // fractional read position in source frames (audio thread owns it)

    public bool IsPlaying => _playing;

    public event Action? Finished;

    public void Play(AudioSampleBuffer buffer)
    {
        if (buffer.FrameCount <= 0) { Stop(); return; }
        _playing = false;   // pause the audio thread's read while we re-point
        _buffer = null;
        _pos = 0;
        _buffer = buffer;
        _playing = true;
    }

    public void Stop()
    {
        _playing = false;
        _buffer = null;
    }

    public void Mix(Span<float> buffer, AudioFormat format)
    {
        if (!_playing) return;
        var src = _buffer;
        if (src is null) return;

        var channels = format.Channels < 1 ? 1 : format.Channels;
        var frames = buffer.Length / channels;
        var deviceRate = format.SampleRate <= 0 ? 44100 : format.SampleRate;
        var ratio = (double)src.SampleRate / deviceRate;
        var srcFrames = src.FrameCount;
        var srcChannels = src.Channels;
        var pos = _pos;
        var ended = false;

        for (var f = 0; f < frames; f++)
        {
            var i0 = (long)pos;
            if (i0 >= srcFrames) { ended = true; break; }
            var frac = (float)(pos - i0);
            var i1 = i0 + 1 < srcFrames ? i0 + 1 : i0;
            var baseIndex = f * channels;
            for (var c = 0; c < channels; c++)
            {
                var sc = c < srcChannels ? c : srcChannels - 1;
                var s = src.Sample(i0, sc) + (src.Sample(i1, sc) - src.Sample(i0, sc)) * frac;
                buffer[baseIndex + c] += s * Gain;
            }

            pos += ratio;
        }

        _pos = pos;
        if (ended)
        {
            _playing = false;
            _buffer = null;
            Finished?.Invoke();
        }
    }
}
