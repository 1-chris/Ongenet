using System;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Single path for "live preview" notes (mouse on the on-screen keyboards, or the computer
/// keyboard) routed to the currently selected track's instrument. Tracks which notes are
/// sounding so the on-screen keyboards can highlight them.
/// </summary>
public interface IPreviewService
{
    /// <summary>Starts a preview note on the selected instrument (no-op if already on).</summary>
    void NoteOn(int midiNote);

    /// <summary>Stops a preview note.</summary>
    void NoteOff(int midiNote);

    /// <summary>Whether a note is currently sounding (for keyboard highlighting).</summary>
    bool IsActive(int midiNote);

    /// <summary>Raised whenever the set of active notes changes.</summary>
    event Action? ActiveNotesChanged;

    /// <summary>Raised when a preview note starts (the live MIDI-input stream, for recording).</summary>
    event Action<int>? NotePressed;

    /// <summary>Raised when a preview note stops.</summary>
    event Action<int>? NoteReleased;
}
