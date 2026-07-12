using System;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Shared helpers for creating and editing FL-style pattern tracks.</summary>
public static class PatternTrackHelper
{
    public static Pattern CreatePattern(string name, double lengthBeats = 4, int colorIndex = 0) =>
        new()
        {
            Name = name,
            LengthBeats = lengthBeats,
            ColorIndex = colorIndex
        };

    public static Track CreatePatternTrack(Project project, string? name = null)
    {
        var number = project.Tracks.Count(t => t.Kind == TrackKind.Pattern) + 1;
        var pattern = CreatePattern(name ?? $"Pattern {number}");
        project.Patterns.Add(pattern);

        var track = new Track
        {
            Name = $"Pattern Track {number}",
            Kind = TrackKind.Pattern,
            ColorKey = "CatppuccinPeach",
            ActivePatternId = pattern.Id
        };
        return track;
    }

    public static PatternChannel AddInstrumentRow(Pattern pattern, Track instrumentTrack)
    {
        var row = new PatternChannel
        {
            Order = pattern.Channels.Count,
            SourceKind = PatternRowSourceKind.InstrumentTrack,
            TrackId = instrumentTrack.Id,
            Name = instrumentTrack.Name
        };
        pattern.Channels.Add(row);
        pattern.GetOrCreateSequence(row);
        return row;
    }

    public static PatternChannel AddSampleRow(Pattern pattern, Track samplerTrack, Clip sampleClip, int defaultNote = 60)
    {
        var row = new PatternChannel
        {
            Order = pattern.Channels.Count,
            SourceKind = PatternRowSourceKind.AudioSample,
            TrackId = samplerTrack.Id,
            SampleClipId = sampleClip.Id,
            Name = sampleClip.Name
        };
        pattern.Channels.Add(row);
        var seq = pattern.GetOrCreateSequence(row);
        foreach (var step in seq.Steps)
            step.Note = defaultNote;
        return row;
    }

    public static Pattern? ResolvePattern(Project project, Track? patternTrack, PatternClip? clip)
    {
        if (clip is not null)
            return project.Patterns.FirstOrDefault(p => p.Id == clip.PatternId);
        if (patternTrack is { Kind: TrackKind.Pattern, ActivePatternId: { } id })
            return project.Patterns.FirstOrDefault(p => p.Id == id);
        return null;
    }

    public static Clip? FindClip(Project project, Guid clipId)
    {
        foreach (var track in project.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip is not null) return clip;
        }
        return null;
    }

    public static int NextPatternNumber(Project project) =>
        project.Patterns.Count + 1;
}
