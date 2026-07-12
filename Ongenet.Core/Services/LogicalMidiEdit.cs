using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Bulk MIDI transform operations (logical edit lite).</summary>
public static class LogicalMidiEdit
{
    public static void TransposeClip(Clip clip, int semitones)
    {
        foreach (var note in clip.Notes)
            note.Note = Math.Clamp(note.Note + semitones, 0, 127);
    }

    public static void ScaleVelocity(Clip clip, double factor)
    {
        foreach (var note in clip.Notes)
            note.Velocity = (float)Math.Clamp(note.Velocity * factor, 0, 1);
    }

    public static void QuantizeClip(Clip clip, double gridBeats)
    {
        if (gridBeats <= 0) return;
        foreach (var note in clip.Notes)
        {
            note.StartBeat = Math.Round(note.StartBeat / gridBeats) * gridBeats;
            note.LengthBeats = Math.Max(gridBeats * 0.25, Math.Round(note.LengthBeats / gridBeats) * gridBeats);
        }
    }

    public static void DeleteNotesInRange(Clip clip, double startBeat, double endBeat)
    {
        clip.Notes.RemoveAll(n => n.StartBeat >= startBeat && n.EndBeat <= endBeat);
    }

    public static void AddControlChange(Clip clip, int controller, int value, double startBeat, double lengthBeats = 0.25)
    {
        clip.ControlChanges.Add(new MidiControlChange
        {
            Controller = controller,
            Value = Math.Clamp(value, 0, 127),
            StartBeat = startBeat,
            LengthBeats = lengthBeats
        });
    }

    /// <summary>Randomizes note start times within ±<paramref name="maxTicks"/> PPQ ticks.</summary>
    public static void HumanizeClip(Clip clip, int maxTicks, Random? rng = null)
    {
        rng ??= Random.Shared;
        foreach (var note in clip.Notes)
            note.HumanizeTicks = rng.Next(-maxTicks, maxTicks + 1);
    }

    /// <summary>Sets note playback chance for all notes in the clip.</summary>
    public static void ApplyChance(Clip clip, float chance)
    {
        chance = Math.Clamp(chance, 0f, 1f);
        foreach (var note in clip.Notes)
            note.Chance = chance;
    }

    /// <summary>Groups selected notes under a shared <see cref="MidiNote.NoteGroupId"/>.</summary>
    public static Guid GroupNotes(Clip clip, IEnumerable<MidiNote> notes)
    {
        var id = Guid.NewGuid();
        foreach (var note in notes)
            note.NoteGroupId = id;
        return id;
    }

    public static IEnumerable<MidiNote> NotesMatching(Clip clip, Func<MidiNote, bool> predicate) =>
        clip.Notes.Where(predicate);
}
