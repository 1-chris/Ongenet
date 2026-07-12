namespace Ongenet.Core.Audio.Midi;

using System;

/// <summary>Session-view actions that can be triggered by MIDI learn or controller definitions.</summary>
public enum SessionMidiAction
{
    LaunchSlot,
    LaunchScene,
    QueueSlot,
    StopSlot,
    StopScene,
    StopAll,
    GateOn,
    GateOff
}

/// <summary>A learned or imported MIDI binding to a session action.</summary>
public sealed class SessionMidiMapping
{
    public SessionMidiAction Action { get; init; }
    public bool IsNote { get; init; }
    public int Channel { get; init; } = -1;
    public int Number { get; init; }
    public string? SourceDeviceId { get; init; }
    public Guid? TrackId { get; init; }
    public int? SceneIndex { get; init; }
}
