using Ongenet.Core.Models.Audio;

namespace Ongenet.Desktop.ViewModels.Timeline
{
    /// <summary>
    /// Track-level operations a lane's context menu / header invokes, implemented by the timeline.
    /// </summary>
    public interface ITrackActions
    {
        void DuplicateTrack(TrackLaneViewModel lane);
        void DeleteTrack(TrackLaneViewModel lane);
        void AddInstrumentTrack();
        void AddAudioTrack();

        /// <summary>Notifies that a track's properties changed (so other views resync).</summary>
        void NotifyTrackChanged(Track track);

        /// <summary>Collapses/expands a track's automation rows (rebuilds the rendered rows).</summary>
        void ToggleAutomation(TrackLaneViewModel lane);

        /// <summary>Removes one automation lane from its track (rebuilds the rendered rows).</summary>
        void RemoveAutomationLane(AutomationLaneViewModel lane);

        /// <summary>Collapses/expands a group's nested rows (children + their automation).</summary>
        void ToggleGroup(TrackLaneViewModel lane);

        /// <summary>Groups the currently multi-selected tracks under a new group bus.</summary>
        void GroupSelectedTracks();

        /// <summary>Removes a group but keeps its tracks, moving them up to the group's parent.</summary>
        void DeleteGroupKeepChildren(TrackLaneViewModel lane);

        /// <summary>Removes a group and every track nested inside it.</summary>
        void DeleteGroupAndChildren(TrackLaneViewModel lane);

        /// <summary>Moves a track one level out of its group (popping it below the group).</summary>
        void DetachFromGroup(TrackLaneViewModel lane);
    }
}
