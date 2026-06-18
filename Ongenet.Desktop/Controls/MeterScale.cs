using System;

namespace Ongenet.Desktop.Controls
{
    /// <summary>Shared decibel mapping for the level meters.</summary>
    internal static class MeterScale
    {
        public const double MinDb = -60.0;
        public const double MaxDb = 6.0;

        /// <summary>dB values to draw tick lines at on the master meter.</summary>
        public static readonly double[] Ticks = { 0, -6, -12, -24, -48 };

        /// <summary>Maps a linear level (0..1+) to a normalised 0..1 position on the dB scale.</summary>
        public static double Normalize(double level)
        {
            if (level <= 0) return 0;
            var db = 20.0 * Math.Log10(level);
            return NormalizeDb(db);
        }

        /// <summary>Maps a dB value to a normalised 0..1 position on the scale.</summary>
        public static double NormalizeDb(double db)
        {
            var n = (db - MinDb) / (MaxDb - MinDb);
            return n < 0 ? 0 : n > 1 ? 1 : n;
        }
    }
}
