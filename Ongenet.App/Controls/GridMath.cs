namespace Ongenet.App.Controls
{
    /// <summary>
    /// Shared grid maths for the timeline + piano roll: picks the finest beat subdivision that
    /// stays at least <see cref="MinPixels"/> apart at the current zoom, so the grid (and snapping)
    /// gets finer as you zoom in.
    /// </summary>
    public static class GridMath
    {
        private const double MinPixels = 10.0;

        // Beat subdivisions, finest first. The finer entries only kick in at deep zoom (each still has to
        // clear MinPixels), giving sample-accurate snapping/grid down to 1/64 of a beat when zoomed right in.
        private static readonly double[] Subdivisions =
            { 1.0 / 256, 1.0 / 128, 1.0 / 64, 1.0 / 32, 1.0 / 16, 0.125, 0.25, 0.5, 1.0 };

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

        /// <summary>
        /// Bar stride for ruler labels: 1 = every bar, 2 = every other bar, etc. Doubles until
        /// consecutive labels are at least <see cref="MinBarLabelSpacingPx"/> apart.
        /// </summary>
        public static int BarLabelStride(double pixelsPerBeat, int beatsPerBar)
        {
            if (pixelsPerBeat <= 0) return 1;

            var barPx = (beatsPerBar < 1 ? 1 : beatsPerBar) * pixelsPerBeat;
            if (barPx <= 0) return 1;

            var stride = 1;
            while (barPx * stride < MinBarLabelSpacingPx && stride < 1_048_576)
                stride *= 2;
            return stride;
        }

        /// <summary>Minimum horizontal gap between ruler bar numbers, in pixels.</summary>
        public const double MinBarLabelSpacingPx = 44.0;
    }
}
