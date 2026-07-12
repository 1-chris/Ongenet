using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Scheduling;

/// <summary>Resolves which clips on a track are audible during comp playback.</summary>
internal static class TakeLanePlayback
{
    /// <summary>
    /// Returns clips that should play: non-comp clips always play; comp clips only when their take is
    /// selected in a take lane.
    /// </summary>
    public static IEnumerable<Clip> ActiveClips(Track track)
    {
        if (track.TakeLanes.Count == 0) return track.Clips;

        var compClipIds = new HashSet<Guid>();
        var selectedClipIds = new HashSet<Guid>();
        foreach (var lane in track.TakeLanes)
        {
            foreach (var take in lane.Takes)
            {
                compClipIds.Add(take.ClipId);
                if (take.IsSelected) selectedClipIds.Add(take.ClipId);
            }
        }

        return track.Clips.Where(c => !compClipIds.Contains(c.Id) || selectedClipIds.Contains(c.Id));
    }
}
