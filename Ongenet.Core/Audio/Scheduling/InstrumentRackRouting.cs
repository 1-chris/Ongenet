using System;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Scheduling;

/// <summary>Maps incoming MIDI notes to instrument rack slots (standard vs drum pad grid).</summary>
public static class InstrumentRackRouting
{
    /// <summary>
    /// Returns the slot array to use for a note. Standard racks fan out to all slots; drum pad grids
    /// route to a single slot when the note matches a pad, otherwise null (note is dropped).
    /// </summary>
    public static InstrumentSlot[]? ResolveSlots(Track track, InstrumentSlot[] allSlots, int midiNote)
    {
        if (allSlots.Length == 0) return null;
        if (track.Rack.Kind != RackKind.DrumPadGrid)
            return allSlots;

        foreach (var pad in track.Rack.DrumPads)
        {
            if (pad.MidiNote != midiNote) continue;
            var idx = pad.InstrumentSlotIndex;
            if (idx < 0 || idx >= allSlots.Length) return null;
            return new[] { allSlots[idx] };
        }

        return null;
    }
}
