using System;
using System.IO;
using Ongenet.Core.Models.Media;

namespace Ongenet.Core.Persistence;

internal static class VideoLayerMigration
{
    internal static VideoLayer FromLegacyTrack(Guid id, string filePath, double offsetSeconds, double fps, bool muted,
        double inPointSeconds, double outPointSeconds, Guid? syncClipId, int zOrder)
    {
        var name = string.IsNullOrWhiteSpace(filePath) ? "Video" : Path.GetFileName(filePath);
        var layer = new VideoLayer
        {
            Id = id,
            Name = name,
            ZOrder = zOrder,
            OffsetSeconds = offsetSeconds,
            Fps = fps,
            Muted = muted,
            InPointSeconds = inPointSeconds,
            OutPointSeconds = outPointSeconds,
            SyncClipId = syncClipId
        };
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            layer.Items.Add(new VideoLayerItem
            {
                Kind = VideoElementKind.Video,
                SourcePath = filePath,
                X = 0,
                Y = 0,
                Width = 1,
                Height = 1
            });
        }

        return layer;
    }
}
