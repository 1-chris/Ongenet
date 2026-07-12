using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ongenet.App.Display;
using Ongenet.App.Theming;
using Ongenet.Core.Audio.Files;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// Draws an audio clip's crossfades with draggable fade handles when selected.
    /// </summary>
    public sealed class ClipFadeControl : ThemedControl
    {
        private const double WaveMargin = 3.0;
        private const double HandleHit = 8.0;

        public static readonly StyledProperty<double> FadeInWidthProperty =
            AvaloniaProperty.Register<ClipFadeControl, double>(nameof(FadeInWidth));

        public static readonly StyledProperty<double> FadeOutWidthProperty =
            AvaloniaProperty.Register<ClipFadeControl, double>(nameof(FadeOutWidth));

        public static readonly StyledProperty<double> ClipWidthProperty =
            AvaloniaProperty.Register<ClipFadeControl, double>(nameof(ClipWidth));

        public static readonly StyledProperty<double> ClipLengthBeatsProperty =
            AvaloniaProperty.Register<ClipFadeControl, double>(nameof(ClipLengthBeats), 1.0);

        public static readonly StyledProperty<double> PixelsPerBeatProperty =
            AvaloniaProperty.Register<ClipFadeControl, double>(nameof(PixelsPerBeat), 1.0);

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<ClipFadeControl, bool>(nameof(IsSelected));

        public static readonly StyledProperty<AudioWaveform?> FadeInWaveformProperty =
            AvaloniaProperty.Register<ClipFadeControl, AudioWaveform?>(nameof(FadeInWaveform));

        public static readonly StyledProperty<AudioWaveform?> FadeOutWaveformProperty =
            AvaloniaProperty.Register<ClipFadeControl, AudioWaveform?>(nameof(FadeOutWaveform));

        public static readonly StyledProperty<IBrush?> ClipBackgroundProperty =
            AvaloniaProperty.Register<ClipFadeControl, IBrush?>(nameof(ClipBackground));

        public static readonly StyledProperty<IBrush?> WaveFillProperty =
            AvaloniaProperty.Register<ClipFadeControl, IBrush?>(nameof(WaveFill));

        public static readonly StyledProperty<IBrush?> StrokeProperty =
            AvaloniaProperty.Register<ClipFadeControl, IBrush?>(nameof(Stroke));

        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<ClipFadeControl, int>(nameof(Revision));

        public static readonly StyledProperty<Action<double, double>?> FadeChangedProperty =
            AvaloniaProperty.Register<ClipFadeControl, Action<double, double>?>(nameof(FadeChanged));

        private IBrush _bassFill = Brushes.Blue;
        private IBrush _midFill = Brushes.Green;
        private IBrush _trebleFill = Brushes.PeachPuff;

        static ClipFadeControl()
        {
            AffectsRender<ClipFadeControl>(FadeInWidthProperty, FadeOutWidthProperty, FadeInWaveformProperty,
                FadeOutWaveformProperty, ClipBackgroundProperty, WaveFillProperty, StrokeProperty, RevisionProperty,
                IsSelectedProperty);
        }

        public double FadeInWidth { get => GetValue(FadeInWidthProperty); set => SetValue(FadeInWidthProperty, value); }
        public double FadeOutWidth { get => GetValue(FadeOutWidthProperty); set => SetValue(FadeOutWidthProperty, value); }
        public double ClipWidth { get => GetValue(ClipWidthProperty); set => SetValue(ClipWidthProperty, value); }
        public double ClipLengthBeats { get => GetValue(ClipLengthBeatsProperty); set => SetValue(ClipLengthBeatsProperty, value); }
        public double PixelsPerBeat { get => GetValue(PixelsPerBeatProperty); set => SetValue(PixelsPerBeatProperty, value); }
        public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
        public AudioWaveform? FadeInWaveform { get => GetValue(FadeInWaveformProperty); set => SetValue(FadeInWaveformProperty, value); }
        public AudioWaveform? FadeOutWaveform { get => GetValue(FadeOutWaveformProperty); set => SetValue(FadeOutWaveformProperty, value); }
        public IBrush? ClipBackground { get => GetValue(ClipBackgroundProperty); set => SetValue(ClipBackgroundProperty, value); }
        public IBrush? WaveFill { get => GetValue(WaveFillProperty); set => SetValue(WaveFillProperty, value); }
        public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public int Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
        public Action<double, double>? FadeChanged { get => GetValue(FadeChangedProperty); set => SetValue(FadeChangedProperty, value); }

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

        protected override void BuildThemeResources()
        {
            _bassFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Sapphire, 210));
            _midFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Green, 200));
            _trebleFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Peach, 210));
        }

        private enum DragKind { None, FadeIn, FadeOut }
        private DragKind _drag = DragKind.None;
        private double _dragFadeInBeats;
        private double _dragFadeOutBeats;

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
                var ramp = TriangleGeometry(new Point(0, height), new Point(fadeIn, 0), new Point(0, 0));
                context.DrawGeometry(rampFill, null, ramp);
                context.DrawLine(neighbourPen, new Point(0, 0), new Point(fadeIn, height));
                context.DrawLine(ownPen, new Point(0, height), new Point(fadeIn, 0));
            }

            var fadeOut = Math.Min(Math.Max(0, FadeOutWidth), width);
            if (fadeOut > 0)
            {
                var x0 = width - fadeOut;
                DrawMix(context, FadeOutWaveform, x0, fadeOut, width, height);
                var ramp = TriangleGeometry(new Point(x0, 0), new Point(width, height), new Point(x0, height));
                context.DrawGeometry(rampFill, null, ramp);
                context.DrawLine(neighbourPen, new Point(x0, height), new Point(width, 0));
                context.DrawLine(ownPen, new Point(x0, 0), new Point(width, height));
            }

            if (IsSelected)
            {
                var handleBrush = new SolidColorBrush(Colors.White, 0.75);
                if (fadeIn > 0)
                    context.FillRectangle(handleBrush, new Rect(fadeIn - 2, 0, 4, height));
                if (fadeOut > 0)
                    context.FillRectangle(handleBrush, new Rect(width - fadeOut - 2, 0, 4, height));
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is ViewModels.Timeline.ClipViewModel vm)
                FadeChanged = vm.OnFadeDragCompleted;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!IsSelected || PixelsPerBeat <= 0) return;
            var x = e.GetPosition(this).X;
            var width = Bounds.Width;
            var fadeIn = Math.Min(Math.Max(0, FadeInWidth), width);
            var fadeOut = Math.Min(Math.Max(0, FadeOutWidth), width);

            if (fadeIn > 0 && Math.Abs(x - fadeIn) <= HandleHit)
            {
                _drag = DragKind.FadeIn;
                _dragFadeInBeats = fadeIn / PixelsPerBeat;
                _dragFadeOutBeats = FadeOutWidth / PixelsPerBeat;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (fadeOut > 0 && Math.Abs(x - (width - fadeOut)) <= HandleHit)
            {
                _drag = DragKind.FadeOut;
                _dragFadeInBeats = FadeInWidth / PixelsPerBeat;
                _dragFadeOutBeats = fadeOut / PixelsPerBeat;
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_drag == DragKind.None || PixelsPerBeat <= 0) return;
            var x = Math.Clamp(e.GetPosition(this).X, 0, Bounds.Width);
            var beat = x / PixelsPerBeat;

            if (_drag == DragKind.FadeIn)
                _dragFadeInBeats = Math.Clamp(beat, 0, ClipLengthBeats);
            else
                _dragFadeOutBeats = Math.Clamp((Bounds.Width - x) / PixelsPerBeat, 0, ClipLengthBeats);

            FadeInWidth = _dragFadeInBeats * PixelsPerBeat;
            FadeOutWidth = _dragFadeOutBeats * PixelsPerBeat;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_drag == DragKind.None) return;
            _drag = DragKind.None;
            e.Pointer.Capture(null);
            FadeChanged?.Invoke(_dragFadeInBeats, _dragFadeOutBeats);
            e.Handled = true;
        }

        private void DrawMix(DrawingContext context, AudioWaveform? mix, double x0, double regionWidth,
            double width, double height)
        {
            if (mix is null || mix.TotalFrames <= 0) return;

            if (ClipBackground is { } bg)
                context.FillRectangle(bg, new Rect(x0, 0, regionWidth, height));

            var waveHeight = Math.Max(1, height - WaveMargin * 2);
            using (context.PushTransform(Matrix.CreateTranslation(0, WaveMargin)))
            {
                WaveformControl.Draw(context, mix, x0, regionWidth, waveHeight, 0, 1,
                    WaveformDisplayPreferences.BandColorsEnabled, WaveFill, _bassFill, _midFill, _trebleFill);
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
