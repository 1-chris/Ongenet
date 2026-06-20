using System;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Single path for "live" notes — mouse on the on-screen keyboards, the computer keyboard, or an
/// external MIDI controller — routed to the currently selected track's instrument. Tracks which notes
/// are sounding so the on-screen keyboards can highlight them. Thread-safe: live input may arrive on a
/// background MIDI thread while the UI thread also previews notes.
/// </summary>
public interface IPreviewService
{
    /// <summary>Starts a preview note at the default velocity (no-op if already on).</summary>
    void NoteOn(int midiNote);

    /// <summary>Starts a preview note at <paramref name="velocity"/> (0..1); no-op if already on.</summary>
    void NoteOn(int midiNote, float velocity);

    /// <summary>Stops a preview note.</summary>
    void NoteOff(int midiNote);

    /// <summary>Whether a note is currently sounding (for keyboard highlighting).</summary>
    bool IsActive(int midiNote);

    /// <summary>Sends a CC change to the selected instrument (e.g. mod wheel, sustain pedal).</summary>
    void ControlChange(int controller, int value);

    /// <summary>Sends a pitch-bend change (14-bit, centre 8192) to the selected instrument.</summary>
    void PitchBend(int value14);

    /// <summary>Sends channel aftertouch (pressure, 0..127) to the selected instrument.</summary>
    void ChannelAftertouch(int value);

    /// <summary>Raised whenever the set of active notes changes (marshalled to the UI thread).</summary>
    event Action? ActiveNotesChanged;

    /// <summary>Raised when a preview note starts, carrying its 0..1 velocity (for recording).</summary>
    event Action<int, float>? NotePressed;

    /// <summary>Raised when a preview note stops.</summary>
    event Action<int>? NoteReleased;
}
