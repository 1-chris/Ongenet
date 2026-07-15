using System;
using Avalonia;
using Avalonia.Media;
using Ongenet.App.Theming;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// Horizontal stereo master meter for the top bar: sample-peak bars, optional true-peak markers,
    /// and a compact LUFS / dBTP readout with true-peak over-target warning tint.
    /// </summary>
    public sealed class MasterMeterControl : ThemedControl
    {
        public static readonly StyledProperty<double> LevelLeftProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(LevelLeft));

        public static readonly StyledProperty<double> LevelRightProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(LevelRight));

        public static readonly StyledProperty<double> TruePeakLeftDbTpProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(TruePeakLeftDbTp), -120);

        public static readonly StyledProperty<double> TruePeakRightDbTpProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(TruePeakRightDbTp), -120);

        public static readonly StyledProperty<string> LoudnessTextProperty =
            AvaloniaProperty.Register<MasterMeterControl, string>(nameof(LoudnessText), "");

        public static readonly StyledProperty<bool> TruePeakWarningProperty =
            AvaloniaProperty.Register<MasterMeterControl, bool>(nameof(TruePeakWarning));

        public static readonly StyledProperty<double> TargetTruePeakDbTpProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(TargetTruePeakDbTp), -1.0);

        public static readonly StyledProperty<string> MeterTapLabelProperty =
            AvaloniaProperty.Register<MasterMeterControl, string>(nameof(MeterTapLabel), "");

        /// <summary>K-System style 0 dB reference offset (0 = digital FS, 20 = K-20). Shifts tick display.</summary>
        public static readonly StyledProperty<double> KSystemOffsetDbProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(KSystemOffsetDb), 0);

        private IBrush _background = Brushes.Black;
        private IPen _tickPen = new Pen(Brushes.Gray, 1);
        private IPen _tpPen = new Pen(Brushes.Orange, 1);
        private IBrush _barBrush = Brushes.Green;
        private IBrush _warnBrush = Brushes.Red;
        private IBrush _textBrush = Brushes.White;
        private double _cachedBarWidth = -1;

        protected override void BuildThemeResources()
        {
            _background = new SolidColorBrush(ThemePalette.Mantle);
            _tickPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Crust, 120)), 1);
            _tpPen = new Pen(new SolidColorBrush(ThemePalette.Peach), 1.5);
            _warnBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Red, 80));
            _textBrush = new SolidColorBrush(ThemePalette.Subtext0);
            _cachedBarWidth = -1;
        }

        static MasterMeterControl()
        {
            AffectsRender<MasterMeterControl>(
                LevelLeftProperty, LevelRightProperty,
                TruePeakLeftDbTpProperty, TruePeakRightDbTpProperty,
                LoudnessTextProperty, TruePeakWarningProperty,
                TargetTruePeakDbTpProperty, MeterTapLabelProperty, KSystemOffsetDbProperty);
        }

        public double LevelLeft { get => GetValue(LevelLeftProperty); set => SetValue(LevelLeftProperty, value); }
        public double LevelRight { get => GetValue(LevelRightProperty); set => SetValue(LevelRightProperty, value); }
        public double TruePeakLeftDbTp { get => GetValue(TruePeakLeftDbTpProperty); set => SetValue(TruePeakLeftDbTpProperty, value); }
        public double TruePeakRightDbTp { get => GetValue(TruePeakRightDbTpProperty); set => SetValue(TruePeakRightDbTpProperty, value); }
        public string LoudnessText { get => GetValue(LoudnessTextProperty); set => SetValue(LoudnessTextProperty, value); }
        public bool TruePeakWarning { get => GetValue(TruePeakWarningProperty); set => SetValue(TruePeakWarningProperty, value); }
        public double TargetTruePeakDbTp { get => GetValue(TargetTruePeakDbTpProperty); set => SetValue(TargetTruePeakDbTpProperty, value); }
        public string MeterTapLabel { get => GetValue(MeterTapLabelProperty); set => SetValue(MeterTapLabelProperty, value); }
        public double KSystemOffsetDb { get => GetValue(KSystemOffsetDbProperty); set => SetValue(KSystemOffsetDbProperty, value); }

        public override void Render(DrawingContext context)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 4 || h < 4) return;

            using var clip = context.PushClip(new Rect(0, 0, w, h));

            context.FillRectangle(_background, new Rect(0, 0, w, h));
            if (TruePeakWarning ||
                Math.Max(TruePeakLeftDbTp, TruePeakRightDbTp) > TargetTruePeakDbTp + 0.05)
                context.FillRectangle(_warnBrush, new Rect(0, 0, w, h));

            const double gap = 2;
            var hasText = !string.IsNullOrEmpty(LoudnessText) || !string.IsNullOrEmpty(MeterTapLabel);
            var textH = hasText ? 11 : 0;
            var barsH = h - textH - (textH > 0 ? 2 : 0);
            var barH = Math.Max(2, (barsH - gap) / 2);

            DrawBar(context, new Rect(0, 0, w, barH), LevelLeft, TruePeakLeftDbTp);
            DrawBar(context, new Rect(0, barH + gap, w, barH), LevelRight, TruePeakRightDbTp);

            foreach (var db in MeterScale.Ticks)
            {
                var x = MeterScale.NormalizeDb(db - KSystemOffsetDb) * w;
                context.DrawLine(_tickPen, new Point(x, 0), new Point(x, barsH));
            }

            // K-System 0 reference when offset is active (K-12 / K-14 / K-20).
            if (Math.Abs(KSystemOffsetDb) > 0.01)
            {
                var k0 = MeterScale.NormalizeDb(-KSystemOffsetDb) * w;
                context.DrawLine(_tpPen, new Point(k0, 0), new Point(k0, barsH));
            }

            var targetX = MeterScale.NormalizeDb(TargetTruePeakDbTp) * w;
            context.DrawLine(_tpPen, new Point(targetX, 0), new Point(targetX, barsH));

            if (textH > 0)
            {
                var label = string.IsNullOrEmpty(MeterTapLabel) ? LoudnessText
                    : string.IsNullOrEmpty(LoudnessText) ? MeterTapLabel
                    : $"{MeterTapLabel} · {LoudnessText}";
                var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 9, _textBrush)
                {
                    MaxTextWidth = Math.Max(1, w - 4),
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                context.DrawText(ft, new Point(2, barsH + 1));
            }
        }

        private void DrawBar(DrawingContext context, Rect area, double level, double tpDb)
        {
            var fill = MeterScale.Normalize(level);
            if (fill > 0)
            {
                if (Math.Abs(area.Width - _cachedBarWidth) > 0.5)
                {
                    _cachedBarWidth = area.Width;
                    _barBrush = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                        EndPoint = new RelativePoint(area.Width, 0, RelativeUnit.Absolute),
                        GradientStops =
                        {
                            new GradientStop(ThemePalette.Green, 0.0),
                            new GradientStop(ThemePalette.Green, 0.6),
                            new GradientStop(ThemePalette.Yellow, 0.8),
                            new GradientStop(ThemePalette.Peach, 0.9),
                            new GradientStop(ThemePalette.Red, 1.0)
                        }
                    };
                }

                context.FillRectangle(_barBrush, new Rect(area.X, area.Y, area.Width * fill, area.Height));
            }

            if (tpDb > -100)
            {
                var tx = MeterScale.NormalizeDb(tpDb) * area.Width;
                context.DrawLine(_tpPen, new Point(area.X + tx, area.Y), new Point(area.X + tx, area.Y + area.Height));
            }
        }
    }
}
