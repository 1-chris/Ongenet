using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ongenet.App.Controls;
using Ongenet.App.Theming;
using Ongenet.App.ViewModels.Field;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.App.Controls.Field;

/// <summary>
/// Freeform canvas that hosts Field surface widgets for both design and playback.
/// Colours come from <see cref="ThemePalette"/> shared brushes so a live theme switch
/// rebuilds the surface without stale snapshot colours.
/// </summary>
public sealed class FieldSurfaceCanvas : Canvas
{
    private FieldSurfaceViewModel? _vm;
    private readonly DispatcherTimer _visualTimer;
    private Point _dragStart;
    private Point _widgetStart;
    private Size _sizeStart;
    private Guid? _dragId;
    private bool _resizing;

    public FieldSurfaceCanvas()
    {
        ClipToBounds = true;
        ApplyCanvasChrome();
        _visualTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _visualTimer.Tick += (_, _) => _vm?.RefreshVisuals();
        DataContextChanged += (_, _) => Attach(DataContext as FieldSurfaceViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemePalette.Changed += OnThemeChanged;
        ApplyCanvasChrome();
        Rebuild();
        _visualTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemePalette.Changed -= OnThemeChanged;
        _visualTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeChanged()
    {
        ApplyCanvasChrome();
        Rebuild();
    }

    private void ApplyCanvasChrome()
    {
        // Shared Application.Resources brushes — Colour mutates in place on theme change.
        Background = ThemePalette.BrushOf("Crust");
    }

    private void Attach(FieldSurfaceViewModel? vm)
    {
        if (_vm is not null) _vm.Widgets.CollectionChanged -= OnWidgetsChanged;
        _vm = vm;
        if (_vm is not null) _vm.Widgets.CollectionChanged += OnWidgetsChanged;
        Rebuild();
    }

    private void OnWidgetsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        if (_vm is null) return;
        Width = _vm.CanvasWidth;
        Height = _vm.CanvasHeight;
        foreach (var widget in _vm.Widgets)
            Children.Add(BuildHost(widget));
    }

    private Control BuildHost(FieldWidgetViewModel widget)
    {
        var border = new Border
        {
            Width = widget.Width,
            Height = widget.Height,
            Tag = widget.Id,
            BorderThickness = new Thickness(widget.IsSelected ? 2 : 1),
            BorderBrush = ThemePalette.BrushOf(widget.IsSelected ? "Mauve" : "Surface1"),
            Background = ThemePalette.BrushOf(
                widget.Kind is FieldWidgetKind.Panel ? "Surface0" : "Mantle"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Opacity = widget.IsBindingResolved || widget.Kind is FieldWidgetKind.Text or FieldWidgetKind.Panel
                or FieldWidgetKind.Divider or FieldWidgetKind.Spacer
                ? 1
                : 0.45
        };
        SetLeft(border, widget.X);
        SetTop(border, widget.Y);
        border.Child = BuildContent(widget);

        border.PointerPressed += (s, e) => OnWidgetPressed(widget, border, e);
        border.PointerMoved += (s, e) => OnWidgetMoved(border, e);
        border.PointerReleased += (s, e) => OnWidgetReleased(widget, border, e);
        border.PointerCaptureLost += (s, e) => { _dragId = null; _resizing = false; };

        return border;
    }

    private Control BuildContent(FieldWidgetViewModel widget)
    {
        var stack = new StackPanel { Spacing = 2 };
        if (!string.IsNullOrWhiteSpace(widget.Label)
            && widget.Kind is not FieldWidgetKind.Text and not FieldWidgetKind.Divider)
        {
            stack.Children.Add(new TextBlock
            {
                Text = widget.Label,
                FontSize = 10,
                Foreground = ThemePalette.BrushOf("Subtext0"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        switch (widget.Kind)
        {
            case FieldWidgetKind.Knob when widget.FloatParam is { } f:
                stack.Children.Add(MakeKnob(f));
                break;
            case FieldWidgetKind.VSlider when widget.FloatParam is { } f:
                stack.Children.Add(MakeSlider(f, Orientation.Vertical));
                break;
            case FieldWidgetKind.HSlider when widget.FloatParam is { } f:
                stack.Children.Add(MakeSlider(f, Orientation.Horizontal));
                break;
            case FieldWidgetKind.Toggle when widget.BoolParam is { } b:
                stack.Children.Add(new CheckBox
                {
                    IsChecked = b.Value,
                    Content = "On",
                    IsEnabled = !_vm!.IsDesignMode,
                    FontSize = 11
                }.Also(cb => cb.IsCheckedChanged += (_, _) =>
                {
                    if (cb.IsChecked is { } v) b.Value = v;
                }));
                break;
            case FieldWidgetKind.Button when widget.BoolParam is { } b:
                stack.Children.Add(new Button
                {
                    Content = widget.Label.Length > 0 ? widget.Label : "Hit",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IsEnabled = !_vm!.IsDesignMode
                }.Also(btn => btn.Click += (_, _) => b.Value = !b.Value));
                break;
            case FieldWidgetKind.Choice when widget.ChoiceParam is { } c:
                var combo = new ComboBox
                {
                    ItemsSource = c.Options,
                    SelectedIndex = c.SelectedIndex,
                    IsEnabled = !_vm!.IsDesignMode,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedIndex >= 0) c.SelectedIndex = combo.SelectedIndex;
                };
                stack.Children.Add(combo);
                break;
            case FieldWidgetKind.ValueReadout when widget.FloatParam is { } f:
                stack.Children.Add(new TextBlock
                {
                    Text = f.Value.ToString(f.Format) + (f.Unit.Length > 0 ? " " + f.Unit : ""),
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ThemePalette.BrushOf("Text")
                });
                break;
            case FieldWidgetKind.XYPad:
                stack.Children.Add(new Border
                {
                    Background = ThemePalette.BrushOf("Crust"),
                    BorderBrush = ThemePalette.BrushOf("Surface1"),
                    BorderThickness = new Thickness(1),
                    Height = Math.Max(40, widget.Height - 24),
                    Child = new TextBlock
                    {
                        Text = "XY",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = ThemePalette.BrushOf("Overlay1")
                    }
                }.Also(pad =>
                {
                    if (_vm!.IsDesignMode || widget.FloatParam is null) return;
                    pad.PointerMoved += (_, e) =>
                    {
                        if (e.GetCurrentPoint(pad).Properties.IsLeftButtonPressed)
                        {
                            var p = e.GetPosition(pad);
                            var nx = Math.Clamp(p.X / Math.Max(1, pad.Bounds.Width), 0, 1);
                            var ny = 1 - Math.Clamp(p.Y / Math.Max(1, pad.Bounds.Height), 0, 1);
                            widget.FloatParam.Value = widget.FloatParam.Min +
                                (widget.FloatParam.Max - widget.FloatParam.Min) * nx;
                            if (widget.SecondaryFloatParam is { } y)
                                y.Value = y.Min + (y.Max - y.Min) * ny;
                        }
                    };
                }));
                break;
            case FieldWidgetKind.Text:
                stack.Children.Add(new TextBlock
                {
                    Text = widget.Label,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ThemePalette.BrushOf("Text"),
                    TextWrapping = TextWrapping.Wrap
                });
                break;
            case FieldWidgetKind.Divider:
                stack.Children.Add(new Rectangle
                {
                    Height = 2,
                    Fill = ThemePalette.BrushOf("Surface1"),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
                break;
            case FieldWidgetKind.LevelMeter:
                stack.Children.Add(new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 1,
                    Value = widget.Level,
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Width = 14,
                    Height = Math.Max(40, widget.Height - 24),
                    Foreground = ThemePalette.BrushOf("Mauve")
                });
                break;
            case FieldWidgetKind.Oscilloscope:
                stack.Children.Add(new ScopePolyControl
                {
                    Height = Math.Max(40, widget.Height - 24),
                    Samples = widget.Waveform,
                    Revision = widget.WaveformRevision
                });
                break;
            case FieldWidgetKind.EnvelopeDisplay:
                stack.Children.Add(new EnvelopeDisplayControl
                {
                    MinHeight = 40,
                    Height = Math.Max(40, widget.Height - 24),
                    Attack = widget.EnvAttack,
                    Decay = widget.EnvDecay,
                    Sustain = widget.EnvSustain,
                    Release = widget.EnvRelease
                });
                break;
            default:
                if (!widget.IsBindingResolved && widget.Kind is not FieldWidgetKind.Panel
                    and not FieldWidgetKind.Spacer)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = "Unbound",
                        FontSize = 10,
                        Foreground = ThemePalette.BrushOf("Overlay1")
                    });
                }

                break;
        }

        return stack;
    }

    private Control MakeKnob(FloatParameter f)
    {
        var knob = new Knob
        {
            Width = 48,
            Height = 48,
            Minimum = f.Min,
            Maximum = f.Max,
            Skew = f.Skew,
            Value = f.Value,
            IsEnabled = _vm is { IsDesignMode: false }
        };
        knob.PropertyChanged += (_, e) =>
        {
            if (e.Property == Knob.ValueProperty) f.Value = knob.Value;
        };
        return knob;
    }

    private Control MakeSlider(FloatParameter f, Orientation orientation)
    {
        var slider = new Slider
        {
            Minimum = f.Min,
            Maximum = f.Max,
            Value = f.Value,
            Orientation = orientation,
            IsEnabled = _vm is { IsDesignMode: false }
        };
        if (orientation == Orientation.Vertical)
        {
            slider.Height = 90;
            slider.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else slider.HorizontalAlignment = HorizontalAlignment.Stretch;

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) f.Value = slider.Value;
        };
        return slider;
    }

    private void OnWidgetPressed(FieldWidgetViewModel widget, Border border, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        _vm.SelectedWidget = widget;
        border.BorderBrush = ThemePalette.BrushOf("Mauve");
        border.BorderThickness = new Thickness(2);

        if (!_vm.IsDesignMode) return;
        var point = e.GetCurrentPoint(border);
        _dragStart = e.GetPosition(this);
        _widgetStart = new Point(widget.X, widget.Y);
        _sizeStart = new Size(widget.Width, widget.Height);
        _dragId = widget.Id;
        _resizing = point.Position.X > border.Bounds.Width - 10 && point.Position.Y > border.Bounds.Height - 10;
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void OnWidgetMoved(Border border, PointerEventArgs e)
    {
        if (_vm is null || _dragId is null || !_vm.IsDesignMode) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        if (_resizing)
        {
            border.Width = Math.Max(24, _sizeStart.Width + dx);
            border.Height = Math.Max(24, _sizeStart.Height + dy);
        }
        else
        {
            SetLeft(border, _widgetStart.X + dx);
            SetTop(border, _widgetStart.Y + dy);
        }
    }

    private void OnWidgetReleased(FieldWidgetViewModel widget, Border border, PointerReleasedEventArgs e)
    {
        if (_vm is null || _dragId is null) return;
        e.Pointer.Capture(null);
        var x = GetLeft(border);
        var y = GetTop(border);
        _vm.ApplyGeometry(widget.Id, x, y, border.Width, border.Height);
        _dragId = null;
        _resizing = false;
        Rebuild();
    }
}

internal static class ControlExtensions
{
    public static T Also<T>(this T control, Action<T> configure)
    {
        configure(control);
        return control;
    }
}

/// <summary>Lightweight oscilloscope polyline for surface widgets — theme-aware via <see cref="ThemedControl"/>.</summary>
internal sealed class ScopePolyControl : ThemedControl
{
    public static readonly StyledProperty<float[]> SamplesProperty =
        AvaloniaProperty.Register<ScopePolyControl, float[]>(nameof(Samples), Array.Empty<float>());

    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<ScopePolyControl, int>(nameof(Revision));

    private IBrush _background = Brushes.Transparent;
    private IPen _wavePen = new Pen(Brushes.Gray, 1.2);

    public float[] Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public int Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    static ScopePolyControl()
    {
        AffectsRender<ScopePolyControl>(SamplesProperty, RevisionProperty);
    }

    protected override void BuildThemeResources()
    {
        _background = ThemePalette.BrushOf("Crust");
        _wavePen = new Pen(ThemePalette.BrushOf("Mauve"), 1.2);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(_background, bounds);
        var samples = Samples;
        if (samples.Length < 2) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (var i = 0; i < samples.Length; i++)
            {
                var x = bounds.Width * i / (samples.Length - 1.0);
                var y = bounds.Height * 0.5 * (1 - Math.Clamp(samples[i], -1, 1));
                if (i == 0) g.BeginFigure(new Point(x, y), false);
                else g.LineTo(new Point(x, y));
            }

            g.EndFigure(false);
        }

        context.DrawGeometry(null, _wavePen, geo);
    }
}
