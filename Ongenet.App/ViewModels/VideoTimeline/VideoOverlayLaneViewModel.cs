using System.Collections.ObjectModel;
using Ongenet.Core.Models.Media;

namespace Ongenet.App.ViewModels.VideoTimeline;

public sealed class VideoOverlayLaneViewModel : ViewModelBase
{
    public VideoOverlayLaneViewModel(VideoLayer layer, Timeline.TimelineMetrics metrics,
        ObservableCollection<VideoTriggerMarkerViewModel> markers,
        ObservableCollection<VideoVisibilityBlockViewModel> visibilityBlocks, bool isSelected)
    {
        Layer = layer;
        Metrics = metrics;
        Markers = markers;
        VisibilityBlocks = visibilityBlocks;
        IsSelected = isSelected;
    }

    public VideoLayer Layer { get; }
    public Timeline.TimelineMetrics Metrics { get; }
    public ObservableCollection<VideoTriggerMarkerViewModel> Markers { get; }
    public ObservableCollection<VideoVisibilityBlockViewModel> VisibilityBlocks { get; }
    public bool IsSelected { get; }
    public bool ShowVisibilityHint => VisibilityBlocks.Count == 0;

    public string Header => Layer.Name;
    public double LaneHeight => 36;
}
