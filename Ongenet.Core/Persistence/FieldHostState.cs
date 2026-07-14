using System;
using Ongenet.Core.Audio.Field;

namespace Ongenet.Core.Persistence;

/// <summary>
/// Versioned custom-state envelope for <see cref="FieldInstrument"/> / <see cref="FieldEffect"/>.
/// Legacy projects store the graph (or source-track + graph) directly; new state starts with
/// <see cref="Magic"/> so readers can branch without consuming the graph version byte twice.
/// </summary>
public static class FieldHostState
{
    /// <summary>Marker written before the envelope version. Graph serializer versions are 1–2.</summary>
    public const int Magic = 1000;
    public const int EnvelopeVersion = 1;

    public static void WriteInstrument(OngenWriter w, string typeId, string displayName, Guid? definitionId,
        FieldSurfaceDefinition surface, FieldGraph graph)
    {
        w.WriteInt(Magic);
        w.WriteInt(EnvelopeVersion);
        w.WriteString(typeId);
        w.WriteString(displayName);
        w.WriteNullableGuid(definitionId);
        FieldSurfaceSerializer.Write(w, surface);
        FieldGraphSerializer.Write(w, graph);
    }

    public static void WriteEffect(OngenWriter w, Guid? sourceTrackId, string typeId, string displayName,
        Guid? definitionId, FieldSurfaceDefinition surface, FieldGraph graph)
    {
        w.WriteNullableGuid(sourceTrackId);
        w.WriteInt(Magic);
        w.WriteInt(EnvelopeVersion);
        w.WriteString(typeId);
        w.WriteString(displayName);
        w.WriteNullableGuid(definitionId);
        FieldSurfaceSerializer.Write(w, surface);
        FieldGraphSerializer.Write(w, graph);
    }

    public static void ReadInstrument(OngenReader r, out string typeId, out string displayName,
        out Guid? definitionId, out FieldSurfaceDefinition surface, FieldGraph graph, IFieldNodeRegistry registry)
    {
        var marker = r.ReadInt();
        if (marker == Magic)
        {
            _ = r.ReadInt(); // envelope version
            typeId = r.ReadString();
            displayName = r.ReadString();
            definitionId = r.ReadNullableGuid();
            surface = FieldSurfaceSerializer.Read(r);
            FieldGraphSerializer.Read(r, graph, registry);
            return;
        }

        // Legacy: first int was the FieldGraphSerializer version.
        typeId = FieldInstrument.Id;
        displayName = "Field";
        definitionId = null;
        surface = new FieldSurfaceDefinition();
        FieldGraphSerializer.Read(r, graph, registry, alreadyReadVersion: marker);
    }

    public static void ReadEffect(OngenReader r, out Guid? sourceTrackId, out string typeId, out string displayName,
        out Guid? definitionId, out FieldSurfaceDefinition surface, FieldGraph graph, IFieldNodeRegistry registry)
    {
        sourceTrackId = r.ReadNullableGuid();
        var marker = r.ReadInt();
        if (marker == Magic)
        {
            _ = r.ReadInt();
            typeId = r.ReadString();
            displayName = r.ReadString();
            definitionId = r.ReadNullableGuid();
            surface = FieldSurfaceSerializer.Read(r);
            FieldGraphSerializer.Read(r, graph, registry);
            return;
        }

        typeId = FieldEffect.Id;
        displayName = "Field";
        definitionId = null;
        surface = new FieldSurfaceDefinition();
        FieldGraphSerializer.Read(r, graph, registry, alreadyReadVersion: marker);
    }
}
