using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Controls;

/// <summary>Graphical piano-key / velocity map for sampler zones, coloured by layer.</summary>
public sealed class SamplerZoneMapControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<SamplerZoneRowViewModel>?> ZonesProperty =
        AvaloniaProperty.Register<SamplerZoneMapControl, IReadOnlyList<SamplerZoneRowViewModel>?>(nameof(Zones));

    public static readonly StyledProperty<int?> SelectedIndexProperty =
        AvaloniaProperty.Register<SamplerZoneMapControl, int?>(nameof(SelectedIndex));

    public IReadOnlyList<SamplerZoneRowViewModel>? Zones
    {
        get => GetValue(ZonesProperty);
        set => SetValue(ZonesProperty, value);
    }

    public int? SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    static SamplerZoneMapControl()
    {
        AffectsRender<SamplerZoneMapControl>(ZonesProperty, SelectedIndexProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width < 8 || bounds.Height < 8) return;

        var zones = Zones;
        var keyHeight = Math.Max(4, (bounds.Height - 24) / 12f);
        var keyWidth = bounds.Width - 48;

        context.FillRectangle(Brush.Parse("#1e1e2e"), bounds);

        context.DrawText(new FormattedText("Velocity →",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("fonts:Inter#Inter"), 9, Brush.Parse("#a6adc8")), new Point(44, 2));

        for (var octave = 0; octave < 11; octave++)
        {
            var baseY = 18 + octave * keyHeight;
            if (baseY + keyHeight > bounds.Height) break;
            DrawKeyRow(context, 40, baseY, keyWidth, keyHeight, octave * 12, zones);
        }

        for (var n = 0; n <= 127; n += 12)
        {
            var row = n / 12;
            var y = 18 + row * keyHeight;
            if (y > bounds.Height) break;
            var label = MidiName(n);
            context.DrawText(new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("fonts:Inter#Inter"), 8, Brush.Parse("#6c7086")),
                new Point(2, y));
        }
    }

    private void DrawKeyRow(DrawingContext ctx, double x, double y, double w, double h, int baseNote,
        IReadOnlyList<SamplerZoneRowViewModel>? zones)
    {
        ctx.FillRectangle(Brush.Parse("#313244"), new Rect(x, y, w, h));
        if (zones is null) return;

        for (var i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (z.HiKey < baseNote || z.LoKey >= baseNote + 12) continue;
            var lo = Math.Max(z.LoKey, baseNote);
            var hi = Math.Min(z.HiKey, baseNote + 11);
            var left = x + (lo - baseNote) / 12.0 * w;
            var right = x + (hi - baseNote + 1) / 12.0 * w;
            var velFrac = (z.HiVel - z.LoVel + 1) / 127.0;
            var zoneH = Math.Max(3, h * velFrac * 0.85);
            var color = z.LayerColorArgb != 0
                ? Color.FromUInt32(z.LayerColorArgb)
                : Color.Parse("#89b4fa");
            if (SelectedIndex == i) color = Color.Parse("#f5e0dc");
            var brush = new SolidColorBrush(Color.FromArgb(0xBB, color.R, color.G, color.B));
            ctx.FillRectangle(brush, new Rect(left, y + (h - zoneH) * 0.5, right - left, zoneH));
        }
    }

    private static string MidiName(int note)
    {
        var names = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return names[note % 12] + (note / 12 - 1);
    }
}
