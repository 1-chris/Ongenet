using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Shared clip helpers for scripting and batch edits.</summary>
public static class ScriptClipOperations
{
    public static (Track Track, Clip Clip)? FindClip(Project project, Guid clipId)
    {
        foreach (var track in project.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip is not null)
                return (track, clip);
        }

        return null;
    }

    public static Clip DuplicateClip(Clip src)
    {
        var copy = new Clip
        {
            Name = src.Name,
            StartBeat = src.StartBeat,
            LengthBeats = src.LengthBeats,
            IsAudio = src.IsAudio,
            StretchToTempo = src.StretchToTempo,
            PitchCorrected = src.PitchCorrected,
            SourceTempo = src.SourceTempo,
            SourceKey = src.SourceKey,
            AudioFilePath = src.AudioFilePath,
            Waveform = src.Waveform,
            Samples = src.Samples,
            SourceOffsetSeconds = src.SourceOffsetSeconds,
            SourceLengthSeconds = src.SourceLengthSeconds,
            WarpMode = src.WarpMode,
            UserFadeInBeats = src.UserFadeInBeats,
            UserFadeOutBeats = src.UserFadeOutBeats,
            HasAraRegion = src.HasAraRegion,
            AraPitchOffsetSemitones = src.AraPitchOffsetSemitones
        };

        foreach (var ps in src.PitchSegments)
        {
            copy.PitchSegments.Add(new PitchNoteSegment
            {
                StartSample = ps.StartSample,
                EndSample = ps.EndSample,
                PitchCents = ps.PitchCents,
                Amplitude = ps.Amplitude
            });
        }

        foreach (var wm in src.WarpMarkers)
            copy.WarpMarkers.Add(new WarpMarker { SourceSeconds = wm.SourceSeconds, BeatPosition = wm.BeatPosition });

        if (src.IsAudio)
            return copy;

        copy.Notes = src.Notes.Select(n => new MidiNote
        {
            Note = n.Note,
            StartBeat = n.StartBeat,
            LengthBeats = n.LengthBeats,
            Velocity = n.Velocity,
            HumanizeTicks = n.HumanizeTicks,
            Chance = n.Chance,
            NoteGroupId = n.NoteGroupId
        }).ToList();

        return copy;
    }

    public static ScriptClipInfo ToInfo(Track track, Clip clip) =>
        new(clip.Id, track.Id, clip.Name, clip.StartBeat, clip.LengthBeats, clip.IsAudio, clip.Notes.Count);

    public static IEnumerable<(Track Track, Clip Clip)> EnumerateClips(Project project, Guid? trackId)
    {
        foreach (var track in project.Tracks)
        {
            if (trackId is { } id && track.Id != id)
                continue;

            foreach (var clip in track.Clips)
                yield return (track, clip);
        }
    }
}
