using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>
/// Describes a beat-bounded region of the arrangement to offline-render through the full effect chain,
/// stopping before master FX. Used by "Render clip to new track".
/// </summary>
public sealed class ClipRenderScope
{
    public required double StartBeat { get; init; }
    public required double LengthBeats { get; init; }
    public required IReadOnlyDictionary<Track, IReadOnlyList<Clip>> ContentByTrack { get; init; }

    /// <summary>
    /// When set, output is read from this group's bus after its FX (and any outer ancestor group FX).
    /// When null, output is read from the sole content track's post-FX buffer (ungrouped clip).
    /// </summary>
    public Guid? TapAfterGroupId { get; init; }

    /// <summary>External tracks that must render for sidechain taps but are not in <see cref="ContentByTrack"/>.</summary>
    public required HashSet<Guid> SidechainSourceIds { get; init; }

    public double EndBeat => StartBeat + LengthBeats;

    /// <summary>Builds a scope for a single clip on <paramref name="owner"/>.</summary>
    public static ClipRenderScope ForClip(Project project, Track owner, Clip clip)
    {
        var content = new Dictionary<Track, IReadOnlyList<Clip>> { [owner] = new[] { clip } };
        var tapGroupId = TopmostGroupAncestor(project, owner.ParentId);
        var sidechain = CollectSidechainSources(project, content.Keys, tapGroupId);
        return new ClipRenderScope
        {
            StartBeat = clip.StartBeat,
            LengthBeats = clip.LengthBeats,
            ContentByTrack = content,
            TapAfterGroupId = tapGroupId,
            SidechainSourceIds = sidechain
        };
    }

    /// <summary>Builds a scope for a group summary spanning descendant content.</summary>
    public static ClipRenderScope ForGroup(Project project, Track group, double startBeat, double lengthBeats,
        IEnumerable<Track> descendantTracks)
    {
        var scopeEnd = startBeat + lengthBeats;
        var content = new Dictionary<Track, IReadOnlyList<Clip>>();
        foreach (var track in descendantTracks)
        {
            var clips = track.Clips
                .Where(c => c.StartBeat < scopeEnd && c.EndBeat > startBeat)
                .ToList();
            if (clips.Count > 0) content[track] = clips;
        }

        // Nested group: tap after parent group FX; top-level group: tap after this group's FX.
        var tapGroupId = group.ParentId ?? group.Id;
        var sidechain = CollectSidechainSources(project, content.Keys, tapGroupId, group.Id);
        return new ClipRenderScope
        {
            StartBeat = startBeat,
            LengthBeats = lengthBeats,
            ContentByTrack = content,
            TapAfterGroupId = tapGroupId,
            SidechainSourceIds = sidechain
        };
    }

    private static HashSet<Guid> CollectSidechainSources(Project project,
        IEnumerable<Track> contentTracks, Guid? tapGroupId, Guid? includeGroupId = null)
    {
        var relevant = new HashSet<Guid>();
        foreach (var t in contentTracks) relevant.Add(t.Id);
        if (tapGroupId is { } gid) relevant.Add(gid);
        if (includeGroupId is { } ig) relevant.Add(ig);

        var trackById = project.Tracks.ToDictionary(t => t.Id);
        foreach (var track in contentTracks)
        {
            var pid = track.ParentId;
            var guard = 0;
            while (pid is { } pgid && guard++ < 64)
            {
                relevant.Add(pgid);
                if (tapGroupId is not null && pgid == tapGroupId) break;
                pid = trackById.GetValueOrDefault(pgid)?.ParentId;
            }
        }

        var needed = new HashSet<Guid>();
        foreach (var track in project.Tracks)
        {
            if (!relevant.Contains(track.Id)) continue;
            CollectFromEffects(track.Effects, relevant, needed);
            foreach (var slot in track.Instruments)
                CollectFromEffects(slot.Effects, relevant, needed);
        }

        return needed;
    }

    private static void CollectFromEffects(List<IAudioEffect> effects, HashSet<Guid> relevant, HashSet<Guid> needed)
    {
        foreach (var fx in effects)
        {
            if (fx is ISourceTrackEffect { SourceTrackId: { } id } && id != Guid.Empty && !relevant.Contains(id))
                needed.Add(id);
        }
    }

    private static Guid? TopmostGroupAncestor(Project project, Guid? parentId)
    {
        if (parentId is not { } id) return null;
        var trackById = project.Tracks.ToDictionary(t => t.Id);
        Guid? topmost = null;
        var cur = trackById.GetValueOrDefault(id);
        var guard = 0;
        while (cur is { Kind: TrackKind.Group } && guard++ < 64)
        {
            topmost = cur.Id;
            cur = cur.ParentId is { } pid ? trackById.GetValueOrDefault(pid) : null;
        }

        return topmost;
    }
}
