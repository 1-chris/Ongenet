using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio;

/// <summary>Routes additional plugin output buses to destination tracks.</summary>
public sealed class MultiOutputRoute
{
    public Guid SourceTrackId { get; set; }
    public int SlotIndex { get; set; }
    public int PluginOutputBus { get; set; }
    public Guid DestinationTrackId { get; set; }
    public double Level { get; set; } = 1.0;
}

public static class MultiOutputRouter
{
    public static Dictionary<(Guid TrackId, int BusIndex), Guid> BuildIndex(IReadOnlyList<MultiOutputRoute> routes)
    {
        var map = new Dictionary<(Guid, int), Guid>();
        foreach (var r in routes)
            map[(r.SourceTrackId, r.PluginOutputBus)] = r.DestinationTrackId;
        return map;
    }
}
