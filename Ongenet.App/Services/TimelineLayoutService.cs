using Ongenet.App.ViewModels.Timeline;

namespace Ongenet.App.Services;

public sealed class TimelineLayoutService : ITimelineLayoutService
{
    public TimelineMetrics Metrics { get; } = new();
}
