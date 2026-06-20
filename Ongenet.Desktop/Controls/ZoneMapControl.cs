using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Ongenet.Core.Audio.Instruments.Sfz;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Visualises an SFZ patch's region map: a key (X, MIDI 0–127) × velocity (Y, 0 bottom – 127 top)
    /// grid with one translucent box per region, coloured by exclusive group. This is the "inspect a
    /// complex layered patch at a glance" view — read-only (editing would mean rewriting the .sfz).
    /// </summary>
    public sealed class ZoneMapControl : ThemedControl
    {
        public static readonly StyledProperty<IReadOnlyList<SfzRegionRuntime>?> ZonesProperty =
            AvaloniaProperty.Register<ZoneMapControl, IReadOnlyList<SfzRegionRuntime>?>(nameof(Zones));

        /// <summary>Bumped to force a repaint when a new patch loads (the list reference may not change).</summary>
        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<ZoneMapControl, int>(nameof(Revision));

        private IBrush _background = Brushes.Black;
        private IPen _gridPen = new Pen(Brushes.Gray, 1);
        private IPen _octavePen = new Pen(Brushes.Gray, 1);
        private Color[] _palette = Array.Empty<Color>();
        private IBrush _emptyBrush = Brushes.Gray;

        // Explicit text typeface instead of Typeface.Default, for consistency with the other
        // custom-drawn controls (keeps glyph resolution off the emoji fallback font).
        private static readonly Typeface LabelTypeface = new(new FontFamily("Inter, Noto Sans, sans-serif"));

        static ZoneMapControl()
        {
            AffectsRender<ZoneMapControl>(ZonesProperty, RevisionProperty);
        }

        public IReadOnlyList<SfzRegionRuntime>? Zones
        {
            get => GetValue(ZonesProperty);
            set => SetValue(ZonesProperty, value);
        }

        public int Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        protected override void BuildThemeResources()
        {
            _background = new SolidColorBrush(ThemePalette.Crust);
            _gridPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Overlay0, 0x40)), 1);
            _octavePen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Overlay0, 0x80)), 1);
            _emptyBrush = new SolidColorBrush(ThemePalette.Subtext0);
            _palette = new[]
            {
                ThemePalette.Mauve, ThemePalette.Blue, ThemePalette.Green, ThemePalette.Peach,
                ThemePalette.Pink, ThemePalette.Teal, ThemePalette.Yellow, ThemePalette.Sky,
                ThemePalette.Lavender, ThemePalette.Maroon
            };
        }

        public override void Render(DrawingContext context)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 2 || h < 2) return;

            context.FillRectangle(_background, new Rect(0, 0, w, h));

            // Octave grid lines (every 12 semitones).
            for (var note = 0; note <= 128; note += 12)
            {
                var x = note / 128.0 * w;
                context.DrawLine(_octavePen, new Point(x, 0), new Point(x, h));
            }

            var zones = Zones;
            if (zones is null || zones.Count == 0)
            {
                var ft = new FormattedText("No SFZ loaded", System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, LabelTypeface, 12, _emptyBrush);
                context.DrawText(ft, new Point(8, h / 2 - 8));
                return;
            }

            foreach (var z in zones)
            {
                var x0 = Math.Clamp(z.LoKey, 0, 127) / 128.0 * w;
                var x1 = (Math.Clamp(z.HiKey, 0, 127) + 1) / 128.0 * w;
                var yTop = (1.0 - Math.Clamp(z.HiVel, 0, 127) / 127.0) * h;
                var yBot = (1.0 - Math.Clamp(z.LoVel, 0, 127) / 127.0) * h;

                var rect = new Rect(x0, yTop, Math.Max(1.0, x1 - x0), Math.Max(1.0, yBot - yTop));
                var color = _palette.Length > 0 ? _palette[Math.Abs(z.Group) % _palette.Length] : Colors.Gray;
                var fill = new SolidColorBrush(ThemePalette.WithAlpha(color, 0x55));
                var border = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(color, 0xCC)), 1);
                context.DrawRectangle(fill, border, rect);
            }
        }
    }
}
