using System;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;

namespace Ongenet.VideoComposition.Rendering;

/// <summary>Interpolates layer item transforms from beat keyframes.</summary>
public static class VideoKeyframeInterpolator
{
    public static (double X, double Y, double Width, double Height, double Opacity) Resolve(
        VideoLayerItem item, Project project, double beat)
    {
        var keyframes = project.VideoLayerKeyframes
            .Where(k => k.ItemId == item.Id)
            .OrderBy(k => k.Beat)
            .ToList();
        if (keyframes.Count == 0)
            return (item.X, item.Y, item.Width, item.Height, item.Opacity);

        if (beat <= keyframes[0].Beat)
            return (keyframes[0].X, keyframes[0].Y, keyframes[0].Width, keyframes[0].Height, keyframes[0].Opacity);

        for (var i = 1; i < keyframes.Count; i++)
        {
            var prev = keyframes[i - 1];
            var next = keyframes[i];
            if (beat > next.Beat) continue;
            var t = (beat - prev.Beat) / Math.Max(1e-6, next.Beat - prev.Beat);
            return (
                Lerp(prev.X, next.X, t),
                Lerp(prev.Y, next.Y, t),
                Lerp(prev.Width, next.Width, t),
                Lerp(prev.Height, next.Height, t),
                Lerp(prev.Opacity, next.Opacity, t));
        }

        var last = keyframes[^1];
        return (last.X, last.Y, last.Width, last.Height, last.Opacity);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);
}
