using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Ongenet.App.Theming;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// Draws a monospace time string (e.g. the transport playhead readout) in <see cref="Render"/>
    /// instead of via a bound <c>TextBlock</c>. The playhead time changes ~30x/sec while the transport
    /// runs; pushing that through a TextBlock re-runs its (expensive) text-measure layout pass every
    /// frame, which dragged the whole window down to ~13fps. Here <see cref="MeasureOverride"/> returns
    /// a constant size and <see cref="TextProperty"/> only invalidates render, so updates never touch
    /// the layout pass — same pattern as the meter controls.
    /// </summary>
    public sealed class TimeReadoutControl : ThemedControl
    {
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<TimeReadoutControl, string>(nameof(Text), "0:00.000");

        public static readonly StyledProperty<double> FontSizeProperty =
            AvaloniaProperty.Register<TimeReadoutControl, double>(nameof(FontSize), 13.0);

        // Resolved once (statically), so the family chain is never re-matched per frame.
        private static readonly Typeface MonoTypeface =
            new(new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"));

        private IBrush _brush = Brushes.White;
        private Size _cachedSize;

        protected override void BuildThemeResources() => _brush = new SolidColorBrush(ThemePalette.Text);

        static TimeReadoutControl()
        {
            AffectsRender<TimeReadoutControl>(TextProperty, FontSizeProperty);
            // Font size affects the reserved box; text does not (constant-width monospace readout).
            FontSizeProperty.Changed.AddClassHandler<TimeReadoutControl>((c, _) => { c._cachedSize = default; c.InvalidateMeasure(); });
        }

        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

        // Constant box sized to the widest readout ("0:00.000"), measured once. Returning a fixed size
        // means a text change never invalidates measure, so layout stays O(1) regardless of update rate.
        protected override Size MeasureOverride(Size availableSize)
        {
            if (_cachedSize == default)
            {
                var template = Build("00:00.000"); // headroom for 2-digit minutes + milliseconds
                _cachedSize = new Size(Math.Ceiling(template.Width) + 1, Math.Ceiling(template.Height));
            }

            return _cachedSize;
        }

        public override void Render(DrawingContext context)
        {
            var text = Build(Text ?? string.Empty);
            context.DrawText(text, new Point(0, (Bounds.Height - text.Height) / 2));
        }

        private FormattedText Build(string s) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoTypeface, FontSize, _brush);
    }
}
