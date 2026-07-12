using System.Collections.Generic;

namespace Ongenet.Core.Audio.Midi;

/// <summary>JSON-serializable control-surface definition (<c>.ongencontroller</c>).</summary>
public sealed class ControlSurfaceDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ControlSurfaceMatch? Match { get; set; }
    public List<string> Ports { get; set; } = new();
    public List<ControlSurfaceBinding> Bindings { get; set; } = new();
    public List<ControlSurfaceFeedback> Feedback { get; set; } = new();
}

/// <summary>Heuristic device matching for auto-selecting a definition.</summary>
public sealed class ControlSurfaceMatch
{
    public List<string> PortNameContains { get; set; } = new();
}

/// <summary>One MIDI control mapped to a transport, session, or mixer action.</summary>
public sealed class ControlSurfaceBinding
{
    /// <summary>
    /// Action id: transport (<c>PlayPause</c>, <c>Stop</c>, <c>Record</c>), session
    /// (<c>LaunchScene</c>, <c>LaunchSlot</c>, …), or mixer (<c>MixerVolume</c>, <c>MixerPan</c>,
    /// <c>MixerMute</c>, <c>MixerSolo</c>, <c>MixerSend</c>).
    /// </summary>
    public string Action { get; set; } = "";

    public bool IsNote { get; set; }
    public int Channel { get; set; } = -1;
    public int Number { get; set; }
    public int? SceneIndex { get; set; }
    public int? TrackIndex { get; set; }
    public int? MixerChannel { get; set; }
    public string? MixerTarget { get; set; }
}

/// <summary>Outgoing LED/fader feedback rule (reserved for future use).</summary>
public sealed class ControlSurfaceFeedback
{
    public string Action { get; set; } = "";
    public bool IsNote { get; set; }
    public int Channel { get; set; } = -1;
    public int Number { get; set; }
}
