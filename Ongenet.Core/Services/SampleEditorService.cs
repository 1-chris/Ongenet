using System;
using System.Collections.Generic;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Services;

/// <summary>Sample editor v2 operations (normalize, fade, selection stretch, input capture).</summary>
public static class SampleEditorService
{
    public static void Normalize(AudioSampleBuffer buffer, float targetPeak = 0.99f)
    {
        var max = 0f;
        for (var i = 0; i < buffer.FrameCount * buffer.Channels; i++)
        {
            var s = MathF.Abs(buffer.Samples[i]);
            if (s > max) max = s;
        }

        if (max < 1e-9f) return;
        var gain = targetPeak / max;
        for (var i = 0; i < buffer.Samples.Length; i++) buffer.Samples[i] *= gain;
    }

    public static void ApplyFadeIn(AudioSampleBuffer buffer, int startFrame, int endFrame)
    {
        if (endFrame <= startFrame) return;
        for (var f = startFrame; f < endFrame && f < buffer.FrameCount; f++)
        {
            var g = (float)(f - startFrame) / (endFrame - startFrame);
            for (var c = 0; c < buffer.Channels; c++)
                buffer.Samples[f * buffer.Channels + c] *= g;
        }
    }

    public static void ApplyFadeOut(AudioSampleBuffer buffer, int startFrame, int endFrame)
    {
        if (endFrame <= startFrame) return;
        for (var f = startFrame; f < endFrame && f < buffer.FrameCount; f++)
        {
            var g = 1f - (float)(f - startFrame) / (endFrame - startFrame);
            for (var c = 0; c < buffer.Channels; c++)
                buffer.Samples[f * buffer.Channels + c] *= g;
        }
    }

    /// <summary>Creates a growable waveform for live input capture.</summary>
    public static AudioWaveform CreateLiveWaveform(int sampleRate, int samplesPerBucket = 128)
        => new(samplesPerBucket, sampleRate);

    /// <summary>
    /// Appends an armed-input capture block into <paramref name="buffer"/>, resampling when the
    /// input device rate differs. Returns the number of frames appended.
    /// </summary>
    public static int AppendInput(AudioSampleBuffer buffer, ReadOnlySpan<float> input, int inputChannels,
        int inputSampleRate, AudioWaveform? waveform = null)
    {
        if (input.Length == 0 || inputChannels < 1 || inputSampleRate <= 0) return 0;

        inputChannels = Math.Max(1, inputChannels);
        var inputFrames = input.Length / inputChannels;
        if (inputFrames <= 0) return 0;

        var destChannels = Math.Max(1, buffer.Channels);
        var destRate = buffer.SampleRate > 0 ? buffer.SampleRate : inputSampleRate;
        var ratio = (double)inputSampleRate / destRate;
        var outFrames = inputSampleRate == destRate
            ? inputFrames
            : Math.Max(1, (int)Math.Round(inputFrames / ratio));

        var startFrame = buffer.FrameCount;
        var needed = startFrame + outFrames;
        buffer.EnsureFrames(needed);

        for (var f = 0; f < outFrames; f++)
        {
            var srcFrame = inputSampleRate == destRate
                ? f
                : (long)Math.Round(f * ratio);
            if (srcFrame >= inputFrames) break;

            var dstBase = (startFrame + f) * destChannels;
            var srcBase = (int)srcFrame * inputChannels;
            for (var c = 0; c < destChannels; c++)
            {
                var sc = c < inputChannels ? c : inputChannels - 1;
                buffer.Samples[dstBase + c] = input[srcBase + sc];
            }
        }

        var appended = (int)Math.Min(outFrames, buffer.FrameCount - startFrame);
        if (waveform is not null && appended > 0)
        {
            var offset = (int)(startFrame * destChannels);
            var length = appended * destChannels;
            waveform.Append(new ReadOnlySpan<float>(buffer.Samples, offset, length), destChannels);
        }

        return appended;
    }

    /// <summary>Captures armed input into a growable buffer while recording.</summary>
    public sealed class InputRecorder
    {
        private readonly List<float> _pending = new();
        private int _channels = 1;
        private int _sampleRate = 44100;

        public AudioSampleBuffer? Target { get; private set; }
        public AudioWaveform? Waveform { get; private set; }

        public void Begin(AudioSampleBuffer target, AudioWaveform? waveform, int inputChannels, int inputSampleRate)
        {
            Target = target;
            Waveform = waveform;
            _channels = Math.Max(1, inputChannels);
            _sampleRate = inputSampleRate > 0 ? inputSampleRate : target.SampleRate;
            _pending.Clear();
        }

        public void Enqueue(ReadOnlySpan<float> block, int channels)
        {
            if (Target is null || block.Length == 0) return;
            _channels = Math.Max(1, channels);
            _pending.AddRange(block.ToArray());
        }

        public int Flush()
        {
            if (Target is null || _pending.Count == 0) return 0;
            var appended = AppendInput(Target, _pending.ToArray(), _channels, _sampleRate, Waveform);
            _pending.Clear();
            return appended;
        }

        public void End()
        {
            Flush();
            Waveform?.Flush();
            Target = null;
            Waveform = null;
            _pending.Clear();
        }
    }
}
