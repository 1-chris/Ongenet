using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Editable routing overview: track main outputs, sends, and plugin multi-out routes.</summary>
public sealed class RoutingMatrixViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly IEventAggregator _events;

    public RoutingMatrixViewModel(IProjectService project, IEventAggregator events)
    {
        _project = project;
        _events = events;
        TrackRoutes = new ObservableCollection<TrackRouteRow>();
        MultiOutRoutes = new ObservableCollection<MultiOutRouteRow>();

        _project.ProjectChanged += Rebuild;
        _events.Subscribe<TracksChangedEvent>(_ => Rebuild());
        _events.Subscribe<TrackChangedEvent>(_ => Rebuild());
        Rebuild();
    }

    public ObservableCollection<TrackRouteRow> TrackRoutes { get; }
    public ObservableCollection<MultiOutRouteRow> MultiOutRoutes { get; }

    private void Rebuild()
    {
        TrackRoutes.Clear();
        foreach (var track in _project.Current.Tracks.Where(t => t.Kind != TrackKind.Master))
            TrackRoutes.Add(new TrackRouteRow(track, _project.Current.Tracks,
                () => _events.Publish(new TrackChangedEvent(track))));

        MultiOutRoutes.Clear();
        foreach (var route in _project.Current.MultiOutputRoutes)
        {
            var source = _project.Current.Tracks.FirstOrDefault(t => t.Id == route.SourceTrackId);
            var dest = _project.Current.Tracks.FirstOrDefault(t => t.Id == route.DestinationTrackId);
            MultiOutRoutes.Add(new MultiOutRouteRow(route, source?.Name ?? "(missing)",
                _project.Current.Tracks.Where(t => t.IsBus).ToList()));
        }
    }
}

public sealed class TrackRouteRow : ViewModelBase
{
    private readonly Action _changed;

    public TrackRouteRow(Track track, System.Collections.Generic.List<Track> allTracks, Action changed)
    {
        Track = track;
        _changed = changed;
        OutputBuses = allTracks.Where(t => t.IsBus && t.Kind != TrackKind.Master && t.Id != track.Id).ToList();
        Sends = new ObservableCollection<TrackSendRouteRow>(
            track.Sends.Select(send => new TrackSendRouteRow(send, allTracks, changed)));
    }

    public Track Track { get; }
    public string TrackName => Track.Name;
    public Array OutputTargets { get; } = Enum.GetValues<TrackOutputTarget>();
    public System.Collections.Generic.IReadOnlyList<Track> OutputBuses { get; }
    public ObservableCollection<TrackSendRouteRow> Sends { get; }
    public bool HasSends => Sends.Count > 0;

    public TrackOutputTarget OutputTarget
    {
        get => Track.OutputTarget;
        set
        {
            if (Track.OutputTarget == value) return;
            Track.OutputTarget = value;
            _changed();
            OnPropertyChanged();
            OnPropertyChanged(nameof(UsesSpecificBus));
        }
    }

    public bool UsesSpecificBus => OutputTarget == TrackOutputTarget.SpecificBus;

    public Track? OutputBus
    {
        get => OutputBuses.FirstOrDefault(t => t.Id == Track.OutputBusId);
        set
        {
            if (Track.OutputBusId == value?.Id) return;
            Track.OutputBusId = value?.Id;
            if (value is not null) Track.OutputTarget = TrackOutputTarget.SpecificBus;
            _changed();
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputTarget));
        }
    }
}

public sealed class TrackSendRouteRow : ViewModelBase
{
    private readonly TrackSend _send;
    private readonly Action _changed;

    public TrackSendRouteRow(TrackSend send, System.Collections.Generic.List<Track> allTracks, Action changed)
    {
        _send = send;
        _changed = changed;
        TargetName = allTracks.FirstOrDefault(t => t.Id == send.TargetTrackId)?.Name ?? "(missing)";
    }

    public string TargetName { get; }

    public double Level
    {
        get => _send.Level;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (_send.Level == clamped) return;
            _send.Level = clamped;
            _changed();
            OnPropertyChanged();
        }
    }

    public bool PreFader
    {
        get => _send.PreFader;
        set
        {
            if (_send.PreFader == value) return;
            _send.PreFader = value;
            _changed();
            OnPropertyChanged();
        }
    }

    public bool Enabled
    {
        get => _send.Enabled;
        set
        {
            if (_send.Enabled == value) return;
            _send.Enabled = value;
            _changed();
            OnPropertyChanged();
        }
    }
}

public sealed class MultiOutRouteRow : ViewModelBase
{
    private readonly MultiOutputRoute _route;

    public MultiOutRouteRow(MultiOutputRoute route, string sourceTrackName,
        System.Collections.Generic.IReadOnlyList<Track> destinations)
    {
        _route = route;
        SourceTrackName = sourceTrackName;
        Destinations = destinations;
    }

    public string SourceTrackName { get; }
    public int PluginOutputBus => _route.PluginOutputBus;
    public System.Collections.Generic.IReadOnlyList<Track> Destinations { get; }

    public Track? Destination
    {
        get => Destinations.FirstOrDefault(t => t.Id == _route.DestinationTrackId);
        set
        {
            if (value is null || _route.DestinationTrackId == value.Id) return;
            _route.DestinationTrackId = value.Id;
            OnPropertyChanged();
        }
    }

    public double Level
    {
        get => _route.Level;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (_route.Level == clamped) return;
            _route.Level = clamped;
            OnPropertyChanged();
        }
    }
}
