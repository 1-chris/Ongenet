using Ongenet.Core.Models.Media;

namespace Ongenet.App.ViewModels.VideoTimeline;

public sealed class VideoTriggerMarkerViewModel : ViewModelBase
{
    public VideoTriggerMarkerViewModel(VideoTrigger trigger, VideoLayer target, double beat,
        string label, Timeline.TimelineMetrics metrics, bool isSelected)
    {
        Trigger = trigger;
        Target = target;
        Beat = beat;
        Label = label;
        Metrics = metrics;
        IsSelected = isSelected;
    }

    public VideoTrigger Trigger { get; }
    public VideoLayer Target { get; }
    public double Beat { get; }
    public string Label { get; }
    public Timeline.TimelineMetrics Metrics { get; }
    public bool IsSelected { get; }

    public double Left => Metrics.BeatsToPixels(Beat);
}
