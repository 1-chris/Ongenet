using Ongenet.Core.Models.Audio;

namespace Ongenet.App.ViewModels.Timeline;

public sealed class ArrangementMarkerViewModel : ViewModelBase
{
    private readonly TimelineMetrics _metrics;

    public ArrangementMarkerViewModel(ArrangementMarker model, TimelineMetrics metrics)
    {
        Model = model;
        _metrics = metrics;
        _metrics.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineMetrics.PixelsPerBeat))
                OnPropertyChanged(nameof(Left));
        };
    }

    public ArrangementMarker Model { get; }

    public string Name => Model.Name;

    public double Left => _metrics.BeatsToPixels(Model.Beat);
}
