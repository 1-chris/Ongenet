using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>Counts how many clips share the same audio buffer or MIDI note list.</summary>
public static class ClipSharingOps
{
    public static IEnumerable<Clip> EnumerateClips(Project project)
    {
        foreach (var track in project.Tracks)
            foreach (var clip in track.Clips)
                yield return clip;
    }

    public static int CountSharingSamples(Project project, AudioSampleBuffer? buffer)
    {
        if (buffer is null) return 0;
        var count = 0;
        foreach (var clip in EnumerateClips(project))
            if (clip.IsAudio && ReferenceEquals(clip.Samples, buffer)) count++;
        return count;
    }

    public static int CountSharingNotes(Project project, IReadOnlyList<MidiNote>? notes)
    {
        if (notes is null || notes is not List<MidiNote> list) return 0;
        var count = 0;
        foreach (var clip in EnumerateClips(project))
            if (clip.IsMidi && ReferenceEquals(clip.Notes, list)) count++;
        return count;
    }

    public static int SharedInstanceCount(Project project, Clip clip)
    {
        if (clip.IsAudio) return CountSharingSamples(project, clip.Samples);
        if (clip.IsMidi) return CountSharingNotes(project, clip.Notes);
        return 0;
    }
}
