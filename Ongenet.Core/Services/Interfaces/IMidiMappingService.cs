using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// "MIDI learn": binds hardware CCs to automatable parameters and applies incoming CC values to them.
/// Mappings live on the current project (so they save/load with it) and are managed here.
/// </summary>
public interface IMidiMappingService
{
    /// <summary>Whether a learn is armed (the next CC will be bound to the armed target).</summary>
    bool IsLearning { get; }

    /// <summary>The target currently being learned, or null.</summary>
    IAutomationTarget? LearnTarget { get; }

    /// <summary>Arms learn: the next received CC binds to <paramref name="target"/> on <paramref name="owner"/>.</summary>
    void BeginLearn(Track owner, IAutomationTarget target);

    /// <summary>Cancels a pending learn without binding anything.</summary>
    void CancelLearn();

    /// <summary>
    /// Handles an incoming Control Change (called on the MIDI thread). Returns true if it was consumed —
    /// either it completed a learn or it matched a mapping and was applied — so the caller should not
    /// also forward it as a raw CC to the instrument.
    /// </summary>
    bool HandleControlChange(MidiMessage message);

    /// <summary>The mappings for the current project.</summary>
    IReadOnlyList<MidiMapping> Mappings { get; }

    /// <summary>Finds the mapping bound to <paramref name="owner"/> + <paramref name="binding"/>, or null.</summary>
    MidiMapping? FindMapping(Track owner, AutomationBinding binding);

    /// <summary>Removes a mapping.</summary>
    void Remove(MidiMapping mapping);

    /// <summary>Raised when the mapping set changes (add/remove/project load).</summary>
    event Action? MappingsChanged;

    /// <summary>Raised when learn is armed or disarmed.</summary>
    event Action? LearnStateChanged;
}
