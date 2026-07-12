using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>Operations for linked clip groups and shared content instances.</summary>
public static class ClipLinkOps
{
    public static IEnumerable<Clip> EnumerateLinked(Project project, Clip clip)
    {
        if (clip.LinkedClipGroupId is not { } groupId) yield break;
        foreach (var other in ClipSharingOps.EnumerateClips(project))
            if (other.LinkedClipGroupId == groupId) yield return other;
    }

    public static int LinkedInstanceCount(Project project, Clip clip)
    {
        if (clip.LinkedClipGroupId is null) return 0;
        var count = 0;
        foreach (var _ in EnumerateLinked(project, clip)) count++;
        return count;
    }

    public static void Unlink(Clip clip)
    {
        clip.LinkedClipGroupId = null;
    }

    public static Guid EnsureGroup(Clip source)
    {
        if (source.LinkedClipGroupId is { } existing) return existing;
        var group = Guid.NewGuid();
        source.LinkedClipGroupId = group;
        return group;
    }
}
