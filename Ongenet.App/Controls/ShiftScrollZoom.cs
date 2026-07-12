using System;
using Avalonia;

namespace Ongenet.App.Controls;

/// <summary>
/// Maps Shift + mouse wheel to the same zoom/pan model as middle-button drag on timeline views:
/// vertical wheel movement zooms around the pointer; horizontal wheel movement pans.
/// </summary>
internal static class ShiftScrollZoom
{
    public const double DragZoomSensitivity = 0.005;
    public const double WheelPixelScale = 10.0;

    public static (double ZoomDelta, double PanDelta) ResolveWheelDeltas(Vector delta)
    {
        var zoomDelta = delta.Y;
        var panDelta = delta.X;
        // macOS remaps Shift + vertical scroll to horizontal delta — treat that as zoom.
        if (Math.Abs(zoomDelta) < 1e-6 && Math.Abs(panDelta) > 1e-6)
        {
            zoomDelta = panDelta;
            panDelta = 0;
        }

        return (zoomDelta, panDelta);
    }

    public static double WheelZoomFactor(double zoomDelta) =>
        Math.Exp(-zoomDelta * DragZoomSensitivity * WheelPixelScale);

    public static void ApplyBeatTimeline(
        double anchorBeat, Point viewportPos, double currentPpb,
        double zoomDelta, double panDelta, double scrollX,
        out double newPpb, out double newScrollX)
    {
        newPpb = currentPpb;
        newScrollX = scrollX;

        if (Math.Abs(zoomDelta) > 1e-6)
            newPpb = currentPpb * WheelZoomFactor(zoomDelta);

        if (Math.Abs(zoomDelta) > 1e-6 || Math.Abs(panDelta) > 1e-6)
            newScrollX = Math.Max(0, anchorBeat * newPpb - viewportPos.X);

        if (Math.Abs(panDelta) > 1e-6)
            newScrollX = Math.Max(0, newScrollX - panDelta * WheelPixelScale);
    }

    public static void ApplySecondsTimeline(
        double anchorSeconds, Point viewportPos, double durationSeconds, double viewportWidth,
        double currentZoomScale, double zoomDelta, double panDelta, double scrollX,
        out double newZoomScale, out double newScrollX)
    {
        newZoomScale = currentZoomScale;
        newScrollX = scrollX;

        if (Math.Abs(zoomDelta) > 1e-6)
            newZoomScale = currentZoomScale * WheelZoomFactor(zoomDelta);

        if (Math.Abs(zoomDelta) > 1e-6 || Math.Abs(panDelta) > 1e-6)
        {
            var width = Math.Max(1, Math.Max(viewportWidth, viewportWidth * newZoomScale));
            var anchorX = durationSeconds > 0 ? anchorSeconds / durationSeconds * width : 0;
            newScrollX = Math.Max(0, anchorX - viewportPos.X);
        }

        if (Math.Abs(panDelta) > 1e-6)
            newScrollX = Math.Max(0, newScrollX - panDelta * WheelPixelScale);
    }

    public static double SecondsContentWidth(double viewportWidth, double zoomScale) =>
        Math.Max(1, Math.Max(viewportWidth, viewportWidth * zoomScale));
}
