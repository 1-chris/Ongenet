using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Midi;

/// <summary>Converts between step-sequencer data and piano-roll MIDI notes.</summary>
public static class StepPatternConverter
{
    public static IReadOnlyList<MidiNote> ToNotes(StepSequence sequence, double patternLengthBeats)
    {
        var stepCount = EffectiveStepCount(sequence);
        if (stepCount <= 0) return Array.Empty<MidiNote>();

        var stepBeats = patternLengthBeats / stepCount;
        var notes = new List<MidiNote>();
        for (var i = 0; i < sequence.Steps.Count && i < stepCount; i++)
        {
            var step = sequence.Steps[i];
            if (!step.Active) continue;
            notes.Add(new MidiNote
            {
                Note = step.Note,
                StartBeat = i * stepBeats,
                LengthBeats = stepBeats,
                Velocity = step.Velocity
            });
        }

        return notes;
    }

    public static void FromNotes(IReadOnlyList<MidiNote> notes, StepSequence sequence, double patternLengthBeats)
    {
        var stepCount = EffectiveStepCount(sequence);
        if (stepCount <= 0) return;

        EnsureStepCount(sequence, stepCount);
        var stepBeats = patternLengthBeats / stepCount;

        foreach (var step in sequence.Steps)
            step.Active = false;

        foreach (var note in notes)
        {
            if (stepBeats <= 0) continue;
            var idx = (int)Math.Round(note.StartBeat / stepBeats);
            if (idx < 0 || idx >= stepCount) continue;
            var step = sequence.Steps[idx];
            step.Active = true;
            step.Note = note.Note;
            step.Velocity = note.Velocity;
        }
    }

    private static int EffectiveStepCount(StepSequence sequence)
        => Math.Max(1, sequence.StepCount > 0 ? sequence.StepCount : sequence.Steps.Count);

    private static void EnsureStepCount(StepSequence sequence, int count)
    {
        while (sequence.Steps.Count < count)
            sequence.Steps.Add(new StepData());
        sequence.StepCount = count;
    }
}
