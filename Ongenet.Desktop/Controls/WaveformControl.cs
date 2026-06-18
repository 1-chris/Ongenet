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

        static WaveformControl()
        {
            AffectsRender<WaveformControl>(WaveformProperty, FillProperty, RevisionProperty);
        }

        public int Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
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
            var mid = height / 2.0;
            var scale = mid * 0.92; // small margin so peaks don't touch the edges
            var columns = (int)Math.Ceiling(width);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, mid), isFilled: true);

                // Top contour, left to right.
                for (var x = 0; x < columns; x++)
                {
                    PeakAt(waveform, x, width, out _, out var max);
                    ctx.LineTo(new Point(x, mid - max * scale));
                }

                // Bottom contour, right to left, closing the filled shape.
                for (var x = columns - 1; x >= 0; x--)
                {
                    PeakAt(waveform, x, width, out var min, out _);
                    ctx.LineTo(new Point(x, mid - min * scale));
                }

                ctx.EndFigure(true);
            }

            context.DrawGeometry(brush, null, geometry);
        }

        private static void PeakAt(AudioWaveform waveform, int column, double width, out float min, out float max)
        {
            var frameStart = (long)(column / width * waveform.TotalFrames);
            var frameEnd = (long)((column + 1) / width * waveform.TotalFrames);
            waveform.GetPeak(frameStart, frameEnd, out min, out max);
        }
    }
}
