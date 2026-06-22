using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Draws an audio clip's crossfades. In each overlap region it (1) masks the clip's own raw waveform with
    /// the clip background, (2) draws the actual crossfaded (summed) waveform for that region so it's clear
    /// what really plays, and (3) overlays an "X" of fade lines — this clip's volume ramp and the overlapping
    /// neighbour's. Drawing on both sides means the crossfade reads correctly whichever clip is stacked on
    /// top. Purely decorative (no hit-testing).
    /// </summary>
    public sealed class ClipFadeControl : Control
    {
        private const double WaveMargin = 3.0; // matches the clip WaveformControl's vertical margin

        /// <summary>Crossfade-in width in pixels (0 = none).</summary>
        public static readonly StyledProperty<double> FadeInWidthProperty =
            AvaloniaProperty.Register<ClipFadeControl, double>(nameof(FadeInWidth));

        /// <summary>Crossfade-out width in pixels (0 = none).</summary>
        public static readonly StyledProperty<double> FadeOutWidthProperty =
            AvaloniaProperty.Register<ClipFadeControl, double>(nameof(FadeOutWidth));

        /// <summary>The crossfaded waveform for the fade-in (left) overlap region, or null.</summary>
        public static readonly StyledProperty<AudioWaveform?> FadeInWaveformProperty =
            AvaloniaProperty.Register<ClipFadeControl, AudioWaveform?>(nameof(FadeInWaveform));

        /// <summary>The crossfaded waveform for the fade-out (right) overlap region, or null.</summary>
        public static readonly StyledProperty<AudioWaveform?> FadeOutWaveformProperty =
            AvaloniaProperty.Register<ClipFadeControl, AudioWaveform?>(nameof(FadeOutWaveform));

        /// <summary>Brush used to mask the clip's own waveform under the overlap (the clip's background).</summary>
        public static readonly StyledProperty<IBrush?> ClipBackgroundProperty =
            AvaloniaProperty.Register<ClipFadeControl, IBrush?>(nameof(ClipBackground));

        /// <summary>Brush used to fill the crossfaded waveform silhouette (matches the clip waveform).</summary>
        public static readonly StyledProperty<IBrush?> WaveFillProperty =
            AvaloniaProperty.Register<ClipFadeControl, IBrush?>(nameof(WaveFill));

        /// <summary>Stroke colour for the fade lines.</summary>
        public static readonly StyledProperty<IBrush?> StrokeProperty =
            AvaloniaProperty.Register<ClipFadeControl, IBrush?>(nameof(Stroke));

        /// <summary>Bumped to force a repaint when the fades change (the property refs may not).</summary>
        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<ClipFadeControl, int>(nameof(Revision));

        static ClipFadeControl()
        {
            AffectsRender<ClipFadeControl>(FadeInWidthProperty, FadeOutWidthProperty, FadeInWaveformProperty,
                FadeOutWaveformProperty, ClipBackgroundProperty, WaveFillProperty, StrokeProperty, RevisionProperty);
        }

        public double FadeInWidth { get => GetValue(FadeInWidthProperty); set => SetValue(FadeInWidthProperty, value); }
        public double FadeOutWidth { get => GetValue(FadeOutWidthProperty); set => SetValue(FadeOutWidthProperty, value); }
        public AudioWaveform? FadeInWaveform { get => GetValue(FadeInWaveformProperty); set => SetValue(FadeInWaveformProperty, value); }
        public AudioWaveform? FadeOutWaveform { get => GetValue(FadeOutWaveformProperty); set => SetValue(FadeOutWaveformProperty, value); }
        public IBrush? ClipBackground { get => GetValue(ClipBackgroundProperty); set => SetValue(ClipBackgroundProperty, value); }
        public IBrush? WaveFill { get => GetValue(WaveFillProperty); set => SetValue(WaveFillProperty, value); }
        public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public int Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }

        public override void Render(DrawingContext context)
        {
            var width = Bounds.Width;
            var height = Bounds.Height;
            if (width < 1 || height < 1) return;

            var color = (Stroke as ISolidColorBrush)?.Color ?? Colors.Black;
            var ownPen = new Pen(new SolidColorBrush(color), 1.6);
            var neighbourPen = new Pen(new SolidColorBrush(color, 0.55), 1.2);
            var rampFill = new SolidColorBrush(color, 0.14);

            var fadeIn = Math.Min(Math.Max(0, FadeInWidth), width);
            if (fadeIn > 0)
            {
                DrawMix(context, FadeInWaveform, 0, fadeIn, width, height);
                // This clip rises from silence; the overlapping neighbour falls — an X over [0, fadeIn].
                var ramp = TriangleGeometry(new Point(0, height), new Point(fadeIn, 0), new Point(0, 0));
                context.DrawGeometry(rampFill, null, ramp);
                context.DrawLine(neighbourPen, new Point(0, 0), new Point(fadeIn, height)); // neighbour fading out
                context.DrawLine(ownPen, new Point(0, height), new Point(fadeIn, 0));        // this clip fading in
            }

            var fadeOut = Math.Min(Math.Max(0, FadeOutWidth), width);
            if (fadeOut > 0)
            {
                var x0 = width - fadeOut;
                DrawMix(context, FadeOutWaveform, x0, fadeOut, width, height);
                // This clip falls to silence; the overlapping neighbour rises — an X over [x0, width].
                var ramp = TriangleGeometry(new Point(x0, 0), new Point(width, height), new Point(x0, height));
                context.DrawGeometry(rampFill, null, ramp);
                context.DrawLine(neighbourPen, new Point(x0, height), new Point(width, 0)); // neighbour fading in
                context.DrawLine(ownPen, new Point(x0, 0), new Point(width, height));       // this clip fading out
            }
        }

        // Masks the raw waveform under [x0, x0+regionWidth] with the clip background, then draws the
        // crossfaded waveform there (inset to match the clip's own waveform margins).
        private void DrawMix(DrawingContext context, AudioWaveform? mix, double x0, double regionWidth,
            double width, double height)
        {
            if (mix is null || mix.TotalFrames <= 0) return;

            if (ClipBackground is { } bg)
                context.FillRectangle(bg, new Rect(x0, 0, regionWidth, height));

            var waveHeight = Math.Max(1, height - WaveMargin * 2);
            using (context.PushTransform(Matrix.CreateTranslation(0, WaveMargin)))
            {
                var geo = WaveformControl.BuildGeometry(mix, x0, regionWidth, waveHeight, 0.0, 1.0);
                context.DrawGeometry(WaveFill ?? Brushes.Black, null, geo);
            }
        }

        private static StreamGeometry TriangleGeometry(Point a, Point b, Point c)
        {
            var geo = new StreamGeometry();
            using var ctx = geo.Open();
            ctx.BeginFigure(a, true);
            ctx.LineTo(b);
            ctx.LineTo(c);
            ctx.EndFigure(true);
            return geo;
        }
    }
}
