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
    }
}
