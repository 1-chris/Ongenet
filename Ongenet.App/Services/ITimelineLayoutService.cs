using Ongenet.App.ViewModels.Timeline;

namespace Ongenet.App.Services;

/// <summary>Shared beat-to-pixel layout for arrangement and video timelines.</summary>
public interface ITimelineLayoutService
{
    TimelineMetrics Metrics { get; }
}
