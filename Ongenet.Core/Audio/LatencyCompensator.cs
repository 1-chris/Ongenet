using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>
/// Computes per-track and per-bus PDC delays so all paths to the master align at the mix point.
/// </summary>
public static class LatencyCompensator
{
    public sealed class Compensation
    {
        public required int PathLatencySamples { get; init; }
        public required int DelaySamples { get; init; }
    }

    /// <summary>
    /// Returns compensation delay for each content track and bus (keyed by track id).
    /// Each track is delayed by (maxPathLatency - pathLatency).
    /// </summary>
    public static Dictionary<Guid, Compensation> Compute(IReadOnlyList<Track> tracks, int maxBlockFrames = 512)
    {
        var byId = new Dictionary<Guid, Track>(tracks.Count);
        foreach (var t in tracks) byId[t.Id] = t;

        var pathLatency = new Dictionary<Guid, int>();
        var maxLatency = 0;

        foreach (var track in tracks)
        {
            if (IsDirectAudioContentTrack(track))
            {
                var lat = ContentPathLatency(track, byId);
                pathLatency[track.Id] = lat;
                if (lat > maxLatency) maxLatency = lat;
            }
        }

        foreach (var bus in tracks)
        {
            if (!bus.IsBus || bus.Kind == TrackKind.Master) continue;
            var lat = BusPathLatency(bus, byId, pathLatency);
            pathLatency[bus.Id] = lat;
            if (lat > maxLatency) maxLatency = lat;
        }

        // Return tracks fed by sends: path = max(source) + return FX + ancestors.
        foreach (var ret in tracks)
        {
            if (ret.Kind != TrackKind.Return) continue;
            var sourceMax = 0;
            foreach (var t in tracks)
            {
                if (!IsDirectAudioContentTrack(t)) continue;
                foreach (var send in t.Sends)
                {
                    if (!send.Enabled || send.TargetTrackId != ret.Id) continue;
                    if (pathLatency.TryGetValue(t.Id, out var sl) && sl > sourceMax) sourceMax = sl;
                }
            }

            if (sourceMax <= 0) continue;
            var lat = sourceMax + LatencyReporting.BusLatency(ret) + AncestorBusLatency(ret.ParentId, byId);
            pathLatency[ret.Id] = lat;
            if (lat > maxLatency) maxLatency = lat;
        }

        if (tracks.FirstOrDefault(t => t.Kind == TrackKind.Master) is { } master)
        {
            var lat = BusPathLatency(master, byId, pathLatency);
            pathLatency[master.Id] = lat;
            if (lat > maxLatency) maxLatency = lat;
        }

        var result = new Dictionary<Guid, Compensation>();
        foreach (var (id, lat) in pathLatency)
        {
            result[id] = new Compensation
            {
                PathLatencySamples = lat,
                DelaySamples = maxLatency - lat
            };
        }

        return result;
    }

    private static int ContentPathLatency(Track track, Dictionary<Guid, Track> byId)
    {
        var lat = LatencyReporting.TrackContentLatency(track);
        return lat + AncestorBusLatency(track.ParentId, byId);
    }

    private static int BusPathLatency(Track bus, Dictionary<Guid, Track> byId, Dictionary<Guid, int> contentPaths)
    {
        var childMax = 0;
        foreach (var t in byId.Values)
        {
            if (!IsDirectAudioContentTrack(t)) continue;
            if (t.ParentId != bus.Id) continue;
            if (contentPaths.TryGetValue(t.Id, out var cl) && cl > childMax) childMax = cl;
        }

        foreach (var t in byId.Values)
        {
            if (!t.IsBus || t.Kind == TrackKind.Master) continue;
            if (t.ParentId != bus.Id) continue;
            if (contentPaths.TryGetValue(t.Id, out var bl) && bl > childMax) childMax = bl;
        }

        var lat = childMax > 0 ? childMax : LatencyReporting.BusLatency(bus);
        lat += AncestorBusLatency(bus.ParentId, byId);
        return lat;
    }

    private static int AncestorBusLatency(Guid? parentId, Dictionary<Guid, Track> byId)
    {
        var lat = 0;
        var pid = parentId;
        var guard = 0;
        while (pid is { } id && byId.TryGetValue(id, out var parent) && guard++ < 64)
        {
            lat += LatencyReporting.BusLatency(parent);
            pid = parent.ParentId;
        }

        return lat;
    }

    private static bool IsDirectAudioContentTrack(Track track) =>
        !track.IsBus && track.Kind is TrackKind.Audio or TrackKind.Instrument;
}
