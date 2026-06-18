namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Shared grid maths for the timeline + piano roll: picks the finest beat subdivision that
    /// stays at least <see cref="MinPixels"/> apart at the current zoom, so the grid (and snapping)
    /// gets finer as you zoom in.
    /// </summary>
    public static class GridMath
    {
        private const double MinPixels = 10.0;

        // Beat subdivisions, finest first.
        private static readonly double[] Subdivisions = { 0.125, 0.25, 0.5, 1.0 };

        /// <summary>The grid/snap size in beats for the given zoom and bar length.</summary>
        public static double SnapBeats(double pixelsPerBeat, int beatsPerBar)
        {
            if (pixelsPerBeat <= 0) return 1.0;

            foreach (var sub in Subdivisions)
            {
                if (sub * pixelsPerBeat >= MinPixels) return sub;
            }

            // Too zoomed out for a sub-beat grid: fall back to whole bars.
            var bar = beatsPerBar < 1 ? 1 : beatsPerBar;
            return bar;
        }
    }
}
