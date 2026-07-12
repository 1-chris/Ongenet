using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Basic monophonic audio-to-MIDI: runs YIN pitch detection over clip audio and emits note events
/// mapped into clip-local beats.
/// </summary>
public static class MonophonicAudioToMidi
{
    private const int DetectHop = 256;
    private const double MinNoteSeconds = 0.05;
    private const double PitchHoldSemitones = 0.55;

    /// <summary>
    /// Detects monophonic notes in <paramref name="buffer"/> and returns clip-relative MIDI notes
    /// spanning <paramref name="clipLengthBeats"/>.
    /// </summary>
    public static List<MidiNote> Convert(AudioSampleBuffer buffer, double clipLengthBeats, double referenceHz = 440.0)
    {
        var notes = new List<MidiNote>();
        if (buffer.FrameCount <= 0 || buffer.SampleRate <= 0 || clipLengthBeats <= 0)
            return notes;

        var mono = MixToMono(buffer);
        var sr = buffer.SampleRate;
        var detector = new PitchDetector();
        detector.Configure(sr, 70.0, 1200.0);

        var segments = new List<(double startSec, double endSec, int midi)>();
        double? segStart = null;
        double segEnd = 0;
        int segMidi = 0;
        var pitchWindow = new Queue<int>();

        for (var i = 0; i < mono.Length; i++)
        {
            detector.Push(mono[i]);
            if (i % DetectHop != 0 && i != mono.Length - 1) continue;

            var hz = detector.Detect();
            var t = i / (double)sr;
            if (hz > 0)
            {
                pitchWindow.Enqueue(HzToMidi(hz, referenceHz));
                while (pitchWindow.Count > 5) pitchWindow.Dequeue();
                var ordered = pitchWindow.ToArray();
                Array.Sort(ordered);
                var midi = ordered[ordered.Length / 2];
                if (segStart is null)
                {
                    segStart = t;
                    segMidi = midi;
                    segEnd = t;
                }
                else if (Math.Abs(midi - segMidi) <= PitchHoldSemitones)
                {
                    segEnd = t;
                }
                else
                {
                    TryCloseSegment(segments, segStart.Value, segEnd, segMidi);
                    segStart = t;
                    segMidi = midi;
                    segEnd = t;
                }
            }
            else if (segStart is not null)
            {
                TryCloseSegment(segments, segStart.Value, segEnd, segMidi);
                segStart = null;
                pitchWindow.Clear();
            }
            else pitchWindow.Clear();
        }

        if (segStart is not null)
            TryCloseSegment(segments, segStart.Value, segEnd, segMidi);

        var durationSec = mono.Length / (double)sr;
        foreach (var (startSec, endSec, midi) in segments)
        {
            var startBeat = startSec / durationSec * clipLengthBeats;
            var endBeat = endSec / durationSec * clipLengthBeats;
            var length = Math.Max(1.0 / 64.0, endBeat - startBeat);
            notes.Add(new MidiNote
            {
                Note = Math.Clamp(midi, 0, 127),
                StartBeat = Math.Max(0, startBeat),
                LengthBeats = length,
                Velocity = 0.85f
            });
        }

        return notes;
    }

    private static void TryCloseSegment(List<(double, double, int)> segments, double start, double end, int midi)
    {
        if (end - start < MinNoteSeconds) return;
        segments.Add((start, end, midi));
    }

    private static float[] MixToMono(AudioSampleBuffer buffer)
    {
        var ch = buffer.Channels < 1 ? 1 : buffer.Channels;
        var frames = buffer.FrameCount;
        var mono = new float[frames];
        var samples = buffer.Samples;
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var c = 0; c < ch; c++)
                sum += samples[f * ch + c];
            mono[f] = (float)(sum / ch);
        }

        return mono;
    }

    private static int HzToMidi(double hz, double referenceHz)
    {
        if (hz <= 0 || referenceHz <= 0) return 0;
        return (int)Math.Round(69.0 + 12.0 * Math.Log(hz / referenceHz, 2.0));
    }
}
