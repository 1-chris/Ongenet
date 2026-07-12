using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>Polyphonic audio-to-MIDI via harmonic peak detection (basic implementation).</summary>
public static class PolyphonicAudioToMidi
{
    public sealed record NoteEvent(double StartBeat, double LengthBeats, int Note, float Velocity);

    /// <summary>Detect multiple simultaneous pitches per analysis frame and emit MIDI notes.</summary>
    public static IReadOnlyList<NoteEvent> Convert(AudioSampleBuffer buffer, double lengthBeats, int maxPolyphony = 6)
    {
        var events = new List<NoteEvent>();
        if (buffer.Channels == 0 || buffer.FrameCount == 0 || lengthBeats <= 0) return events;

        var mono = Downmix(buffer);
        var frameSize = Math.Max(512, buffer.SampleRate / 20);
        var hop = frameSize / 2;
        var totalFrames = Math.Max(1, (int)((mono.Length - frameSize) / hop));
        var beatPerFrame = lengthBeats / totalFrames;

        var active = new Dictionary<int, (int startFrame, float vel)>();

        for (var f = 0; f < totalFrames; f++)
        {
            var offset = f * hop;
            var pitches = DetectPitches(mono, offset, frameSize, buffer.SampleRate, maxPolyphony);
            var pitchSet = pitches.Select(p => p.note).ToHashSet();

            foreach (var (note, _) in pitches)
            {
                if (!active.ContainsKey(note))
                    active[note] = (f, pitches.First(p => p.note == note).vel);
            }

            var ended = active.Keys.Where(n => !pitchSet.Contains(n)).ToList();
            foreach (var note in ended)
            {
                var (start, vel) = active[note];
                events.Add(new NoteEvent(start * beatPerFrame, (f - start) * beatPerFrame, note, vel));
                active.Remove(note);
            }
        }

        foreach (var (note, (start, vel)) in active)
            events.Add(new NoteEvent(start * beatPerFrame, beatPerFrame, note, vel));

        return events;
    }

    private static float[] Downmix(AudioSampleBuffer buffer)
    {
        var ch = buffer.Channels;
        var frames = (int)buffer.FrameCount;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            var sum = 0f;
            for (var c = 0; c < ch; c++)
                sum += buffer.Sample(i, c);
            mono[i] = sum / ch;
        }
        return mono;
    }

    private static List<(int note, float vel)> DetectPitches(float[] mono, int offset, int frameSize, int sampleRate,
        int maxPolyphony)
    {
        var result = new List<(int, float)>();
        var minLag = sampleRate / 2000;
        var maxLag = sampleRate / 80;
        var window = Math.Min(frameSize, mono.Length - offset);
        if (window <= maxLag) return result;

        var scores = new List<(int lag, float score)>();
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var sum = 0f;
            var n = window - lag;
            for (var i = 0; i < n; i++)
                sum += mono[offset + i] * mono[offset + i + lag];
            scores.Add((lag, sum / n));
        }

        foreach (var (lag, score) in scores.OrderByDescending(s => s.score).Take(maxPolyphony))
        {
            if (score < 0.01f) continue;
            var hz = (float)sampleRate / lag;
            var midi = (int)Math.Round(69 + 12 * Math.Log2(hz / 440.0));
            midi = Math.Clamp(midi, 0, 127);
            result.Add((midi, Math.Clamp(score * 4f, 0.2f, 1f)));
        }

        return result;
    }
}
