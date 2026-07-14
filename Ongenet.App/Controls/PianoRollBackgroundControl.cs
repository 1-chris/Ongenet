using Avalonia;
using Avalonia.Media;
using Ongenet.App.Theming;
using Ongenet.App.ViewModels.PianoRoll;
using Ongenet.Core.Music;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// Renders the piano-roll grid backdrop: shaded rows for black keys, optional in-scale row
    /// highlighting, faint row lines (stronger at each C), and vertical beat lines (stronger at
    /// each bar). Custom-drawn because 88 rows × many beats would be far too many elements.
    /// </summary>
    public sealed class PianoRollBackgroundControl : ThemedControl
    {
        public static readonly StyledProperty<double> PixelsPerBeatProperty =
            AvaloniaProperty.Register<PianoRollBackgroundControl, double>(nameof(PixelsPerBeat));

        public static readonly StyledProperty<double> TotalBeatsProperty =
            AvaloniaProperty.Register<PianoRollBackgroundControl, double>(nameof(TotalBeats));

        public static readonly StyledProperty<int> BeatsPerBarProperty =
            AvaloniaProperty.Register<PianoRollBackgroundControl, int>(nameof(BeatsPerBar), 4);

        public static readonly StyledProperty<bool> ScaleHighlightEnabledProperty =
            AvaloniaProperty.Register<PianoRollBackgroundControl, bool>(nameof(ScaleHighlightEnabled));

        public static readonly StyledProperty<int> ScaleRootPitchClassProperty =
            AvaloniaProperty.Register<PianoRollBackgroundControl, int>(nameof(ScaleRootPitchClass));

        public static readonly StyledProperty<ScaleType> SelectedScaleProperty =
            AvaloniaProperty.Register<PianoRollBackgroundControl, ScaleType>(nameof(SelectedScale));

        // Black-key rows shaded with Crust (always darker than Base across flavours); lines from Text.
        private IBrush _blackRowBrush = Brushes.Transparent;
        private IBrush _scaleRowBrush = Brushes.Transparent;
        private IPen _rowPen = new Pen(Brushes.Gray, 1);
        private IPen _octavePen = new Pen(Brushes.Gray, 1);
        private IPen _beatPen = new Pen(Brushes.Gray, 1);
        private IPen _barPen = new Pen(Brushes.Gray, 1);
        private IPen _subPen = new Pen(Brushes.Gray, 1);

        protected override void BuildThemeResources()
        {
            var fg = ThemePalette.Text;
            _blackRowBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Crust, 90));
            _scaleRowBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Teal, 48));
            _rowPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 36)), 1);
            _octavePen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 70)), 1);
            _beatPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 30)), 1);
            _barPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 80)), 1);
            _subPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 16)), 1);
        }

        static PianoRollBackgroundControl()
        {
            AffectsRender<PianoRollBackgroundControl>(
                PixelsPerBeatProperty, TotalBeatsProperty, BeatsPerBarProperty,
                ScaleHighlightEnabledProperty, ScaleRootPitchClassProperty, SelectedScaleProperty);
        }

        public double PixelsPerBeat { get => GetValue(PixelsPerBeatProperty); set => SetValue(PixelsPerBeatProperty, value); }
        public double TotalBeats { get => GetValue(TotalBeatsProperty); set => SetValue(TotalBeatsProperty, value); }
        public int BeatsPerBar { get => GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }
        public bool ScaleHighlightEnabled { get => GetValue(ScaleHighlightEnabledProperty); set => SetValue(ScaleHighlightEnabledProperty, value); }
        public int ScaleRootPitchClass { get => GetValue(ScaleRootPitchClassProperty); set => SetValue(ScaleRootPitchClassProperty, value); }
        public ScaleType SelectedScale { get => GetValue(SelectedScaleProperty); set => SetValue(SelectedScaleProperty, value); }

        public override void Render(DrawingContext context)
        {
            var width = Bounds.Width;
            var kh = PianoRollMetrics.KeyHeight;
            var highlight = ScaleHighlightEnabled;
            var root = ScaleRootPitchClass;
            var scale = SelectedScale;

            // Rows: shade black-key rows, optional in-scale tint, then row lines (stronger at C).
            for (var note = PianoRollMetrics.HighNote; note >= PianoRollMetrics.LowNote; note--)
            {
                var y = (PianoRollMetrics.HighNote - note) * kh;
                var pitchClass = note % 12;
                var isBlack = pitchClass is 1 or 3 or 6 or 8 or 10;
                if (isBlack)
                {
                    context.FillRectangle(_blackRowBrush, new Rect(0, y, width, kh));
                }

                if (highlight && MusicTheory.IsInScale(note, root, scale))
                {
                    context.FillRectangle(_scaleRowBrush, new Rect(0, y, width, kh));
                }

                var pen = pitchClass == 0 ? _octavePen : _rowPen;
                context.DrawLine(pen, new Point(0, y), new Point(width, y));
            }

            // Vertical bar/beat/sub-beat lines, sub density following the zoom.
            var ppb = PixelsPerBeat;
            var bar = BeatsPerBar < 1 ? 4 : BeatsPerBar;
            if (ppb > 0)
            {
                var height = Bounds.Height;
                var step = GridMath.SnapBeats(ppb, bar);
                var lines = (int)System.Math.Ceiling(TotalBeats / step);
                for (var i = 0; i <= lines; i++)
                {
                    var beat = i * step;
                    var x = beat * ppb;
                    var pen = _subPen;
                    if (IsMultiple(beat, bar)) pen = _barPen;
                    else if (IsMultiple(beat, 1.0)) pen = _beatPen;
                    context.DrawLine(pen, new Point(x, 0), new Point(x, height));
                }
            }
        }

        private static bool IsMultiple(double value, double of)
        {
            var ratio = value / of;
            return System.Math.Abs(ratio - System.Math.Round(ratio)) < 1e-6;
        }
    }
}
