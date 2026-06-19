using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Default <see cref="ISelectionService"/>. Holds the current track/clip selection and
/// raises a single change event that panels subscribe to.
/// </summary>
public class SelectionService : ISelectionService
{
    private readonly List<Track> _selectedTracks = new();

    public Track? SelectedTrack { get; private set; }
    public Clip? SelectedClip { get; private set; }
    public IReadOnlyList<Track> SelectedTracks => _selectedTracks;

    public event Action? SelectionChanged;

    public void SelectTrack(Track? track)
    {
        if (ReferenceEquals(SelectedTrack, track) && SelectedClip is null
            && _selectedTracks.Count == (track is null ? 0 : 1)) return;

        SelectedClip = null;
        SelectedTrack = track;
        _selectedTracks.Clear();
        if (track is not null) _selectedTracks.Add(track);
        SelectionChanged?.Invoke();
    }

    public void ToggleTrackSelection(Track track)
    {
        SelectedClip = null;
        if (_selectedTracks.Remove(track))
        {
            if (ReferenceEquals(SelectedTrack, track))
                SelectedTrack = _selectedTracks.Count > 0 ? _selectedTracks[^1] : null;
        }
        else
        {
            _selectedTracks.Add(track);
            SelectedTrack = track;
        }

        SelectionChanged?.Invoke();
    }

    public void SelectClip(Clip? clip, Track? owner)
    {
        if (ReferenceEquals(SelectedClip, clip) && ReferenceEquals(SelectedTrack, owner)
            && _selectedTracks.Count == (owner is null ? 0 : 1)) return;

        SelectedClip = clip;
        SelectedTrack = owner;
        _selectedTracks.Clear();
        if (owner is not null) _selectedTracks.Add(owner);
        SelectionChanged?.Invoke();
    }
}
