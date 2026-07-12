using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>Target for an in-progress session MIDI learn (action + optional slot/scene context).</summary>
public sealed class SessionLearnTarget
{
    public required SessionMidiAction Action { get; init; }
    public Guid? TrackId { get; init; }
    public int? SceneIndex { get; init; }
}

/// <summary>
/// Maps MIDI controls to session-view actions (launch/stop slots and scenes), learns new bindings,
/// and triggers actions when matching messages arrive. Mappings are per-project, persisted in
/// <see cref="Models.Audio.Project.SessionMidiMappings"/>.
/// </summary>
public interface ISessionMidiMapService
{
    IReadOnlyList<SessionMidiMapping> Mappings { get; }

    /// <summary>The session action currently being learned, or null.</summary>
    SessionLearnTarget? LearnTarget { get; }

    /// <summary>Arms learn: the next note/CC press binds to the given action and slot/scene context.</summary>
    void BeginLearn(SessionMidiAction action, Guid? trackId = null, int? sceneIndex = null);

    /// <summary>Cancels a pending learn.</summary>
    void CancelLearn();

    /// <summary>Removes mappings matching the action and optional slot/scene context.</summary>
    void ClearMapping(SessionMidiAction action, Guid? trackId = null, int? sceneIndex = null);

    /// <summary>
    /// Handles an incoming message (MIDI thread). Returns true if it completed a learn or triggered an
    /// action. Note On, Note Off (gate off), and CC button presses are considered.
    /// </summary>
    bool HandleMessage(MidiMessage message);

    /// <summary>Replaces all mappings (e.g. restoring from the project on load).</summary>
    void SetMappings(IEnumerable<SessionMidiMapping> mappings);

    event Action? MappingsChanged;
    event Action? LearnStateChanged;
}
