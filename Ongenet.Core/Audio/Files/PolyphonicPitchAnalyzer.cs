using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>Seeds per-note pitch segments from polyphonic detection for pitch editing.</summary>
public static class PolyphonicPitchAnalyzer
{
    /// <summary>
    /// Runs polyphonic pitch detection and returns sample-bounded segments with zero-cent
    /// correction offsets (identity) and amplitudes from detection confidence.
    /// </summary>
    public static List<PitchNoteSegment> Analyze(AudioSampleBuffer buffer, double lengthBeats, int maxPolyphony = 6)
    {
        var segments = new List<PitchNoteSegment>();
        if (buffer.Channels == 0 || buffer.FrameCount == 0 || lengthBeats <= 0) return segments;

        var events = PolyphonicAudioToMidi.Convert(buffer, lengthBeats, maxPolyphony);
        var frames = buffer.FrameCount;
        var samplesPerBeat = frames / lengthBeats;

        foreach (var ev in events)
        {
            var start = (long)Math.Round(ev.StartBeat * samplesPerBeat);
            var end = (long)Math.Round((ev.StartBeat + ev.LengthBeats) * samplesPerBeat);
            if (end <= start) end = start + 1;
            end = Math.Min(end, frames);
            start = Math.Clamp(start, 0L, frames - 1);

            segments.Add(new PitchNoteSegment
            {
                StartSample = start,
                EndSample = end,
                PitchCents = 0,
                Amplitude = ev.Velocity
            });
        }

        return segments;
    }
}
