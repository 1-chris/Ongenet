using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Scheduling;

/// <summary>Shared helpers for expanding clip/pattern MIDI through a track's MIDI-FX chain.</summary>
internal static class MidiFxScheduleHelper
{
    private const double PpqPerBeat = 480.0;

    public static void EmitClipNotes(
        ICollection<ScheduledNoteEvent> output,
        Track track,
        IReadOnlyList<MidiNote> clipNotes,
        double clipStartBeat,
        GrooveTemplate? groove,
        double startBeat,
        InstrumentSlot[]? slots,
        IMidiAwareEffect[] midiAwareFx,
        MidiEffectChain midiFxChain,
        double bpm,
        Project project,
        Func<Track, InstrumentSlot[]?, int, InstrumentSlot[]?>? resolveSlots = null)
    {
        resolveSlots ??= (_, s, _) => s;
        var sources = new List<MidiSourceNote>();
        foreach (var note in clipNotes)
        {
            var onBeat = GrooveMath.Apply(clipStartBeat + note.StartBeat, groove);
            var offBeat = onBeat + note.LengthBeats;
            if (offBeat <= startBeat) continue;
            var (mappedNote, mappedVel) = DrumMapProcessor.Apply(project, track, note.Note, note.Velocity);
            sources.Add(new MidiSourceNote(onBeat, offBeat, mappedNote, mappedVel, note.HumanizeTicks));
        }

        if (sources.Count == 0) return;

        if (midiFxChain.IsEmpty)
        {
            foreach (var src in sources)
            {
                var noteSlots = resolveSlots(track, slots, src.Note);
                if (noteSlots is null) continue;
                if (src.OffBeat <= startBeat) continue;
                output.Add(ToEvent(track.Id, src, noteSlots, midiAwareFx));
            }
            return;
        }

        foreach (var expanded in midiFxChain.ExpandNotes(sources, bpm))
        {
            if (expanded.OffBeat <= startBeat) continue;
            var noteSlots = resolveSlots(track, slots, expanded.Note);
            if (noteSlots is null) continue;
            output.Add(ToEvent(track.Id, expanded, noteSlots, midiAwareFx));
        }
    }

    public static ScheduledNoteEvent ToEvent(
        Guid trackId,
        MidiExpandedNote note,
        InstrumentSlot[]? slots,
        IMidiAwareEffect[] midiAwareFx,
        float gain = 1f,
        float pan = 0f)
        => new(trackId, note.OnBeat, note.OffBeat, slots, midiAwareFx, note.Note, note.Velocity, gain, pan,
            note.TimingOffsetBeats, note.PitchBend14);

    public static ScheduledNoteEvent ToEvent(
        Guid trackId,
        MidiSourceNote src,
        InstrumentSlot[]? slots,
        IMidiAwareEffect[] midiAwareFx,
        float gain = 1f,
        float pan = 0f)
    {
        var timing = src.HumanizeTicks == 0 ? 0 : src.HumanizeTicks / PpqPerBeat;
        return new(trackId, src.OnBeat, src.OffBeat, slots, midiAwareFx, src.Note, src.Velocity, gain, pan, timing);
    }
}
