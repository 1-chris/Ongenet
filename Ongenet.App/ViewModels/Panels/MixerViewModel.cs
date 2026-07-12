using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Localization;
using Ongenet.App.ViewModels;
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
    private readonly ISelectionService _selection;

    public MixerViewModel(IProjectService project, IEventAggregator events, IHistoryService history,
        IEffectRegistry effects, IPlaybackClock clock, IInputMonitorService inputMonitor,
        ISelectionService selection, ILocalizationService localization)
    {
        _project = project;
        _events = events;
        _history = history;
        _effects = effects;
        _inputMonitor = inputMonitor;
        _selection = selection;

        _project.ProjectChanged += Rebuild;
        _events.Subscribe<TracksChangedEvent>(_ => Rebuild());
        _events.Subscribe<TrackChangedEvent>(_ => RefreshStrips());
        _selection.SelectionChanged += RefreshSelection;
        localization.CultureChanged += RefreshEnumLabels;
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

    private void RefreshSelection()
    {
        foreach (var strip in Strips)
            strip.RefreshSelection();
    }

    private void RefreshEnumLabels()
    {
        foreach (var strip in Strips)
            strip.RefreshEnumLabels();
    }

    private void Rebuild()
    {
        Strips.Clear();
        foreach (var track in _project.Current.Tracks.Where(t => t.Kind is not (TrackKind.Midi or TrackKind.Pattern)))
            Strips.Add(new MixerStripViewModel(track, _project, _events, _history, _inputMonitor, _selection));
        RefreshSelection();
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

public sealed class EnumOption<T> where T : struct, Enum
{
    public EnumOption(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }
    public string Label { get; }
}

public sealed class MixerStripViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private readonly IInputMonitorService _inputMonitor;
    private readonly ISelectionService _selection;

    public MixerStripViewModel(Track track, IProjectService project, IEventAggregator events, IHistoryService history,
        IInputMonitorService inputMonitor, ISelectionService selection)
    {
        Track = track;
        _project = project;
        _events = events;
        _history = history;
        _inputMonitor = inputMonitor;
        _selection = selection;

        InputMonitoringOptions = BuildInputMonitoringOptions();
        OutputTargetOptions = BuildOutputTargetOptions();

        AddSendCommand = new RelayCommand(AddSend, () => ShowAddSend);
        RebuildSends();
        RefreshRoutingOptions();
    }

    public Track Track { get; }

    public string Name => Track.Name;

    public string ColorKey => Track.ColorKey;

    public bool IsSelected => ReferenceEquals(_selection.SelectedTrack, Track);

    public bool IsBus => Track.IsBus;

    public bool IsArmVisible => Track.Kind is not (TrackKind.Master or TrackKind.Return or TrackKind.Midi or TrackKind.Pattern);

    public bool IsMonitorVisible => Track.Kind == TrackKind.Audio;

    public bool ShowAddSend => Track.Kind is not (TrackKind.Master or TrackKind.Return)
                               && _project.Current.Tracks.Any(t => t.Kind == TrackKind.Return);

    public ObservableCollection<EnumOption<InputMonitoringMode>> InputMonitoringOptions { get; }

    public EnumOption<InputMonitoringMode>? SelectedInputMonitoring
    {
        get => InputMonitoringOptions.FirstOrDefault(o => o.Value == Track.InputMonitoring);
        set
        {
            if (value is null || Track.InputMonitoring == value.Value) return;
            Track.InputMonitoring = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedInputMonitoring));
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

    public ObservableCollection<EnumOption<TrackOutputTarget>> OutputTargetOptions { get; }

    public EnumOption<TrackOutputTarget>? SelectedOutputTarget
    {
        get => OutputTargetOptions.FirstOrDefault(o => o.Value == Track.OutputTarget);
        set
        {
            if (value is null || Track.OutputTarget == value.Value) return;
            _history.Capture("Change routing");
            Track.OutputTarget = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedOutputTarget));
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

    public bool ShowBusPicker => Track.OutputTarget == TrackOutputTarget.SpecificBus;

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

    public ObservableCollection<TrackSendEditorViewModel> Sends { get; } = new();

    public bool HasSends => Sends.Count > 0;

    public RelayCommand AddSendCommand { get; }

    public void SelectTrack() => _selection.SelectTrack(Track);

    public void RefreshMeters() => OnPropertyChanged(nameof(MeterLevel));

    public void RefreshSelection() => OnPropertyChanged(nameof(IsSelected));

    public void RefreshEnumLabels()
    {
        ReplaceOptions(InputMonitoringOptions, BuildInputMonitoringOptions());
        ReplaceOptions(OutputTargetOptions, BuildOutputTargetOptions());
        OnPropertyChanged(nameof(SelectedInputMonitoring));
        OnPropertyChanged(nameof(SelectedOutputTarget));
    }

    public void RefreshFromTrack()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ColorKey));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(Pan));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(IsSoloed));
        OnPropertyChanged(nameof(IsArmed));
        OnPropertyChanged(nameof(SelectedOutputTarget));
        OnPropertyChanged(nameof(ShowBusPicker));
        OnPropertyChanged(nameof(SelectedRoutingBus));
        OnPropertyChanged(nameof(RouteToMaster));
        OnPropertyChanged(nameof(ShowAddSend));
        OnPropertyChanged(nameof(SelectedInputMonitoring));
        RefreshRoutingOptions();
        RefreshSends();
        AddSendCommand.RaiseCanExecuteChanged();
    }

    private void RefreshSends()
    {
        for (var i = Sends.Count - 1; i >= 0; i--)
        {
            if (!Track.Sends.Contains(Sends[i].Send))
                Sends.RemoveAt(i);
        }

        foreach (var send in Track.Sends)
        {
            if (Sends.All(s => s.Send != send))
                Sends.Add(CreateSendEditor(send));
        }

        foreach (var send in Sends)
            send.RefreshFromModel();

        OnPropertyChanged(nameof(HasSends));
    }

    private void RebuildSends()
    {
        Sends.Clear();
        foreach (var send in Track.Sends)
            Sends.Add(CreateSendEditor(send));
        OnPropertyChanged(nameof(HasSends));
    }

    private TrackSendEditorViewModel CreateSendEditor(TrackSend send) =>
        new(Track, send, _project, _history, NotifyTrack, RemoveSendEditor);

    private void RemoveSendEditor(TrackSendEditorViewModel editor)
    {
        Track.Sends.Remove(editor.Send);
        RebuildSends();
        NotifyTrack();
    }

    private void AddSend()
    {
        var returnTracks = _project.Current.Tracks.Where(t => t.Kind == TrackKind.Return).ToList();
        if (returnTracks.Count == 0) return;

        var target = returnTracks.FirstOrDefault(t => Track.Sends.All(s => s.TargetTrackId != t.Id))
                     ?? returnTracks[0];
        _history.Capture("Add send");
        Track.Sends.Add(new TrackSend { TargetTrackId = target.Id });
        RebuildSends();
        NotifyTrack();
    }

    private void RefreshRoutingOptions()
    {
        RoutingBuses.Clear();
        foreach (var bus in _project.Current.Tracks.Where(t => t.IsBus && t.Id != Track.Id))
            RoutingBuses.Add(new RoutingBusOption(bus.Id, bus.Name));
    }

    private static ObservableCollection<EnumOption<InputMonitoringMode>> BuildInputMonitoringOptions() =>
        new([
            new EnumOption<InputMonitoringMode>(InputMonitoringMode.Off, L("Mixer_InputMonitoring_Off")),
            new EnumOption<InputMonitoringMode>(InputMonitoringMode.Auto, L("Mixer_InputMonitoring_Auto")),
            new EnumOption<InputMonitoringMode>(InputMonitoringMode.On, L("Mixer_InputMonitoring_On")),
        ]);

    private static ObservableCollection<EnumOption<TrackOutputTarget>> BuildOutputTargetOptions() =>
        new([
            new EnumOption<TrackOutputTarget>(TrackOutputTarget.ParentBus, L("Mixer_OutputTarget_ParentBus")),
            new EnumOption<TrackOutputTarget>(TrackOutputTarget.Master, L("Mixer_OutputTarget_MasterBus")),
            new EnumOption<TrackOutputTarget>(TrackOutputTarget.SpecificBus, L("Mixer_OutputTarget_SpecificBus")),
            new EnumOption<TrackOutputTarget>(TrackOutputTarget.None, L("Mixer_OutputTarget_None")),
        ]);

    private static void ReplaceOptions<T>(ObservableCollection<EnumOption<T>> target,
        ObservableCollection<EnumOption<T>> source) where T : struct, Enum
    {
        target.Clear();
        foreach (var option in source)
            target.Add(option);
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
