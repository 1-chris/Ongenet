using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Dedicated mixer panel — channel strips with faders, sends, and meters.</summary>
public sealed class MixerViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private readonly IEffectRegistry _effects;
    private readonly IInputMonitorService _inputMonitor;

    public MixerViewModel(IProjectService project, IEventAggregator events, IHistoryService history,
        IEffectRegistry effects, IPlaybackClock clock, IInputMonitorService inputMonitor)
    {
        _project = project;
        _events = events;
        _history = history;
        _effects = effects;
        _inputMonitor = inputMonitor;

        _project.ProjectChanged += Rebuild;
        _events.Subscribe<TracksChangedEvent>(_ => Rebuild());
        _events.Subscribe<TrackChangedEvent>(_ => RefreshStrips());
        clock.Tick += OnPlaybackTick;

        AddReverbReturnCommand = new RelayCommand(() => AddReturnTrack(ReturnTrackTemplate.Reverb));
        AddDelayReturnCommand = new RelayCommand(() => AddReturnTrack(ReturnTrackTemplate.Delay));
        OpenRoutingMatrixCommand = new RelayCommand(OpenRoutingMatrix);
        Rebuild();
    }

    public ObservableCollection<MixerStripViewModel> Strips { get; } = new();

    public RelayCommand AddReverbReturnCommand { get; }
    public RelayCommand AddDelayReturnCommand { get; }
    public RelayCommand OpenRoutingMatrixCommand { get; }

    private void OpenRoutingMatrix()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            Views.Windows.RoutingMatrixWindow.ShowMatrix(desktop.MainWindow);
        else
            Views.Windows.RoutingMatrixWindow.ShowMatrix(null);
    }

    private void OnPlaybackTick()
    {
        foreach (var strip in Strips)
            strip.RefreshMeters();
    }

    private void RefreshStrips()
    {
        foreach (var strip in Strips)
            strip.RefreshFromTrack();
    }

    private void Rebuild()
    {
        Strips.Clear();
        foreach (var track in _project.Current.Tracks.Where(t => t.Kind is not (TrackKind.Midi or TrackKind.Pattern)))
            Strips.Add(new MixerStripViewModel(track, _project, _events, _history, _inputMonitor));
    }

    private void AddReturnTrack(ReturnTrackTemplate template)
    {
        _history.Capture("Add return track");
        var isReverb = template == ReturnTrackTemplate.Reverb;
        var track = new Track
        {
            Name = isReverb ? "Reverb" : "Delay",
            Kind = TrackKind.Return,
            ColorKey = isReverb ? "CatppuccinSky" : "CatppuccinTeal",
            Volume = 0.8
        };
        track.Effects.Add(_effects.Create(isReverb ? ReverbEffect.TypeId : DelayEffect.TypeId));
        track.CommitEffects();

        var masterIdx = _project.Current.Tracks.FindIndex(t => t.Kind == TrackKind.Master);
        if (masterIdx >= 0) _project.Current.Tracks.Insert(masterIdx, track);
        else _project.Current.Tracks.Add(track);

        _events.Publish(new TracksChangedEvent());
        Rebuild();
    }
}

public enum ReturnTrackTemplate
{
    Reverb,
    Delay
}

public sealed class MixerStripViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private readonly IInputMonitorService _inputMonitor;

    public MixerStripViewModel(Track track, IProjectService project, IEventAggregator events, IHistoryService history,
        IInputMonitorService inputMonitor)
    {
        Track = track;
        _project = project;
        _events = events;
        _history = history;
        _inputMonitor = inputMonitor;
        foreach (var send in track.Sends)
            Sends.Add(new MixerSendViewModel(send, project, NotifyTrack));
        RefreshRoutingOptions();
    }

    public Track Track { get; }

    public string Name => Track.Name;

    public bool IsBus => Track.IsBus;

    public bool IsArmVisible => Track.Kind is not (TrackKind.Master or TrackKind.Return or TrackKind.Midi or TrackKind.Pattern);

    public bool IsMonitorVisible => Track.Kind == TrackKind.Audio;

    public Array InputMonitoringModes => Enum.GetValues<InputMonitoringMode>();

    public InputMonitoringMode InputMonitoring
    {
        get => Track.InputMonitoring;
        set
        {
            if (Track.InputMonitoring == value) return;
            Track.InputMonitoring = value;
            OnPropertyChanged();
            _inputMonitor.Refresh();
        }
    }

    public double Volume
    {
        get => Track.Volume;
        set
        {
            if (Track.Volume == value) return;
            Track.Volume = value;
            OnPropertyChanged();
            NotifyTrack();
        }
    }

    public double Pan
    {
        get => Track.Pan;
        set
        {
            if (Track.Pan == value) return;
            Track.Pan = value;
            OnPropertyChanged();
            NotifyTrack();
        }
    }

    public bool IsMuted
    {
        get => Track.IsMuted;
        set
        {
            if (Track.IsMuted == value) return;
            _history.Capture(value ? "Mute track" : "Unmute track");
            Track.IsMuted = value;
            OnPropertyChanged();
            NotifyTrack();
        }
    }

    public bool IsSoloed
    {
        get => Track.IsSoloed;
        set
        {
            if (Track.IsSoloed == value) return;
            _history.Capture(value ? "Solo track" : "Unsolo track");
            Track.IsSoloed = value;
            OnPropertyChanged();
            NotifyTrack();
        }
    }

    public bool IsArmed
    {
        get => Track.IsArmed;
        set
        {
            if (Track.IsArmed == value) return;
            Track.IsArmed = value;
            OnPropertyChanged();
            _inputMonitor.Refresh();
        }
    }

    public float MeterLevel => Track.MeterLevel;

    public TrackOutputTarget OutputTarget
    {
        get => Track.OutputTarget;
        set
        {
            if (Track.OutputTarget == value) return;
            _history.Capture("Change routing");
            Track.OutputTarget = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowBusPicker));
            NotifyTrack();
        }
    }

    public bool RouteToMaster
    {
        get => Track.RouteToMaster;
        set
        {
            if (Track.RouteToMaster == value) return;
            _history.Capture("Change routing");
            Track.RouteToMaster = value;
            OnPropertyChanged();
            NotifyTrack();
        }
    }

    public Guid? OutputBusId
    {
        get => Track.OutputBusId;
        set
        {
            if (Track.OutputBusId == value) return;
            _history.Capture("Change routing");
            Track.OutputBusId = value;
            OnPropertyChanged();
            NotifyTrack();
        }
    }

    public bool ShowBusPicker => OutputTarget == TrackOutputTarget.SpecificBus;

    public TrackOutputTarget[] OutputTargets { get; } =
        Enum.GetValues<TrackOutputTarget>();

    public ObservableCollection<RoutingBusOption> RoutingBuses { get; } = new();

    public RoutingBusOption? SelectedRoutingBus
    {
        get => RoutingBuses.FirstOrDefault(b => b.Id == Track.OutputBusId);
        set
        {
            var id = value?.Id;
            if (Track.OutputBusId == id) return;
            _history.Capture("Change routing");
            Track.OutputBusId = id;
            OnPropertyChanged();
            NotifyTrack();
        }
    }

    public ObservableCollection<MixerSendViewModel> Sends { get; } = new();

    public bool HasSends => Sends.Count > 0;

    public void RefreshMeters() => OnPropertyChanged(nameof(MeterLevel));

    public void RefreshFromTrack()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(Pan));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(IsSoloed));
        OnPropertyChanged(nameof(IsArmed));
        OnPropertyChanged(nameof(OutputTarget));
        OnPropertyChanged(nameof(OutputBusId));
        OnPropertyChanged(nameof(RouteToMaster));
        OnPropertyChanged(nameof(ShowBusPicker));
        OnPropertyChanged(nameof(SelectedRoutingBus));
        RefreshRoutingOptions();
        foreach (var send in Sends)
            send.RefreshFromModel();
    }

    private void RefreshRoutingOptions()
    {
        RoutingBuses.Clear();
        foreach (var bus in _project.Current.Tracks.Where(t => t.IsBus && t.Id != Track.Id))
            RoutingBuses.Add(new RoutingBusOption(bus.Id, bus.Name));
    }

    private void NotifyTrack() => _events.Publish(new TrackChangedEvent(Track));
}

public sealed class RoutingBusOption
{
    public RoutingBusOption(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; }
    public string Name { get; }
}

public sealed class MixerSendViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly Action _notify;

    public MixerSendViewModel(TrackSend send, IProjectService project, Action notify)
    {
        Send = send;
        _project = project;
        _notify = notify;
    }

    public TrackSend Send { get; }

    public string TargetName
    {
        get
        {
            var target = _project.Current.Tracks.FirstOrDefault(t => t.Id == Send.TargetTrackId);
            return target?.Name ?? "(missing)";
        }
    }

    public double Level
    {
        get => Send.Level;
        set
        {
            if (Send.Level == value) return;
            Send.Level = value;
            OnPropertyChanged();
            _notify();
        }
    }

    public bool PreFader
    {
        get => Send.PreFader;
        set
        {
            if (Send.PreFader == value) return;
            Send.PreFader = value;
            OnPropertyChanged();
            _notify();
        }
    }

    public bool Enabled
    {
        get => Send.Enabled;
        set
        {
            if (Send.Enabled == value) return;
            Send.Enabled = value;
            OnPropertyChanged();
            _notify();
        }
    }

    public void RefreshFromModel()
    {
        OnPropertyChanged(nameof(TargetName));
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(PreFader));
        OnPropertyChanged(nameof(Enabled));
    }
}
