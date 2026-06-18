using System;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Default <see cref="ISelectionService"/>. Holds the current track/clip selection and
/// raises a single change event that panels subscribe to.
/// </summary>
public class SelectionService : ISelectionService
{
    public Track? SelectedTrack { get; private set; }
    public Clip? SelectedClip { get; private set; }

    public event Action? SelectionChanged;

    public void SelectTrack(Track? track)
    {
        if (ReferenceEquals(SelectedTrack, track) && SelectedClip is null) return;
        SelectedTrack = track;
        SelectedClip = null;
        SelectionChanged?.Invoke();
    }

    public void SelectClip(Clip? clip, Track? owner)
    {
        if (ReferenceEquals(SelectedClip, clip) && ReferenceEquals(SelectedTrack, owner)) return;
        SelectedClip = clip;
        SelectedTrack = owner;
        SelectionChanged?.Invoke();
    }
}
