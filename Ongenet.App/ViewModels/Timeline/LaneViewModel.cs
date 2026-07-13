using System;

namespace Ongenet.App.ViewModels.Timeline
{
    /// <summary>
    /// Base for a row shown in the arrange view. Rows are polymorphic: a <see cref="TrackLaneViewModel"/>
    /// is a normal track lane, an <see cref="AutomationLaneViewModel"/> is an indented automation curve
    /// belonging to the track above it. Rows can differ in height, so the timeline lays them out with
    /// cumulative-height arithmetic rather than a fixed row stride.
    /// </summary>
    public abstract class LaneViewModel : ViewModelBase
    {
        /// <summary>Magnetic snap zone around half/default heights, in pixels.</summary>
        public const double SnapZone = 4.0;

        /// <summary>Default row height for this lane type, in pixels.</summary>
        public abstract double DefaultHeight { get; }

        /// <summary>Half of <see cref="DefaultHeight"/> — the minimum user-resizable height.</summary>
        public double HalfHeight => DefaultHeight / 2.0;

        /// <summary>Row height in pixels (lane content and header share it).</summary>
        public abstract double Height { get; }

        /// <summary>When true the header uses a single compact row (name + controls inline).</summary>
        public virtual bool IsCompact => Height <= DefaultHeight * 0.75;

        /// <summary>Whether the user can drag-resize this row's bottom edge.</summary>
        public virtual bool SupportsResize => Height > 0;

        /// <summary>Resolves a stored height (0 = default) to pixels.</summary>
        public static double ResolveHeight(double stored, double defaultHeight)
            => stored <= 0 ? defaultHeight : stored;

        /// <summary>
        /// Snaps a raw drag height to half, default, or free (above default). Heights below half clamp to half.
        /// </summary>
        public static double SnapHeight(double raw, double half, double def)
        {
            if (raw < half) return half;
            if (Math.Abs(raw - half) <= SnapZone) return half;
            if (raw < def - SnapZone)
                return raw < (half + def) / 2 ? half : def;
            if (Math.Abs(raw - def) <= SnapZone) return def;
            return raw;
        }

        /// <summary>Applies a snapped height and persists it on the underlying model.</summary>
        public abstract void SetHeight(double height);
    }
}
