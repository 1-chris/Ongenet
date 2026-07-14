using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Field;

namespace Ongenet.Core.Persistence;

/// <summary>Reads/writes a <see cref="FieldSurfaceDefinition"/> inside a Field host state blob or definition package.</summary>
public static class FieldSurfaceSerializer
{
    private const int Version = 1;

    public static void Write(OngenWriter w, FieldSurfaceDefinition surface)
    {
        w.WriteInt(Version);
        w.WriteDouble(surface.CanvasWidth);
        w.WriteDouble(surface.CanvasHeight);

        w.WriteInt(surface.Widgets.Count);
        foreach (var widget in surface.Widgets)
        {
            w.WriteChunk(c =>
            {
                c.WriteGuid(widget.Id);
                c.WriteInt((int)widget.Kind);
                c.WriteDouble(widget.X);
                c.WriteDouble(widget.Y);
                c.WriteDouble(widget.Width);
                c.WriteDouble(widget.Height);
                c.WriteInt(widget.ZOrder);
                c.WriteString(widget.Label);
                c.WriteInt((int)widget.BindingKind);
                WriteParamBinding(c, widget.ParameterBinding);
                WriteParamBinding(c, widget.SecondaryParameterBinding);
                if (widget.SignalBinding is { } sig)
                {
                    c.WriteBool(true);
                    c.WriteGuid(sig.NodeId);
                    c.WriteString(sig.PortId);
                }
                else c.WriteBool(false);
            });
        }

        w.WriteInt(surface.ExposedControls.Count);
        foreach (var exposed in surface.ExposedControls)
        {
            w.WriteChunk(c =>
            {
                c.WriteGuid(exposed.Id);
                c.WriteGuid(exposed.NodeId);
                c.WriteInt(exposed.ParamIndex);
                c.WriteInt((int)exposed.ExpectedKind);
                c.WriteString(exposed.DisplayName);
                c.WriteString(exposed.Group ?? "");
            });
        }
    }

    public static FieldSurfaceDefinition Read(OngenReader r)
    {
        var surface = new FieldSurfaceDefinition();
        _ = r.ReadInt(); // version
        surface.CanvasWidth = r.ReadDouble();
        surface.CanvasHeight = r.ReadDouble();

        var widgetCount = r.ReadInt();
        for (var i = 0; i < widgetCount; i++)
        {
            r.ReadChunk(c =>
            {
                var widget = new FieldWidget
                {
                    Id = c.ReadGuid(),
                    Kind = (FieldWidgetKind)c.ReadInt(),
                    X = c.ReadDouble(),
                    Y = c.ReadDouble(),
                    Width = c.ReadDouble(),
                    Height = c.ReadDouble(),
                    ZOrder = c.ReadInt(),
                    Label = c.ReadString(),
                    BindingKind = (FieldWidgetBindingKind)c.ReadInt(),
                    ParameterBinding = ReadParamBinding(c),
                    SecondaryParameterBinding = ReadParamBinding(c)
                };
                if (c.ReadBool())
                {
                    widget.SignalBinding = new FieldSignalBinding
                    {
                        NodeId = c.ReadGuid(),
                        PortId = c.ReadString()
                    };
                }

                surface.Widgets.Add(widget);
            });
        }

        var exposedCount = r.ReadInt();
        for (var i = 0; i < exposedCount; i++)
        {
            r.ReadChunk(c =>
            {
                var id = c.ReadGuid();
                var nodeId = c.ReadGuid();
                var paramIndex = c.ReadInt();
                var kind = (FieldBoundParamKind)c.ReadInt();
                var displayName = c.ReadString();
                var group = c.ReadString();
                surface.ExposedControls.Add(new FieldExposedControl
                {
                    Id = id,
                    NodeId = nodeId,
                    ParamIndex = paramIndex,
                    ExpectedKind = kind,
                    DisplayName = displayName,
                    Group = string.IsNullOrEmpty(group) ? null : group
                });
            });
        }

        return surface;
    }

    private static void WriteParamBinding(OngenWriter w, FieldParameterBinding? binding)
    {
        if (binding is null)
        {
            w.WriteBool(false);
            return;
        }

        w.WriteBool(true);
        w.WriteGuid(binding.NodeId);
        w.WriteInt(binding.ParamIndex);
        w.WriteInt((int)binding.ExpectedKind);
    }

    private static FieldParameterBinding? ReadParamBinding(OngenReader r)
    {
        if (!r.ReadBool()) return null;
        return new FieldParameterBinding
        {
            NodeId = r.ReadGuid(),
            ParamIndex = r.ReadInt(),
            ExpectedKind = (FieldBoundParamKind)r.ReadInt()
        };
    }

    /// <summary>Repair helpers used when the exposed-control list drifted from widgets.</summary>
    public static void EnsureExposedFromParameterWidgets(FieldSurfaceDefinition surface)
    {
        if (surface.ExposedControls.Count > 0) return;
        var seen = new HashSet<(Guid, int)>();
        foreach (var widget in surface.Widgets)
        {
            if (widget.BindingKind != FieldWidgetBindingKind.Parameter || widget.ParameterBinding is not { } b)
                continue;
            var key = (b.NodeId, b.ParamIndex);
            if (!seen.Add(key)) continue;
            surface.ExposedControls.Add(new FieldExposedControl
            {
                NodeId = b.NodeId,
                ParamIndex = b.ParamIndex,
                ExpectedKind = b.ExpectedKind,
                DisplayName = string.IsNullOrWhiteSpace(widget.Label) ? $"Control {surface.ExposedControls.Count + 1}" : widget.Label
            });
        }
    }
}
