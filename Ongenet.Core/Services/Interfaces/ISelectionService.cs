using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// The single source of truth for what is currently selected, shared across panels.
/// The timeline writes selection here; the track and clip inspectors read from it. Using a
/// dedicated service (rather than the event aggregator or shell view model) keeps each panel
/// coupled only to the selection it cares about.
/// </summary>
public interface ISelectionService
{
    /// <summary>The primary (last-clicked) selected track, or null. Drives the inspector and effects panel.</summary>
    Track? SelectedTrack { get; }

    /// <summary>All currently selected tracks (for multi-track operations like grouping). Empty or one for a normal selection.</summary>
    IReadOnlyList<Track> SelectedTracks { get; }

    /// <summary>The selected clip, or null.</summary>
    Clip? SelectedClip { get; }

    /// <summary>Raised whenever <see cref="SelectedTrack"/> or <see cref="SelectedClip"/> changes.</summary>
    event Action? SelectionChanged;

    /// <summary>Selects a single track (and clears any clip and multi-selection).</summary>
    void SelectTrack(Track? track);

    /// <summary>Toggles a track's membership in the multi-selection (Ctrl+click); makes it the primary when added.</summary>
    void ToggleTrackSelection(Track track);

    /// <summary>Selects a clip and the track that owns it.</summary>
    void SelectClip(Clip? clip, Track? owner);
}
