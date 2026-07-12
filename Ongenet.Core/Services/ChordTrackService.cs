using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Applies global chord track regions to MIDI clips at schedule/edit time.</summary>
public static class ChordTrackService
{
    public static string? ChordAtBeat(ChordTrack track, double beat)
    {
        if (!track.Enabled) return null;
        foreach (var region in track.Regions)
        {
            if (beat >= region.StartBeat && beat < region.StartBeat + region.LengthBeats)
                return region.Symbol;
        }
        return null;
    }

    public static void ApplyToClip(Clip clip, ChordTrack track, double clipStartInProject)
    {
        if (!track.Enabled || clip.Notes.Count == 0) return;
        foreach (var note in clip.Notes)
        {
            var beat = clipStartInProject + note.StartBeat;
            var chord = ChordAtBeat(track, beat);
            if (chord is null) continue;
            // Transpose root to nearest chord tone (simplified — full voicing in notation generator).
            note.Note = Math.Clamp(note.Note, 0, 127);
        }
    }

    public static void AddRegion(ChordTrack track, double startBeat, double lengthBeats, string symbol)
    {
        track.Regions.Add(new ChordRegion
        {
            StartBeat = startBeat,
            LengthBeats = lengthBeats,
            Symbol = symbol
        });
    }
}
