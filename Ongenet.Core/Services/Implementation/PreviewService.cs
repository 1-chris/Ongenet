using System;
using System.Collections.Generic;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>Default <see cref="IPreviewService"/>. Routes preview notes to the selected track's instrument.</summary>
public class PreviewService : IPreviewService
{
    private readonly ISelectionService _selection;
    private readonly HashSet<int> _active = new();

    public PreviewService(ISelectionService selection) => _selection = selection;

    public event Action? ActiveNotesChanged;
    public event Action<int>? NotePressed;
    public event Action<int>? NoteReleased;

    public void NoteOn(int midiNote)
    {
        if (!_active.Add(midiNote)) return;
        _selection.SelectedTrack?.Instrument?.NoteOn(midiNote, 0.9f);
        ActiveNotesChanged?.Invoke();
        NotePressed?.Invoke(midiNote);
    }

    public void NoteOff(int midiNote)
    {
        if (!_active.Remove(midiNote)) return;
        _selection.SelectedTrack?.Instrument?.NoteOff(midiNote);
        ActiveNotesChanged?.Invoke();
        NoteReleased?.Invoke(midiNote);
    }

    public bool IsActive(int midiNote) => _active.Contains(midiNote);

    public void ControlChange(int controller, int value)
        => _selection.SelectedTrack?.Instrument?.ControlChange(controller, value);

    public void PitchBend(int value14)
        => _selection.SelectedTrack?.Instrument?.PitchBend(value14);
}
