using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Field;

/// <summary>Kind of a widget on a user Field control surface.</summary>
public enum FieldWidgetKind
{
    Knob = 0,
    VSlider = 1,
    HSlider = 2,
    Toggle = 3,
    Button = 4,
    Choice = 5,
    XYPad = 6,
    ValueReadout = 7,
    Text = 8,
    Panel = 9,
    Divider = 10,
    Spacer = 11,
    LevelMeter = 12,
    Oscilloscope = 13,
    EnvelopeDisplay = 14
}

/// <summary>What a widget is bound to.</summary>
public enum FieldWidgetBindingKind
{
    None = 0,
    Parameter = 1,
    Signal = 2
}

/// <summary>Persisted parameter kind expected at a binding target.</summary>
public enum FieldBoundParamKind
{
    Float = 0,
    Bool = 1,
    Choice = 2
}

/// <summary>Stable link from a surface control to a graph node parameter.</summary>
public sealed class FieldParameterBinding
{
    public Guid NodeId { get; set; }
    public int ParamIndex { get; set; }
    public FieldBoundParamKind ExpectedKind { get; set; }
}

/// <summary>Link from a visual widget to a node (and optional output port) for meters/scopes.</summary>
public sealed class FieldSignalBinding
{
    public Guid NodeId { get; set; }
    public string PortId { get; set; } = "";
}

/// <summary>
/// One control exposed at the instrument/effect parameter list (automation / presets). Order of these
/// entries is stable and must not be rearranged silently — serializers and automation bind by index.
/// </summary>
public sealed class FieldExposedControl
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NodeId { get; set; }
    public int ParamIndex { get; set; }
    public FieldBoundParamKind ExpectedKind { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Group { get; set; }
}

/// <summary>A freeform surface widget with layout and optional binding.</summary>
public sealed class FieldWidget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public FieldWidgetKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 64;
    public double Height { get; set; } = 64;
    public int ZOrder { get; set; }
    public string Label { get; set; } = "";
    public FieldWidgetBindingKind BindingKind { get; set; }
    public FieldParameterBinding? ParameterBinding { get; set; }
    public FieldSignalBinding? SignalBinding { get; set; }

    /// <summary>For XYPad: secondary float parameter binding (Y axis).</summary>
    public FieldParameterBinding? SecondaryParameterBinding { get; set; }
}

/// <summary>Declarative freeform control surface for a user Field definition.</summary>
public sealed class FieldSurfaceDefinition
{
    public double CanvasWidth { get; set; } = 480;
    public double CanvasHeight { get; set; } = 280;
    public List<FieldWidget> Widgets { get; set; } = new();
    public List<FieldExposedControl> ExposedControls { get; set; } = new();

    public FieldSurfaceDefinition Clone()
    {
        var copy = new FieldSurfaceDefinition
        {
            CanvasWidth = CanvasWidth,
            CanvasHeight = CanvasHeight
        };
        foreach (var w in Widgets)
        {
            copy.Widgets.Add(new FieldWidget
            {
                Id = w.Id,
                Kind = w.Kind,
                X = w.X,
                Y = w.Y,
                Width = w.Width,
                Height = w.Height,
                ZOrder = w.ZOrder,
                Label = w.Label,
                BindingKind = w.BindingKind,
                ParameterBinding = CloneParam(w.ParameterBinding),
                SecondaryParameterBinding = CloneParam(w.SecondaryParameterBinding),
                SignalBinding = w.SignalBinding is null
                    ? null
                    : new FieldSignalBinding { NodeId = w.SignalBinding.NodeId, PortId = w.SignalBinding.PortId }
            });
        }

        foreach (var e in ExposedControls)
        {
            copy.ExposedControls.Add(new FieldExposedControl
            {
                Id = e.Id,
                NodeId = e.NodeId,
                ParamIndex = e.ParamIndex,
                ExpectedKind = e.ExpectedKind,
                DisplayName = e.DisplayName,
                Group = e.Group
            });
        }

        return copy;
    }

    private static FieldParameterBinding? CloneParam(FieldParameterBinding? b)
        => b is null ? null : new FieldParameterBinding
        {
            NodeId = b.NodeId,
            ParamIndex = b.ParamIndex,
            ExpectedKind = b.ExpectedKind
        };
}
