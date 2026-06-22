using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Draws an audio clip's waveform by reading min/max peaks from an <see cref="AudioWaveform"/>
    /// across its own pixel width. Rendering cost is proportional to the control's width (one
    /// peak query per column), not the file length — this is the custom-render path the timeline's
    /// <c>TimelineMetrics</c> seam was designed to enable.
    /// </summary>
    public sealed class WaveformControl : Control
    {
        /// <summary>The peaks to draw. Null renders nothing.</summary>
        public static readonly StyledProperty<AudioWaveform?> WaveformProperty =
            AvaloniaProperty.Register<WaveformControl, AudioWaveform?>(nameof(Waveform));

        /// <summary>Brush used to fill the waveform body.</summary>
        public static readonly StyledProperty<IBrush?> FillProperty =
            AvaloniaProperty.Register<WaveformControl, IBrush?>(nameof(Fill));

        /// <summary>
        /// Bumped to force a repaint when the bound <see cref="AudioWaveform"/> grows in place (e.g.
        /// while recording) — the property reference doesn't change, so we need an explicit trigger.
        /// </summary>
        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<WaveformControl, int>(nameof(Revision));

        /// <summary>Fraction of the source (0..1) at which the drawn window begins. Non-zero for a sliced clip.</summary>
        public static readonly StyledProperty<double> StartFractionProperty =
            AvaloniaProperty.Register<WaveformControl, double>(nameof(StartFraction));

        /// <summary>Fraction of the source (0..1) at which the drawn window ends. Defaults to the whole source.</summary>
        public static readonly StyledProperty<double> EndFractionProperty =
            AvaloniaProperty.Register<WaveformControl, double>(nameof(EndFraction), 1.0);

        static WaveformControl()
        {
            AffectsRender<WaveformControl>(WaveformProperty, FillProperty, RevisionProperty,
                StartFractionProperty, EndFractionProperty);
        }

        public int Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        public double StartFraction
        {
            get => GetValue(StartFractionProperty);
            set => SetValue(StartFractionProperty, value);
        }

        public double EndFraction
        {
            get => GetValue(EndFractionProperty);
            set => SetValue(EndFractionProperty, value);
        }

        public AudioWaveform? Waveform
        {
            get => GetValue(WaveformProperty);
            set => SetValue(WaveformProperty, value);
        }

        public IBrush? Fill
        {
            get => GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            var waveform = Waveform;
            if (waveform is null || waveform.TotalFrames <= 0) return;

            var width = Bounds.Width;
            var height = Bounds.Height;
            if (width < 1 || height < 1) return;

            var brush = Fill ?? Brushes.Black;
            context.DrawGeometry(brush, null,
                BuildGeometry(waveform, 0, width, height, StartFraction, EndFraction));
        }

        /// <summary>
        /// Builds the filled min/max waveform silhouette for <paramref name="waveform"/> across
        /// <paramref name="regionWidth"/> px starting at <paramref name="x0"/>, vertically centred in
        /// <paramref name="height"/>. Shared by the clip waveform and the crossfade overlap preview so they
        /// look identical. <paramref name="startFraction"/>/<paramref name="endFraction"/> window the source.
        /// </summary>
        public static StreamGeometry BuildGeometry(AudioWaveform waveform, double x0, double regionWidth,
            double height, double startFraction, double endFraction)
        {
            var mid = height / 2.0;
            var scale = mid * 0.92; // small margin so peaks don't touch the edges
            var columns = (int)Math.Ceiling(regionWidth);

            // The window of the source drawn: [start, start+span] as fractions of the source (whole buffer = 0..1).
            var start = Math.Clamp(startFraction, 0.0, 1.0);
            var span = Math.Clamp(endFraction, 0.0, 1.0) - start;
            if (span <= 0) span = 1.0 - start;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x0, mid), isFilled: true);

                // Top contour, left to right.
                for (var x = 0; x < columns; x++)
                {
                    PeakAt(waveform, x, regionWidth, start, span, out _, out var max);
                    ctx.LineTo(new Point(x0 + x, mid - max * scale));
                }

                // Bottom contour, right to left, closing the filled shape.
                for (var x = columns - 1; x >= 0; x--)
                {
                    PeakAt(waveform, x, regionWidth, start, span, out var min, out _);
                    ctx.LineTo(new Point(x0 + x, mid - min * scale));
                }

                ctx.EndFigure(true);
            }

            return geometry;
        }

        private static void PeakAt(AudioWaveform waveform, int column, double width,
            double start, double span, out float min, out float max)
        {
            var frameStart = (long)((start + column / width * span) * waveform.TotalFrames);
            var frameEnd = (long)((start + (column + 1) / width * span) * waveform.TotalFrames);
            waveform.GetPeak(frameStart, frameEnd, out min, out max);
        }
    }
}
