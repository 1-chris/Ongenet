using System;
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
    /// <summary>The selected track, or null.</summary>
    Track? SelectedTrack { get; }

    /// <summary>The selected clip, or null.</summary>
    Clip? SelectedClip { get; }

    /// <summary>Raised whenever <see cref="SelectedTrack"/> or <see cref="SelectedClip"/> changes.</summary>
    event Action? SelectionChanged;

    /// <summary>Selects a track (and clears any clip selection).</summary>
    void SelectTrack(Track? track);

    /// <summary>Selects a clip and the track that owns it.</summary>
    void SelectClip(Clip? clip, Track? owner);
}
