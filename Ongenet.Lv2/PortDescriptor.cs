using System.Collections.Generic;

namespace Ongenet.Lv2;

/// <summary>The signal carried by an LV2 port (from its <c>rdf:type</c> classes).</summary>
public enum PortKind { Audio, Control, Cv, Atom, Event, Unknown }

/// <summary>Port data-flow direction.</summary>
public enum PortDirection { Input, Output }

/// <summary>One label/value pair from an enumerated control port's <c>lv2:scalePoint</c>.</summary>
public readonly record struct ScalePoint(string Label, double Value);

/// <summary>
/// A single LV2 port, parsed from the bundle Turtle. Audio/CV ports carry sample buffers; input
/// control ports become editable <see cref="Ongenet.Core.Audio.Parameters.Parameter"/>s; atom ports
/// carry MIDI (instruments) or are connected as empty/chunk buffers. Ranges/properties come from the
/// metadata, not the binary.
/// </summary>
public sealed class PortDescriptor
{
    public required int Index { get; init; }
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required PortKind Kind { get; init; }
    public required PortDirection Direction { get; init; }

    public float Default { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
    public bool HasRange { get; init; }

    public bool Toggled { get; init; }
    public bool Integer { get; init; }
    public bool Enumeration { get; init; }
    public bool SampleRate { get; init; }   // value is multiplied by the sample rate
    public bool Logarithmic { get; init; }
    public bool ConnectionOptional { get; init; }

    /// <summary>True for an atom/event input port that accepts <c>midi:MidiEvent</c> (note input).</summary>
    public bool SupportsMidi { get; init; }

    public IReadOnlyList<ScalePoint> ScalePoints { get; init; } = System.Array.Empty<ScalePoint>();

    public bool IsAudio => Kind is PortKind.Audio or PortKind.Cv;
    public bool IsControl => Kind == PortKind.Control;
    public bool IsAtomOrEvent => Kind is PortKind.Atom or PortKind.Event;
}
