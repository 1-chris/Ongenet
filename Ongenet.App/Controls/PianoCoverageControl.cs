using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Ongenet.App.Theming;

namespace Ongenet.App.Controls;

/// <summary>
/// MIDI key coverage strip (0–127). Covered keys use layer colours; press/hold plays a preview note.
/// </summary>
public sealed class PianoCoverageControl : ThemedControl
{
    public static readonly StyledProperty<IReadOnlyList<bool>?> CoverageProperty =
        AvaloniaProperty.Register<PianoCoverageControl, IReadOnlyList<bool>?>(nameof(Coverage));

    public static readonly StyledProperty<IReadOnlyList<uint>?> KeyColorsProperty =
        AvaloniaProperty.Register<PianoCoverageControl, IReadOnlyList<uint>?>(nameof(KeyColors));

    public static readonly StyledProperty<int> SelectedKeyProperty =
        AvaloniaProperty.Register<PianoCoverageControl, int>(nameof(SelectedKey), -1);

    public static readonly StyledProperty<int> HoverKeyProperty =
        AvaloniaProperty.Register<PianoCoverageControl, int>(nameof(HoverKey), -1);

    private IBrush _bg = Brushes.Black;
    private IBrush _white = Brushes.White;
    private IBrush _black = Brushes.Gray;
    private IBrush _cover = Brushes.Blue;
    private IBrush _sel = Brushes.Orange;
    private int _heldKey = -1;

    static PianoCoverageControl()
    {
        AffectsRender<PianoCoverageControl>(CoverageProperty, KeyColorsProperty, SelectedKeyProperty, HoverKeyProperty);
    }

    /// <summary>Raised when the pointer presses a key (preview NoteOn).</summary>
    public event Action<int>? NotePreviewOn;

    /// <summary>Raised when the pointer releases a held key (preview NoteOff).</summary>
    public event Action<int>? NotePreviewOff;

    public IReadOnlyList<bool>? Coverage
    {
        get => GetValue(CoverageProperty);
        set => SetValue(CoverageProperty, value);
    }

    /// <summary>Per-key opaque ARGB colours (0 = uncovered / use flat coverage tint).</summary>
    public IReadOnlyList<uint>? KeyColors
    {
        get => GetValue(KeyColorsProperty);
        set => SetValue(KeyColorsProperty, value);
    }

    public int SelectedKey
    {
        get => GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    public int HoverKey
    {
        get => GetValue(HoverKeyProperty);
        set => SetValue(HoverKeyProperty, value);
    }

    protected override void BuildThemeResources()
    {
        _bg = new SolidColorBrush(ThemePalette.Crust);
        _white = new SolidColorBrush(ThemePalette.Surface0);
        _black = new SolidColorBrush(ThemePalette.Surface2);
        _cover = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Mauve, 0x99));
        _sel = new SolidColorBrush(ThemePalette.Peach);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var key = KeyAt(e.GetPosition(this).X);
        HoverKey = key;
        if (_heldKey >= 0 && key >= 0 && key != _heldKey)
        {
            NotePreviewOff?.Invoke(_heldKey);
            _heldKey = key;
            SelectedKey = key;
            NotePreviewOn?.Invoke(key);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var key = KeyAt(e.GetPosition(this).X);
        if (key < 0) return;
        e.Pointer.Capture(this);
        SelectedKey = key;
        if (_heldKey >= 0 && _heldKey != key)
            NotePreviewOff?.Invoke(_heldKey);
        _heldKey = key;
        NotePreviewOn?.Invoke(key);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ReleaseHeld();
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ReleaseHeld();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoverKey = -1;
    }

    private void ReleaseHeld()
    {
        if (_heldKey < 0) return;
        NotePreviewOff?.Invoke(_heldKey);
        _heldKey = -1;
    }

    private int KeyAt(double x)
    {
        var w = Bounds.Width;
        if (w < 1) return -1;
        return Math.Clamp((int)(x / w * 128.0), 0, 127);
    }

    private static bool IsBlack(int key)
    {
        var n = key % 12;
        return n is 1 or 3 or 6 or 8 or 10;
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 2 || h < 2) return;
        context.FillRectangle(_bg, new Rect(0, 0, w, h));
        var cov = Coverage;
        var colors = KeyColors;
        var keyW = w / 128.0;
        for (var k = 0; k < 128; k++)
        {
            var x = k * keyW;
            var brush = IsBlack(k) ? _black : _white;
            context.FillRectangle(brush, new Rect(x, 0, Math.Max(1, keyW - 0.5), h));

            uint argb = 0;
            if (colors is not null && k < colors.Count) argb = colors[k];
            if (argb != 0)
            {
                var c = Color.FromUInt32(argb);
                var tint = new SolidColorBrush(Color.FromArgb(0xAA, c.R, c.G, c.B));
                context.FillRectangle(tint, new Rect(x, h * 0.3, Math.Max(1, keyW - 0.5), h * 0.7));
            }
            else if (cov is not null && k < cov.Count && cov[k])
            {
                context.FillRectangle(_cover, new Rect(x, h * 0.35, Math.Max(1, keyW - 0.5), h * 0.65));
            }

            if (k == SelectedKey || k == HoverKey || k == _heldKey)
                context.FillRectangle(_sel, new Rect(x, 0, Math.Max(1, keyW), 3));
        }
    }
}
