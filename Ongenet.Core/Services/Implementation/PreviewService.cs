using System;
using System.Collections.Generic;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Default <see cref="IPreviewService"/>. Routes preview notes to the selected track's instrument.
/// Live input can arrive on a background MIDI thread as well as the UI thread, so the active-note set
/// and the instrument calls are guarded by a lock; the UI-visible <see cref="ActiveNotesChanged"/> is
/// marshalled to the UI thread (when a dispatcher is available) while the note itself sounds
/// immediately on the calling thread for low latency.
/// </summary>
public class PreviewService : IPreviewService
{
    /// <summary>Velocity used by the on-screen keyboards / computer keyboard, which have no velocity sensing.</summary>
    private const float DefaultVelocity = 0.9f;

    private readonly ISelectionService _selection;
    private readonly IUiThreadDispatcher? _ui;
    private readonly HashSet<int> _active = new();
    private readonly object _lock = new();

    public PreviewService(ISelectionService selection, IUiThreadDispatcher? ui = null)
    {
        _selection = selection;
        _ui = ui;
    }

    public event Action? ActiveNotesChanged;
    public event Action<int, float>? NotePressed;
    public event Action<int>? NoteReleased;

    public void NoteOn(int midiNote) => NoteOn(midiNote, DefaultVelocity);

    public void NoteOn(int midiNote, float velocity)
    {
        lock (_lock)
        {
            if (!_active.Add(midiNote)) return;
            _selection.SelectedTrack?.Instrument?.NoteOn(midiNote, velocity);
        }

        // Outside the lock: recording capture (NotePressed) runs synchronously on the caller's thread;
        // the keyboard-highlight notification hops to the UI thread.
        NotePressed?.Invoke(midiNote, velocity);
        RaiseActiveNotesChanged();
    }

    public void NoteOff(int midiNote)
    {
        lock (_lock)
        {
            if (!_active.Remove(midiNote)) return;
            _selection.SelectedTrack?.Instrument?.NoteOff(midiNote);
        }

        NoteReleased?.Invoke(midiNote);
        RaiseActiveNotesChanged();
    }

    public bool IsActive(int midiNote)
    {
        lock (_lock) return _active.Contains(midiNote);
    }

    public void ControlChange(int controller, int value)
        => _selection.SelectedTrack?.Instrument?.ControlChange(controller, value);

    public void PitchBend(int value14)
        => _selection.SelectedTrack?.Instrument?.PitchBend(value14);

    public void ChannelAftertouch(int value)
        => _selection.SelectedTrack?.Instrument?.ChannelAftertouch(value);

    private void RaiseActiveNotesChanged()
    {
        var handler = ActiveNotesChanged;
        if (handler is null) return;
        if (_ui is null) handler();
        else _ui.Post(handler);
    }
}
