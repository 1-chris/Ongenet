using System;
using Ongenet.Core.Models.Media;

namespace Ongenet.App.ViewModels.VideoTimeline;

public sealed class VideoVisibilityBlockViewModel : ViewModelBase
{
    public VideoVisibilityBlockViewModel(VideoVisibilityRegion region, Timeline.TimelineMetrics metrics, bool isSelected)
    {
        Region = region;
        Metrics = metrics;
        IsSelected = isSelected;
    }

    public VideoVisibilityRegion Region { get; }
    public Timeline.TimelineMetrics Metrics { get; }
    public bool IsSelected { get; }

    public double Left => Metrics.BeatsToPixels(Region.StartBeat);
    public double Width => Math.Max(4, Metrics.BeatsToPixels(Region.EndBeat - Region.StartBeat));

    public void RefreshFromRegion()
    {
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Width));
    }
}
