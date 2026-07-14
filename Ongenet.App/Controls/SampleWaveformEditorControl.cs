using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Interactive waveform editor: tempo grid, ruler, hover cursor, snap, trim handles, selection, move.
    /// </summary>
    public sealed class SampleWaveformEditorControl : ThemedControl
    {
        public static readonly StyledProperty<AudioWaveform?> WaveformProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, AudioWaveform?>(nameof(Waveform));

        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, int>(nameof(Revision));

        public static readonly StyledProperty<double> DurationSecondsProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(DurationSeconds));

        public static readonly StyledProperty<double> SecondsPerBeatProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(SecondsPerBeat));

        public static readonly StyledProperty<int> BeatsPerBarProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, int>(nameof(BeatsPerBar), 4);

        public static readonly StyledProperty<double> ContentWidthProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(ContentWidth));

        public static readonly StyledProperty<double> HorizontalOffsetProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(HorizontalOffset));

        public static readonly StyledProperty<double> ViewportWidthProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(ViewportWidth));

        public static readonly StyledProperty<double> TrimStartSecondsProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(TrimStartSeconds));

        public static readonly StyledProperty<double> TrimEndSecondsProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(TrimEndSeconds), double.PositiveInfinity);

        public static readonly StyledProperty<double> HighlightStartSecondsProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(HighlightStartSeconds));

        public static readonly StyledProperty<double> HighlightEndSecondsProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(HighlightEndSeconds), double.PositiveInfinity);

        public static readonly StyledProperty<double> SelectionStartSecondsProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(SelectionStartSeconds), -1.0);

        public static readonly StyledProperty<double> SelectionEndSecondsProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(SelectionEndSeconds), -1.0);

        public static readonly StyledProperty<double> HoverSecondsProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(HoverSeconds), -1.0);

        public static readonly StyledProperty<double> PlayheadSecondsProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, double>(nameof(PlayheadSeconds), -1.0);

        public static readonly StyledProperty<bool> SnapEnabledProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, bool>(nameof(SnapEnabled), true);

        public static readonly StyledProperty<bool> SpectralOverlayEnabledProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, bool>(nameof(SpectralOverlayEnabled));

        public static readonly StyledProperty<IReadOnlyList<float>?> SpectralMagnitudesProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, IReadOnlyList<float>?>(nameof(SpectralMagnitudes));

        public static readonly StyledProperty<int> SpectralRevisionProperty =
            AvaloniaProperty.Register<SampleWaveformEditorControl, int>(nameof(SpectralRevision));

        private const double RulerHeight = 18.0;
        private const double HandleHitPx = 6.0;
        private const double MinTrimSeconds = 0.001;

        private IBrush _crust = Brushes.Black;
        private IBrush _waveFill = Brushes.Gray;
        private IBrush _bassFill = Brushes.Blue;
        private IBrush _midFill = Brushes.Green;
        private IBrush _trebleFill = Brushes.PeachPuff;
        private IPen _barPen = new Pen(Brushes.Gray, 1);
        private IPen _beatPen = new Pen(Brushes.Gray, 1);
        private IPen _subPen = new Pen(Brushes.Gray, 1);
        private IBrush _labelBrush = Brushes.Gray;
        private IPen _hoverPen = new Pen(Brushes.Gray, 1);
        private IPen _playheadPen = new Pen(Brushes.Gray, 1);

        private static readonly Typeface LabelTypeface = new(new FontFamily("fonts:Inter#Inter"));

        private enum DragMode { None, TrimStart, TrimEnd, Select, MoveSelection }

        private DragMode _drag = DragMode.None;
        private double _moveAnchorSeconds;
        private double _selectionAtMoveStart;
        private double _selectionEndAtMoveStart;

        static SampleWaveformEditorControl()
        {
            AffectsRender<SampleWaveformEditorControl>(WaveformProperty, RevisionProperty, DurationSecondsProperty,
                SecondsPerBeatProperty, BeatsPerBarProperty, HorizontalOffsetProperty, ContentWidthProperty, ViewportWidthProperty,
                TrimStartSecondsProperty, TrimEndSecondsProperty, HighlightStartSecondsProperty,
                HighlightEndSecondsProperty,                 SelectionStartSecondsProperty, SelectionEndSecondsProperty,
                HoverSecondsProperty, PlayheadSecondsProperty, SpectralOverlayEnabledProperty,
                SpectralMagnitudesProperty, SpectralRevisionProperty);
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

        protected override void BuildThemeResources()
        {
            _crust = ThemePalette.BrushOf("Crust");
            _waveFill = ThemePalette.BrushOf("Mauve");
            _bassFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Sapphire, 210));
            _midFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Green, 200));
            _trebleFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Peach, 210));
            var fg = ThemePalette.Text;
            _barPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 80)), 1);
            _beatPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 40)), 1);
            _subPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 20)), 1);
            _labelBrush = new SolidColorBrush(ThemePalette.WithAlpha(fg, 180));
            _hoverPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 140)), 1);
            _playheadPen = new Pen(ThemePalette.BrushOf("Red"), 2);
        }

        public AudioWaveform? Waveform { get => GetValue(WaveformProperty); set => SetValue(WaveformProperty, value); }
        public int Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
        public double DurationSeconds { get => GetValue(DurationSecondsProperty); set => SetValue(DurationSecondsProperty, value); }
        public double SecondsPerBeat { get => GetValue(SecondsPerBeatProperty); set => SetValue(SecondsPerBeatProperty, value); }
        public int BeatsPerBar { get => GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }
        public double HorizontalOffset { get => GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
        public double ContentWidth { get => GetValue(ContentWidthProperty); set => SetValue(ContentWidthProperty, value); }
        public double ViewportWidth { get => GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
        public double TrimStartSeconds { get => GetValue(TrimStartSecondsProperty); set => SetValue(TrimStartSecondsProperty, value); }
        public double TrimEndSeconds { get => GetValue(TrimEndSecondsProperty); set => SetValue(TrimEndSecondsProperty, value); }
        public double HighlightStartSeconds { get => GetValue(HighlightStartSecondsProperty); set => SetValue(HighlightStartSecondsProperty, value); }
        public double HighlightEndSeconds { get => GetValue(HighlightEndSecondsProperty); set => SetValue(HighlightEndSecondsProperty, value); }
        public double SelectionStartSeconds { get => GetValue(SelectionStartSecondsProperty); set => SetValue(SelectionStartSecondsProperty, value); }
        public double SelectionEndSeconds { get => GetValue(SelectionEndSecondsProperty); set => SetValue(SelectionEndSecondsProperty, value); }
        public double HoverSeconds { get => GetValue(HoverSecondsProperty); set => SetValue(HoverSecondsProperty, value); }
        public double PlayheadSeconds { get => GetValue(PlayheadSecondsProperty); set => SetValue(PlayheadSecondsProperty, value); }
        public bool SnapEnabled { get => GetValue(SnapEnabledProperty); set => SetValue(SnapEnabledProperty, value); }
        public bool SpectralOverlayEnabled { get => GetValue(SpectralOverlayEnabledProperty); set => SetValue(SpectralOverlayEnabledProperty, value); }
        public IReadOnlyList<float>? SpectralMagnitudes { get => GetValue(SpectralMagnitudesProperty); set => SetValue(SpectralMagnitudesProperty, value); }
        public int SpectralRevision { get => GetValue(SpectralRevisionProperty); set => SetValue(SpectralRevisionProperty, value); }

        public event EventHandler? TrimCommitted;
        public event EventHandler? SelectionChanged;
        public event EventHandler? MoveStarted;
        public event EventHandler? MoveCommitted;
        public event EventHandler? HoverChanged;

        public override void Render(DrawingContext context)
        {
            var width = Bounds.Width;
            var height = Bounds.Height;
            if (width < 1 || height < 1) return;

            context.FillRectangle(_crust, new Rect(0, 0, width, height));

            var waveform = Waveform;
            if (waveform is null || DurationSeconds <= 0) return;

            var waveTop = RulerHeight;
            var waveHeight = Math.Max(1, height - RulerHeight);

            DrawGridAndRuler(context, width, waveTop, waveHeight);

            if (SpectralOverlayEnabled && SpectralMagnitudes is { Count: > 0 })
                DrawSpectralOverlay(context, width, waveTop, waveHeight);

            using (context.PushTransform(Matrix.CreateTranslation(0, waveTop)))
            {
                WaveformControl.Draw(context, waveform, 0, width, waveHeight, 0, 1,
                    WaveformDisplayPreferences.BandColorsEnabled, _waveFill, _bassFill, _midFill, _trebleFill);
            }

            var hiStart = SecondsToX(HighlightStartSeconds, width);
            var hiEnd = SecondsToX(EffectiveEnd(HighlightEndSeconds), width);
            if (hiEnd > hiStart)
            {
                context.FillRectangle(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.ColorOf("Blue"), 20)),
                    new Rect(hiStart, waveTop, hiEnd - hiStart, waveHeight));
            }

            if (HasSelection())
            {
                var selA = SecondsToX(Math.Min(SelectionStartSeconds, SelectionEndSeconds), width);
                var selB = SecondsToX(Math.Max(SelectionStartSeconds, SelectionEndSeconds), width);
                context.FillRectangle(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.ColorOf("Green"), 56)),
                    new Rect(selA, waveTop, selB - selA, waveHeight));
            }

            var trimA = SecondsToX(TrimStartSeconds, width);
            var trimB = SecondsToX(EffectiveEnd(TrimEndSeconds), width);
            var handlePen = new Pen(ThemePalette.BrushOf("Yellow"), 2);
            context.DrawLine(handlePen, new Point(trimA, waveTop), new Point(trimA, height));
            context.DrawLine(handlePen, new Point(trimB, waveTop), new Point(trimB, height));

            if (HoverSeconds >= 0)
            {
                var hx = SecondsToX(HoverSeconds, width);
                context.DrawLine(_hoverPen, new Point(hx, 0), new Point(hx, height));
            }

            if (PlayheadSeconds >= 0)
            {
                var px = SecondsToX(PlayheadSeconds, width);
                context.DrawLine(_playheadPen, new Point(px, 0), new Point(px, height));
            }
        }

        private void DrawGridAndRuler(DrawingContext context, double width, double waveTop, double waveHeight)
        {
            var spb = SecondsPerBeat;
            if (spb <= 0) return;

            var bar = BeatsPerBar < 1 ? 4 : BeatsPerBar;
            var stepBeats = SampleGridMath.GridStepBeats(width, DurationSeconds, spb, bar);
            if (stepBeats <= 0) return;

            var totalBeats = DurationSeconds / spb;
            var lineCount = (int)Math.Ceiling(totalBeats / stepBeats);

            var scrollLeft = HorizontalOffset;
            var visibleWidth = ViewportWidth > 0 ? ViewportWidth : width;
            var scrollRight = scrollLeft + visibleWidth;

            var firstIndex = Math.Max(0, (int)Math.Floor((scrollLeft / width) * totalBeats / stepBeats) - 1);
            var lastIndex = Math.Min(lineCount, (int)Math.Ceiling((scrollRight / width) * totalBeats / stepBeats) + 1);

            for (var i = firstIndex; i <= lastIndex; i++)
            {
                var beat = i * stepBeats;
                var seconds = beat * spb;
                if (seconds > DurationSeconds) break;
                var x = SecondsToX(seconds, width);

                var pen = _subPen;
                if (SampleGridMath.IsMultiple(beat, bar)) pen = _barPen;
                else if (SampleGridMath.IsMultiple(beat, 1.0)) pen = _beatPen;

                context.DrawLine(pen, new Point(x, waveTop), new Point(x, waveTop + waveHeight));

                if (SampleGridMath.IsMultiple(beat, bar))
                {
                    var barNum = (int)Math.Round(beat / bar) + 1;
                    var barLabel = new FormattedText(barNum.ToString(), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, LabelTypeface, 10, _labelBrush);
                    context.DrawText(barLabel, new Point(x + 2, 1));

                    var timeLabel = new FormattedText(
                        string.Create(CultureInfo.InvariantCulture, $"{seconds:0.##}s"),
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 9,
                        new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Text, 120)));
                    context.DrawText(timeLabel, new Point(x + 2, 10));
                }
            }
        }

        private void DrawSpectralOverlay(DrawingContext context, double width, double waveTop, double waveHeight)
        {
            var mags = SpectralMagnitudes!;
            var count = mags.Count;
            if (count <= 0) return;

            var barWidth = Math.Max(1.0, width / count);
            var spectralBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.ColorOf("Mauve"), 90));

            for (var i = 0; i < count; i++)
            {
                var h = mags[i] * waveHeight * 0.85;
                if (h < 1) continue;
                var x = i * barWidth;
                context.FillRectangle(spectralBrush, new Rect(x, waveTop + waveHeight - h, barWidth, h));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || DurationSeconds <= 0) return;

            var width = MapWidth;
            var seconds = Snap(PointerXToSeconds(e.GetPosition(this).X, width));

            if (e.ClickCount >= 2)
            {
                _drag = DragMode.None;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            var contentX = ContentXFromPointer(e.GetPosition(this).X);
            var trimStartX = SecondsToX(TrimStartSeconds, width);
            var trimEndX = SecondsToX(EffectiveEnd(TrimEndSeconds), width);

            if (Math.Abs(contentX - trimStartX) <= HandleHitPx)
            {
                _drag = DragMode.TrimStart;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (Math.Abs(contentX - trimEndX) <= HandleHitPx)
            {
                _drag = DragMode.TrimEnd;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (HasSelection() && IsInsideSelection(seconds))
            {
                _drag = DragMode.MoveSelection;
                _moveAnchorSeconds = seconds;
                _selectionAtMoveStart = SelectionStartSeconds;
                _selectionEndAtMoveStart = SelectionEndSeconds;
                MoveStarted?.Invoke(this, EventArgs.Empty);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            _drag = DragMode.Select;
            SelectionStartSeconds = seconds;
            SelectionEndSeconds = seconds;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (DurationSeconds <= 0) return;

            var width = MapWidth;
            var contentX = ContentXFromPointer(e.GetPosition(this).X);

            if (_drag == DragMode.None)
            {
                UpdateHover(contentX, width);
                return;
            }

            var seconds = Snap(ClampSeconds(XToSeconds(contentX, width)));

            switch (_drag)
            {
                case DragMode.TrimStart:
                    TrimStartSeconds = Math.Min(seconds, EffectiveEnd(TrimEndSeconds) - MinTrimSeconds);
                    break;
                case DragMode.TrimEnd:
                    TrimEndSeconds = Math.Max(seconds, TrimStartSeconds + MinTrimSeconds);
                    break;
                case DragMode.Select:
                    SelectionEndSeconds = seconds;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case DragMode.MoveSelection:
                {
                    var delta = seconds - _moveAnchorSeconds;
                    var len = _selectionEndAtMoveStart - _selectionAtMoveStart;
                    var newStart = ClampSeconds(_selectionAtMoveStart + delta);
                    if (newStart + len > DurationSeconds) newStart = DurationSeconds - len;
                    if (newStart < 0) newStart = 0;
                    SelectionStartSeconds = newStart;
                    SelectionEndSeconds = newStart + len;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    break;
                }
            }

            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_drag == DragMode.None) return;

            if (_drag is DragMode.TrimStart or DragMode.TrimEnd)
                TrimCommitted?.Invoke(this, EventArgs.Empty);
            else if (_drag == DragMode.MoveSelection)
                MoveCommitted?.Invoke(this, EventArgs.Empty);

            _drag = DragMode.None;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_drag == DragMode.None && HoverSeconds >= 0)
            {
                HoverSeconds = -1;
                HoverChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void UpdateHover(double contentX, double width)
        {
            var seconds = Snap(ClampSeconds(XToSeconds(contentX, width)));
            if (Math.Abs(seconds - HoverSeconds) < 1e-9) return;
            HoverSeconds = seconds;
            HoverChanged?.Invoke(this, EventArgs.Empty);
        }

        private double Snap(double seconds)
            => SnapEnabled
                ? SampleGridMath.SnapSeconds(seconds, MapWidth, DurationSeconds, SecondsPerBeat, BeatsPerBar)
                : seconds;

        private bool HasSelection()
            => SelectionStartSeconds >= 0 && SelectionEndSeconds >= 0 &&
               Math.Abs(SelectionEndSeconds - SelectionStartSeconds) > MinTrimSeconds;

        private bool IsInsideSelection(double seconds)
        {
            if (!HasSelection()) return false;
            var a = Math.Min(SelectionStartSeconds, SelectionEndSeconds);
            var b = Math.Max(SelectionStartSeconds, SelectionEndSeconds);
            return seconds >= a && seconds <= b;
        }

        private double EffectiveEnd(double end) => end > DurationSeconds || double.IsPositiveInfinity(end) ? DurationSeconds : end;

        private double SecondsToX(double seconds, double width)
            => DurationSeconds <= 0 ? 0 : Math.Clamp(seconds / DurationSeconds, 0, 1) * width;

        /// <summary>Pointer X is already in content coordinates inside the scrolled editor.</summary>
        private double ContentXFromPointer(double pointerX) => pointerX;

        private double MapWidth => ContentWidth > 1 ? ContentWidth : Bounds.Width;

        private double PointerXToSeconds(double pointerX, double width)
            => XToSeconds(ContentXFromPointer(pointerX), width);

        private double XToSeconds(double contentX, double width)
            => width <= 0 ? 0 : Math.Clamp(contentX / width, 0, 1) * DurationSeconds;

        private double ClampSeconds(double seconds) => Math.Clamp(seconds, 0, DurationSeconds);
    }
}
