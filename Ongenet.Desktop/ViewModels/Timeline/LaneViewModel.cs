namespace Ongenet.Desktop.ViewModels.Timeline
{
    /// <summary>
    /// Base for a row shown in the arrange view. Rows are polymorphic: a <see cref="TrackLaneViewModel"/>
    /// is a normal track lane, an <see cref="AutomationLaneViewModel"/> is an indented automation curve
    /// belonging to the track above it. Rows can differ in height, so the timeline lays them out with
    /// cumulative-height arithmetic rather than a fixed row stride.
    /// </summary>
    public abstract class LaneViewModel : ViewModelBase
    {
        /// <summary>Row height in pixels (lane content and header share it).</summary>
        public abstract double Height { get; }
    }
}
