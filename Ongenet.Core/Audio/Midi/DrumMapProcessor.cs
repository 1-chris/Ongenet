using System;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Midi;

/// <summary>Applies project drum maps to MIDI note/velocity at playback time.</summary>
public static class DrumMapProcessor
{
    public static (int Note, float Velocity) Apply(Project project, Track track, int note, float velocity)
    {
        if (track.DrumMapId is not { } mapId) return (note, velocity);
        var map = project.DrumMaps.FirstOrDefault(m => m.Id == mapId);
        if (map is null) return (note, velocity);
        var entry = map.Entries.FirstOrDefault(e => e.Note == note);
        if (entry is null) return (note, velocity);
        return (note, Math.Clamp(velocity * entry.VelocityScale, 0f, 1f));
    }

    public static string? LabelFor(Project project, Track track, int note)
    {
        if (track.DrumMapId is not { } mapId) return null;
        var map = project.DrumMaps.FirstOrDefault(m => m.Id == mapId);
        return map?.Entries.FirstOrDefault(e => e.Note == note)?.Label;
    }

    public static Clip? SampleClipFor(Project project, Track track, int note)
    {
        if (track.DrumMapId is not { } mapId) return null;
        var map = project.DrumMaps.FirstOrDefault(m => m.Id == mapId);
        var entry = map?.Entries.FirstOrDefault(e => e.Note == note);
        if (entry?.SampleClipId is not { } clipId) return null;
        foreach (var t in project.Tracks)
        {
            var clip = t.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip is not null) return clip;
        }

        return null;
    }
}
