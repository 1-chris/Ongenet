using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Ongenet.App.Services;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Session view — clip launcher grid (tracks × scenes).</summary>
public sealed class SessionViewModel : ViewModelBase
{
    private static readonly string[] SceneColors =
    [
        "CatppuccinGreen", "CatppuccinBlue", "CatppuccinPeach", "CatppuccinPink",
        "CatppuccinTeal", "CatppuccinYellow", "CatppuccinLavender", "CatppuccinRed"
    ];

    private static readonly double[] QuantizeOptions = [0, 0.25, 0.5, 1, 2, 4];

    private readonly IProjectService _project;
    private readonly IPlaybackModeService _playback;
    private readonly ISessionCaptureService _capture;
    private readonly ISelectionService _selection;
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private SessionSlotViewModel? _selectedSlot;

    public SessionViewModel(IProjectService project, IPlaybackModeService playback, ISessionCaptureService capture,
        ISelectionService selection, IEventAggregator events, IHistoryService history)
    {
        _project = project;
        _playback = playback;
        _capture = capture;
        _selection = selection;
        _events = events;
        _history = history;
        StopAllCommand = new RelayCommand(_playback.StopAll);
        CaptureCommand = new RelayCommand(() => _capture.Capture(), () => _capture.PendingLaunchCount > 0);
        ClearSelectedSlotCommand = new RelayCommand(ClearSelectedSlot, () => HasSelection);
        _project.ProjectChanged += Rebuild;
        _playback.ActiveClipsChanged += RefreshSlotStates;
        _capture.PendingChanged += OnCapturePendingChanged;
        _selection.SelectionChanged += OnSelectionChanged;
        _events.Subscribe<SessionClipsChangedEvent>(_ => Rebuild());
        Rebuild();
    }

    public ObservableCollection<SessionSceneColumnViewModel> SceneColumns { get; } = new();
    public ObservableCollection<SessionTrackRowViewModel> TrackRows { get; } = new();

    public ICommand StopAllCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand ClearSelectedSlotCommand { get; }

    public int SceneCount => SceneColumns.Count;

    public SessionSlotViewModel? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (_selectedSlot == value) return;
            _selectedSlot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(InspectorName));
            OnPropertyChanged(nameof(InspectorSourceName));
            OnPropertyChanged(nameof(InspectorLengthBeats));
            OnPropertyChanged(nameof(LaunchMode));
            OnPropertyChanged(nameof(FollowAction));
            OnPropertyChanged(nameof(LaunchQuantizeBeats));
            OnPropertyChanged(nameof(LaunchQuantizeIndex));
            (ClearSelectedSlotCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => SelectedSlot?.Clip is not null;
    public SessionClip? SelectedClip => SelectedSlot?.Clip;

    public string InspectorName
    {
        get => SelectedClip?.Name ?? "";
        set
        {
            if (SelectedClip is not { } clip || clip.Name == value) return;
            clip.Name = value;
            OnPropertyChanged();
            SelectedSlot?.RefreshDisplay();
            PublishSessionClipChanged();
        }
    }

    public string InspectorSourceName
    {
        get
        {
            if (SelectedClip?.SourceClipId is not { } sourceId) return "";
            var track = _project.Current.Tracks.FirstOrDefault(t => t.Id == SelectedClip.TrackId);
            var source = track?.Clips.FirstOrDefault(c => c.Id == sourceId);
            return source?.Name ?? "(missing)";
        }
    }

    public double InspectorLengthBeats
    {
        get => SelectedClip?.LengthBeats ?? 0;
        set
        {
            if (SelectedClip is not { } clip || Math.Abs(clip.LengthBeats - value) < 1e-9) return;
            clip.LengthBeats = Math.Max(0.25, value);
            OnPropertyChanged();
            PublishSessionClipChanged();
        }
    }

    public Array LaunchModes => Enum.GetValues<SessionLaunchMode>();
    public Array FollowActions => Enum.GetValues<FollowAction>();
    public double[] QuantizeBeatOptions => QuantizeOptions;

    public SessionLaunchMode LaunchMode
    {
        get => SelectedClip?.LaunchMode ?? SessionLaunchMode.Trigger;
        set
        {
            if (SelectedClip is not { } clip || clip.LaunchMode == value) return;
            clip.LaunchMode = value;
            OnPropertyChanged();
            PublishSessionClipChanged();
        }
    }

    public FollowAction FollowAction
    {
        get => SelectedClip?.FollowAction ?? FollowAction.Stop;
        set
        {
            if (SelectedClip is not { } clip || clip.FollowAction == value) return;
            clip.FollowAction = value;
            OnPropertyChanged();
            PublishSessionClipChanged();
        }
    }

    public double LaunchQuantizeBeats
    {
        get => SelectedClip?.LaunchQuantizeBeats ?? 0;
        set
        {
            if (SelectedClip is not { } clip || Math.Abs(clip.LaunchQuantizeBeats - value) < 1e-9) return;
            clip.LaunchQuantizeBeats = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LaunchQuantizeIndex));
            PublishSessionClipChanged();
        }
    }

    public int LaunchQuantizeIndex
    {
        get
        {
            var beats = LaunchQuantizeBeats;
            for (var i = 0; i < QuantizeOptions.Length; i++)
                if (Math.Abs(QuantizeOptions[i] - beats) < 1e-9) return i;
            return 0;
        }
        set
        {
            if (value < 0 || value >= QuantizeOptions.Length) return;
            LaunchQuantizeBeats = QuantizeOptions[value];
        }
    }

    public double SessionCrossfader
    {
        get => _playback.SessionCrossfader;
        set
        {
            if (Math.Abs(_playback.SessionCrossfader - value) < 1e-9) return;
            _playback.SessionCrossfader = value;
            OnPropertyChanged();
        }
    }

    public int PendingCaptureCount => _capture.PendingLaunchCount;

    public double ProjectLaunchQuantizeBeats
    {
        get => _playback.LaunchQuantizeBeats;
        set => _playback.LaunchQuantizeBeats = value;
    }

    public int ProjectLaunchQuantizeIndex
    {
        get
        {
            var beats = ProjectLaunchQuantizeBeats;
            for (var i = 0; i < QuantizeOptions.Length; i++)
                if (Math.Abs(QuantizeOptions[i] - beats) < 1e-9) return i;
            return 0;
        }
        set
        {
            if (value < 0 || value >= QuantizeOptions.Length) return;
            ProjectLaunchQuantizeBeats = QuantizeOptions[value];
        }
    }

    public void SelectSlot(SessionSlotViewModel slot) => SelectedSlot = slot;

    /// <summary>Creates a session clip from an arrangement clip in the first empty slot on that track.</summary>
    public bool TryCreateSessionClipFromArrangement(Clip source, Track track)
    {
        var sceneIndex = FindFirstEmptyScene(track.Id);
        if (sceneIndex < 0) sceneIndex = NextSceneIndex();
        return TryAssignToSlot(sceneIndex, track.Id, source);
    }

    /// <summary>Assigns an arrangement clip as the source for a session slot.</summary>
    public bool TryAssignToSlot(int sceneIndex, Guid trackId, Clip source)
    {
        if (_project.Current.Tracks.FirstOrDefault(t => t.Id == trackId) is null)
            return false;

        _history.Capture("Assign session slot");

        var existing = _project.Current.SessionClips
            .FirstOrDefault(c => c.TrackId == trackId && c.SceneIndex == sceneIndex);
        if (existing is not null)
            _project.Current.SessionClips.Remove(existing);

        var track = _project.Current.Tracks.First(t => t.Id == trackId);
        _project.Current.SessionClips.Add(new SessionClip
        {
            TrackId = trackId,
            SceneIndex = sceneIndex,
            Name = source.Name,
            LengthBeats = source.LengthBeats,
            SourceClipId = source.Id
        });

        _events.Publish(new SessionClipsChangedEvent());
        return true;
    }

    /// <summary>Removes the session clip in the given slot.</summary>
    public bool TryClearSlot(int sceneIndex, Guid trackId)
    {
        var clip = _project.Current.SessionClips
            .FirstOrDefault(c => c.TrackId == trackId && c.SceneIndex == sceneIndex);
        if (clip is null) return false;

        _history.Capture("Clear session slot");
        _playback.StopClip(clip.Id);
        _project.Current.SessionClips.Remove(clip);
        _events.Publish(new SessionClipsChangedEvent());
        return true;
    }

    private void ClearSelectedSlot()
    {
        if (SelectedSlot is not { } slot || slot.Clip is null) return;
        TryClearSlot(slot.SceneIndex, slot.TrackId);
    }

    private void PublishSessionClipChanged() => _events.Publish(new SessionClipChangedEvent());

    private int FindFirstEmptyScene(Guid trackId)
    {
        var occupied = _project.Current.SessionClips
            .Where(c => c.TrackId == trackId)
            .Select(c => c.SceneIndex)
            .ToHashSet();
        for (var s = 0; s < SceneCount; s++)
            if (!occupied.Contains(s)) return s;
        return -1;
    }

    private int NextSceneIndex()
    {
        if (_project.Current.SessionClips.Count == 0) return 0;
        return _project.Current.SessionClips.Max(c => c.SceneIndex) + 1;
    }

    private void Rebuild()
    {
        SceneColumns.Clear();
        TrackRows.Clear();
        SelectedSlot = null;

        var clips = _project.Current.SessionClips;
        var sceneCount = clips.Count == 0 ? 8 : clips.Max(c => c.SceneIndex) + 1;
        sceneCount = Math.Max(sceneCount, 8);

        for (var s = 0; s < sceneCount; s++)
        {
            var sceneIndex = s;
            SceneColumns.Add(new SessionSceneColumnViewModel(
                sceneIndex,
                SceneColors[s % SceneColors.Length],
                () => _playback.LaunchScene(sceneIndex),
                () => StopScene(sceneIndex)));
        }

        foreach (var track in _project.Current.Tracks.Where(t => t.Kind is TrackKind.Audio or TrackKind.Instrument))
        {
            var row = new SessionTrackRowViewModel(track.Name, track.ColorKey);
            for (var s = 0; s < sceneCount; s++)
            {
                var clip = clips.FirstOrDefault(c => c.TrackId == track.Id && c.SceneIndex == s);
                row.Slots.Add(new SessionSlotViewModel(clip, track.Id, s, track.ColorKey, _playback, this, _selection));
            }
            TrackRows.Add(row);
        }

        OnPropertyChanged(nameof(SceneCount));
        RefreshAssignableSlots();
    }

    private void StopScene(int sceneIndex)
    {
        foreach (var clip in _project.Current.SessionClips.Where(c => c.SceneIndex == sceneIndex))
            _playback.StopClip(clip.Id);
    }

    private void RefreshSlotStates()
    {
        foreach (var row in TrackRows)
        foreach (var slot in row.Slots)
            slot.RefreshState();
    }

    private void OnCapturePendingChanged()
    {
        OnPropertyChanged(nameof(PendingCaptureCount));
        (CaptureCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnSelectionChanged() => RefreshAssignableSlots();

    private void RefreshAssignableSlots()
    {
        foreach (var row in TrackRows)
        foreach (var slot in row.Slots)
            slot.RefreshAssignable();
    }
}

public sealed class SessionSceneColumnViewModel : ViewModelBase
{
    public SessionSceneColumnViewModel(int sceneIndex, string colorKey, Action launch, Action stop)
    {
        SceneIndex = sceneIndex;
        Label = $"Scene {sceneIndex + 1}";
        ColorKey = colorKey;
        LaunchCommand = new RelayCommand(launch);
        StopCommand = new RelayCommand(stop);
    }

    public int SceneIndex { get; }
    public string Label { get; }
    public string ColorKey { get; }
    public ICommand LaunchCommand { get; }
    public ICommand StopCommand { get; }
}

public sealed class SessionTrackRowViewModel : ViewModelBase
{
    public SessionTrackRowViewModel(string trackName, string colorKey)
    {
        TrackName = trackName;
        ColorKey = colorKey;
    }

    public string TrackName { get; }
    public string ColorKey { get; }
    public ObservableCollection<SessionSlotViewModel> Slots { get; } = new();
}

public sealed class SessionSlotViewModel : ViewModelBase
{
    private readonly SessionClip? _clip;
    private readonly Guid _trackId;
    private readonly int _sceneIndex;
    private readonly IPlaybackModeService _playback;
    private readonly SessionViewModel _owner;
    private readonly ISelectionService _selection;

    public SessionSlotViewModel(SessionClip? clip, Guid trackId, int sceneIndex, string trackColorKey,
        IPlaybackModeService playback, SessionViewModel owner, ISelectionService selection)
    {
        _clip = clip;
        _trackId = trackId;
        _sceneIndex = sceneIndex;
        TrackColorKey = trackColorKey;
        _playback = playback;
        _owner = owner;
        _selection = selection;

        LaunchCommand = new RelayCommand(Launch, () => _clip is not null);
        StopCommand = new RelayCommand(Stop, () => _clip is not null && IsPlaying);
        QueueCommand = new RelayCommand(Queue, () => _clip is not null && !IsPlaying);
        SelectCommand = new RelayCommand(() => _owner.SelectSlot(this), () => _clip is not null);
        AssignFromSelectedCommand = new RelayCommand(AssignFromSelected, CanAssignFromSelected);
        ClearSlotCommand = new RelayCommand(ClearSlot, () => _clip is not null);
    }

    public SessionClip? Clip => _clip;
    public Guid TrackId => _trackId;
    public int SceneIndex => _sceneIndex;
    public bool HasClip => _clip is not null;
    public string DisplayName => _clip?.Name ?? "+";
    public string TrackColorKey { get; }

    public bool IsPlaying => _clip?.IsPlaying ?? false;
    public bool IsQueued => _clip?.IsQueued ?? false;
    public bool IsEmpty => _clip is null;
    public bool IsSelected => _owner.SelectedSlot == this;
    public bool CanAssignSource { get; private set; }
    public bool IsGateMode => _clip?.LaunchMode == SessionLaunchMode.Gate;

    public ICommand LaunchCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand QueueCommand { get; }
    public ICommand SelectCommand { get; }
    public ICommand AssignFromSelectedCommand { get; }
    public ICommand ClearSlotCommand { get; }

    public void SelectForInspector()
    {
        if (_clip is not null)
            _owner.SelectSlot(this);
    }

    private void Launch()
    {
        if (_clip is null) return;
        if (_clip.LaunchMode == SessionLaunchMode.Gate)
            _playback.GateClip(_clip.Id, held: true);
        else
            _playback.LaunchClip(_clip.Id);
    }

    public void PressGate()
    {
        if (_clip?.LaunchMode == SessionLaunchMode.Gate)
            _playback.GateClip(_clip.Id, held: true);
    }

    public void LaunchImmediate()
    {
        if (_clip is null) return;
        _playback.LaunchClip(_clip.Id);
    }

    public void AssignFromSelection() => AssignFromSelected();

    public void ReleaseGate()
    {
        if (_clip?.LaunchMode == SessionLaunchMode.Gate)
            _playback.GateClip(_clip.Id, held: false);
    }

    private void Queue()
    {
        if (_clip is not null) _playback.QueueClip(_clip.Id);
    }

    private void Stop()
    {
        if (_clip is not null) _playback.StopClip(_clip.Id);
    }

    private void ClearSlot() => _owner.TryClearSlot(_sceneIndex, _trackId);

    private bool CanAssignFromSelected()
        => IsEmpty && _selection.SelectedClip is { } clip
           && _selection.SelectedTrack?.Id == _trackId
           && clip.Id != Guid.Empty;

    private void AssignFromSelected()
    {
        if (_selection.SelectedClip is not { } clip || _selection.SelectedTrack?.Id != _trackId) return;
        _owner.TryAssignToSlot(_sceneIndex, _trackId, clip);
    }

    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsQueued));
        OnPropertyChanged(nameof(IsSelected));
        (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (QueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearSlotCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void RefreshAssignable()
    {
        var canAssign = CanAssignFromSelected();
        if (canAssign == CanAssignSource) return;
        CanAssignSource = canAssign;
        OnPropertyChanged(nameof(CanAssignSource));
        (AssignFromSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void RefreshDisplay() => OnPropertyChanged(nameof(DisplayName));
}
