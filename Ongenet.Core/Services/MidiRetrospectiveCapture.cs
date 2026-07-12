using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>
/// Ring buffer of recent live MIDI input for retrospective capture ("capture last played notes").
/// Events are timestamped with a monotonic clock and converted to clip-local beats when flushed.
/// </summary>
public sealed class MidiRetrospectiveCapture
{
    private const double MinNoteBeats = 1.0 / 64.0;

    private readonly object _lock = new();
    private readonly Dictionary<int, OpenNote> _open = new();
    private readonly List<CapturedNote> _completed = new();
    private readonly Queue<long> _eventTimes = new();

    /// <summary>How far back retrospective capture searches for notes.</summary>
    public TimeSpan LookbackWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Records a raw MIDI message from a hardware input port.</summary>
    public void Record(in MidiMessage message)
    {
        switch (message.Kind)
        {
            case MidiMessageKind.NoteOn when message.Velocity > 0:
                RecordNoteOn(message.Note, message.Velocity / 127f);
                break;
            case MidiMessageKind.NoteOff:
            case MidiMessageKind.NoteOn:
                RecordNoteOff(message.Note);
                break;
        }
    }

    /// <summary>Records a preview note-on (keyboard / on-screen piano).</summary>
    public void RecordNoteOn(int midiNote, float velocity)
    {
        var now = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            TrimOldEvents(now);
            _open[midiNote] = new OpenNote(midiNote, velocity, now);
            _eventTimes.Enqueue(now);
        }
    }

    /// <summary>Records a preview note-off.</summary>
    public void RecordNoteOff(int midiNote)
    {
        var now = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            TrimOldEvents(now);
            if (!_open.Remove(midiNote, out var open)) return;
            _completed.Add(new CapturedNote(open.Note, open.Velocity, open.StartTicks, now));
            _eventTimes.Enqueue(now);
        }
    }

    /// <summary>
    /// Builds clip-relative MIDI notes ending at <paramref name="playheadBeat"/>. Still-held notes
    /// are closed at the playhead.
    /// </summary>
    public List<MidiNote> BuildClipNotes(double playheadBeat, double bpm)
    {
        if (bpm <= 0) bpm = 120;
        var now = Stopwatch.GetTimestamp();
        var notes = new List<CapturedNote>();
        lock (_lock)
        {
            TrimOldEvents(now);
            notes.AddRange(_completed);
            foreach (var open in _open.Values)
                notes.Add(new CapturedNote(open.Note, open.Velocity, open.StartTicks, now));
        }

        if (notes.Count == 0) return new List<MidiNote>();

        var latestTicks = notes[^1].EndTicks;
        var result = new List<MidiNote>();
        foreach (var note in notes)
        {
            var endOffsetSec = TicksToSeconds(latestTicks - note.EndTicks);
            var startOffsetSec = TicksToSeconds(latestTicks - note.StartTicks);
            var endBeat = playheadBeat - endOffsetSec * bpm / 60.0;
            var startBeat = playheadBeat - startOffsetSec * bpm / 60.0;
            var length = endBeat - startBeat;
            if (length < MinNoteBeats) length = MinNoteBeats;
            if (endBeat <= 0) continue;
            result.Add(new MidiNote
            {
                Note = note.Note,
                StartBeat = Math.Max(0, startBeat),
                LengthBeats = length,
                Velocity = note.Velocity
            });
        }

        result.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
        return result;
    }

    /// <summary>Clears buffered notes after a successful capture.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _open.Clear();
            _completed.Clear();
            _eventTimes.Clear();
        }
    }

    private void TrimOldEvents(long nowTicks)
    {
        var windowTicks = (long)(LookbackWindow.TotalSeconds * Stopwatch.Frequency);
        while (_eventTimes.Count > 0 && nowTicks - _eventTimes.Peek() > windowTicks)
            _eventTimes.Dequeue();

        var cutoff = nowTicks - windowTicks;
        _completed.RemoveAll(n => n.EndTicks < cutoff);
        var stale = new List<int>();
        foreach (var (note, open) in _open)
            if (open.StartTicks < cutoff) stale.Add(note);
        foreach (var note in stale) _open.Remove(note);
    }

    private static double TicksToSeconds(long ticks)
        => ticks <= 0 ? 0 : ticks / (double)Stopwatch.Frequency;

    private readonly record struct OpenNote(int Note, float Velocity, long StartTicks);

    private readonly record struct CapturedNote(int Note, float Velocity, long StartTicks, long EndTicks);
}
