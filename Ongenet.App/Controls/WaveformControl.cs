using System;
using Avalonia;
using Avalonia.Media;
using Ongenet.App.Display;
using Ongenet.App.Theming;
using Ongenet.Core.Audio.Files;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// Draws an audio clip's waveform by reading min/max peaks from an <see cref="AudioWaveform"/>
    /// across its own pixel width. Rendering cost is proportional to the control's width (one
    /// peak query per column), not the file length — this is the custom-render path the timeline's
    /// <c>TimelineMetrics</c> seam was designed to enable.
    ///
    /// When <see cref="WaveformDisplayPreferences.BandColorsEnabled"/> is true, bass/mid/treble bands
    /// are drawn in theme colours using peaks precomputed at build time (no extra per-frame cost).
    /// </summary>
    public sealed class WaveformControl : ThemedControl
    {
        /// <summary>The peaks to draw. Null renders nothing.</summary>
        public static readonly StyledProperty<AudioWaveform?> WaveformProperty =
            AvaloniaProperty.Register<WaveformControl, AudioWaveform?>(nameof(Waveform));

        /// <summary>Brush used when band colours are disabled in Settings.</summary>
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

        private IBrush _bassFill = Brushes.Blue;
        private IBrush _midFill = Brushes.Green;
        private IBrush _trebleFill = Brushes.PeachPuff;

        static WaveformControl()
        {
            AffectsRender<WaveformControl>(WaveformProperty, FillProperty,
                RevisionProperty, StartFractionProperty, EndFractionProperty);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            WaveformDisplayPreferences.Changed += OnWaveformDisplayChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            WaveformDisplayPreferences.Changed -= OnWaveformDisplayChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnWaveformDisplayChanged() => InvalidateVisual();

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

        protected override void BuildThemeResources()
        {
            _bassFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Sapphire, 210));
            _midFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Green, 200));
            _trebleFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Peach, 210));
        }

        public override void Render(DrawingContext context)
        {
            var waveform = Waveform;
            if (waveform is null || waveform.TotalFrames <= 0) return;

            var width = Bounds.Width;
            var height = Bounds.Height;
            if (width < 1 || height < 1) return;

            Draw(context, waveform, 0, width, height, StartFraction, EndFraction,
                WaveformDisplayPreferences.BandColorsEnabled, Fill, _bassFill, _midFill, _trebleFill);
        }

        /// <summary>
        /// Draws a waveform region. Shared by clip waveforms, the sample editor, and crossfade previews.
        /// </summary>
        public static void Draw(DrawingContext context, AudioWaveform waveform,
            double x0, double regionWidth, double height,
            double startFraction, double endFraction,
            bool bandColors, IBrush? singleFill,
            IBrush bassFill, IBrush midFill, IBrush trebleFill)
        {
            if (bandColors && waveform.HasBandPeaks)
            {
                context.DrawGeometry(bassFill, null,
                    BuildGeometry(waveform, x0, regionWidth, height, startFraction, endFraction, WaveformBand.Bass));
                context.DrawGeometry(midFill, null,
                    BuildGeometry(waveform, x0, regionWidth, height, startFraction, endFraction, WaveformBand.Mid));
                context.DrawGeometry(trebleFill, null,
                    BuildGeometry(waveform, x0, regionWidth, height, startFraction, endFraction, WaveformBand.Treble));
                return;
            }

            context.DrawGeometry(singleFill ?? Brushes.Black, null,
                BuildGeometry(waveform, x0, regionWidth, height, startFraction, endFraction));
        }

        /// <summary>
        /// Builds the filled min/max waveform silhouette for <paramref name="waveform"/> across
        /// <paramref name="regionWidth"/> px starting at <paramref name="x0"/>, vertically centred in
        /// <paramref name="height"/>. Shared by the clip waveform and the crossfade overlap preview so they
        /// look identical. <paramref name="startFraction"/>/<paramref name="endFraction"/> window the source.
        /// </summary>
        public static StreamGeometry BuildGeometry(AudioWaveform waveform, double x0, double regionWidth,
            double height, double startFraction, double endFraction, WaveformBand? band = null)
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
                    PeakAt(waveform, band, x, regionWidth, start, span, out _, out var max);
                    ctx.LineTo(new Point(x0 + x, mid - max * scale));
                }

                // Bottom contour, right to left, closing the filled shape.
                for (var x = columns - 1; x >= 0; x--)
                {
                    PeakAt(waveform, band, x, regionWidth, start, span, out var min, out _);
                    ctx.LineTo(new Point(x0 + x, mid - min * scale));
                }

                ctx.EndFigure(true);
            }

            return geometry;
        }

        private static void PeakAt(AudioWaveform waveform, WaveformBand? band, int column, double width,
            double start, double span, out float min, out float max)
        {
            var frameStart = (long)((start + column / width * span) * waveform.TotalFrames);
            var frameEnd = (long)((start + (column + 1) / width * span) * waveform.TotalFrames);
            if (band is { } b)
                waveform.GetBandPeak(b, frameStart, frameEnd, out min, out max);
            else
                waveform.GetPeak(frameStart, frameEnd, out min, out max);
        }
    }
}
