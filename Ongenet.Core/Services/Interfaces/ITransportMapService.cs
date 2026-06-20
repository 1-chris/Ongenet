using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Maps MIDI controls (notes / CC buttons) to transport actions (play-pause, stop, record), learns
/// new bindings, and triggers the actions when matching messages arrive. Mappings are global controller
/// setup persisted in the app settings file.
/// </summary>
public interface ITransportMapService
{
    IReadOnlyList<TransportMapping> Mappings { get; }

    /// <summary>The action currently being learned (the next control binds to it), or null.</summary>
    TransportAction? LearnAction { get; }

    /// <summary>Arms learn for <paramref name="action"/>: the next note/CC press binds to it.</summary>
    void BeginLearn(TransportAction action);

    /// <summary>Cancels a pending learn.</summary>
    void CancelLearn();

    /// <summary>Removes the mapping (if any) for <paramref name="action"/>.</summary>
    void ClearMapping(TransportAction action);

    /// <summary>Returns the control bound to <paramref name="action"/>, or null.</summary>
    TransportMapping? MappingFor(TransportAction action);

    /// <summary>
    /// Handles an incoming message (MIDI thread). Returns true if it completed a learn or triggered an
    /// action, so the caller should not also route it to the instrument. Only Note On and Control Change
    /// are considered triggers.
    /// </summary>
    bool HandleMessage(MidiMessage message);

    /// <summary>Replaces all mappings (e.g. restoring from settings on startup).</summary>
    void SetMappings(IEnumerable<TransportMapping> mappings);

    event Action? MappingsChanged;
    event Action? LearnStateChanged;
}
