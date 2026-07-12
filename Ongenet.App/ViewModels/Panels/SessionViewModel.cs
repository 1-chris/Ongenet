using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media;
using Ongenet.App.Localization;
using Ongenet.App.Services;
using Ongenet.App.Theming;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Session view — clip launcher grid (tracks × scenes).</summary>
public sealed class SessionViewModel : ViewModelBase
{
    /// <summary>Horizontal footprint per scene/slot cell (88px cell + 2px margin each side).</summary>
    public const double SlotCellStride = 92;

    /// <summary>Maximum scene slots auto-fill assigns per track.</summary>
    public const int MaxAutoFillScenes = 8;
    private static readonly string[] SceneColors =
    [
        "CatppuccinGreen", "CatppuccinBlue", "CatppuccinPeach", "CatppuccinPink",
        "CatppuccinTeal", "CatppuccinYellow", "CatppuccinLavender", "CatppuccinRed"
    ];

    private static readonly double[] QuantizeOptions = [0, 0.25, 0.5, 1, 2, 4];

    private readonly IProjectService _project;
    private readonly IPlaybackModeService _playback;
    private readonly ISessionCaptureService _capture;
    private readonly ISessionMidiMapService _sessionMidi;
    private readonly ISelectionService _selection;
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private readonly IPlaybackClock _clock;
    private SessionSlotViewModel? _selectedSlot;
    private long _lastMeterMs;

    public SessionViewModel(IProjectService project, IPlaybackModeService playback, ISessionCaptureService capture,
        ISessionMidiMapService sessionMidi, ISelectionService selection, IEventAggregator events,
        IHistoryService history, IPlaybackClock clock)
    {
        _project = project;
        _playback = playback;
        _capture = capture;
        _sessionMidi = sessionMidi;
        _selection = selection;
        _events = events;
        _history = history;
        _clock = clock;
        StopAllCommand = new RelayCommand(_playback.StopAll);
        CaptureCommand = new RelayCommand(() => _capture.Capture(),
            () => SessionRecordArmed && _capture.PendingLaunchCount > 0);
        ClearSelectedSlotCommand = new RelayCommand(ClearSelectedSlot, () => HasSelection);
        SwitchToHybridModeCommand = new RelayCommand(() => PlaybackMode = PlaybackMode.Hybrid);
        AutoFillFromArrangementCommand = new RelayCommand(AutoFillFromArrangement, CanAutoFillFromArrangement);
        _project.ProjectChanged += Rebuild;
        _playback.ActiveClipsChanged += OnActiveClipsChanged;
        _playback.ModeChanged += OnPlaybackModeChanged;
        _capture.PendingChanged += OnCapturePendingChanged;
        _capture.SessionRecordArmedChanged += OnSessionRecordArmedChanged;
        _sessionMidi.LearnStateChanged += OnSessionMidiLearnChanged;
        _selection.SelectionChanged += OnSelectionChanged;
        _events.Subscribe<SessionClipsChangedEvent>(_ => Rebuild());
        _events.Subscribe<ClipChangedEvent>(_ =>
            (AutoFillFromArrangementCommand as RelayCommand)?.RaiseCanExecuteChanged());
        ThemePalette.Changed += OnThemePaletteChanged;
        _clock.Tick += OnPlaybackTick;
        Rebuild();
    }

    public ObservableCollection<SessionSceneColumnViewModel> SceneColumns { get; } = new();
    public ObservableCollection<SessionTrackRowViewModel> TrackRows { get; } = new();

    public ICommand StopAllCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand ClearSelectedSlotCommand { get; }
    public ICommand SwitchToHybridModeCommand { get; }
    public ICommand AutoFillFromArrangementCommand { get; }

    public int SceneCount => SceneColumns.Count;

    public double SceneGridWidth => SceneColumns.Count * SlotCellStride;

    public bool HasAnySessionClips => _project.Current.SessionClips.Count > 0;

    public PlaybackMode PlaybackMode
    {
        get => _playback.Mode;
        set => _playback.Mode = value;
    }

    public bool ShowArrangementModeWarning => PlaybackMode == PlaybackMode.Arrangement;

    public bool ShowCrossfaderWarning =>
        PlaybackMode == PlaybackMode.Hybrid && SessionCrossfader < 0.01;

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
            OnPropertyChanged(nameof(ShowCrossfaderWarning));
        }
    }

    public int PendingCaptureCount => _capture.PendingLaunchCount;

    public bool SessionRecordArmed => _capture.SessionRecordArmed;

    public bool ShowCaptureControls => SessionRecordArmed;

    public void BeginLearnLaunchSlot(Guid trackId, int sceneIndex)
        => _sessionMidi.BeginLearn(SessionMidiAction.LaunchSlot, trackId, sceneIndex);

    public void BeginLearnLaunchScene(int sceneIndex)
        => _sessionMidi.BeginLearn(SessionMidiAction.LaunchScene, sceneIndex: sceneIndex);

    public void CancelMidiLearn() => _sessionMidi.CancelLearn();

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
        AddSessionClipForSlot(sceneIndex, trackId, source);
        _events.Publish(new SessionClipsChangedEvent());
        return true;
    }

    /// <summary>Arrangement clips on a track, ordered by timeline position.</summary>
    public IReadOnlyList<Clip> GetArrangementClipsForTrack(Guid trackId)
    {
        var track = _project.Current.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null) return Array.Empty<Clip>();
        return track.Clips.OrderBy(c => c.StartBeat).ToList();
    }

    internal string FormatClipPickerLabel(Clip clip, IReadOnlyList<Clip> allOnTrack)
    {
        if (allOnTrack.Count(c => c.Name == clip.Name) <= 1)
            return clip.Name;
        return Loc.Format("Session_Clip_picker_label", clip.Name, clip.StartBeat);
    }

    private void AddSessionClipForSlot(int sceneIndex, Guid trackId, Clip source)
    {
        var existing = _project.Current.SessionClips
            .FirstOrDefault(c => c.TrackId == trackId && c.SceneIndex == sceneIndex);
        if (existing is not null)
            _project.Current.SessionClips.Remove(existing);

        _project.Current.SessionClips.Add(new SessionClip
        {
            TrackId = trackId,
            SceneIndex = sceneIndex,
            Name = source.Name,
            LengthBeats = source.LengthBeats,
            SourceClipId = source.Id
        });
    }

    private bool CanAutoFillFromArrangement()
        => _project.Current.Tracks
            .Where(t => t.Kind is TrackKind.Audio or TrackKind.Instrument)
            .Any(t => t.Clips.Count > 0);

    private void AutoFillFromArrangement()
    {
        _history.Capture("Auto-fill session from arrangement");

        foreach (var track in _project.Current.Tracks.Where(t => t.Kind is TrackKind.Audio or TrackKind.Instrument))
        {
            foreach (var sc in _project.Current.SessionClips.Where(c => c.TrackId == track.Id).ToList())
            {
                _playback.StopClip(sc.Id);
                _project.Current.SessionClips.Remove(sc);
            }

            var clips = track.Clips
                .OrderBy(c => c.StartBeat)
                .GroupBy(c => c.Name)
                .Select(g => g.First())
                .Take(MaxAutoFillScenes)
                .ToList();

            for (var scene = 0; scene < clips.Count; scene++)
                AddSessionClipForSlot(scene, track.Id, clips[scene]);
        }

        _events.Publish(new SessionClipsChangedEvent());
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
                () => StopScene(sceneIndex),
                () => BeginLearnLaunchScene(sceneIndex),
                _sessionMidi));
        }

        foreach (var track in _project.Current.Tracks.Where(t => t.Kind is TrackKind.Audio or TrackKind.Instrument))
        {
            var row = new SessionTrackRowViewModel(track.Name, track.ColorKey);
            for (var s = 0; s < sceneCount; s++)
            {
                var clip = clips.FirstOrDefault(c => c.TrackId == track.Id && c.SceneIndex == s);
                row.Slots.Add(new SessionSlotViewModel(clip, track.Id, s, track.ColorKey, _playback, this, _selection,
                    _sessionMidi));
            }
            row.SlotsGridWidth = sceneCount * SlotCellStride;
            row.SceneCount = sceneCount;
            TrackRows.Add(row);
        }

        OnPropertyChanged(nameof(SceneCount));
        OnPropertyChanged(nameof(SceneGridWidth));
        OnPropertyChanged(nameof(HasAnySessionClips));
        RefreshAssignableSlots();
        (AutoFillFromArrangementCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnThemePaletteChanged()
    {
        foreach (var column in SceneColumns)
            column.NotifyThemeChanged();
    }

    private void OnPlaybackModeChanged()
    {
        OnPropertyChanged(nameof(PlaybackMode));
        OnPropertyChanged(nameof(ShowArrangementModeWarning));
        OnPropertyChanged(nameof(ShowCrossfaderWarning));
    }

    private void StopScene(int sceneIndex)
    {
        foreach (var clip in _project.Current.SessionClips.Where(c => c.SceneIndex == sceneIndex))
            _playback.StopClip(clip.Id);
    }

    private void OnActiveClipsChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RefreshSlotStates();
            OnPropertyChanged(nameof(SessionCrossfader));
            OnPropertyChanged(nameof(ShowCrossfaderWarning));
            OnPropertyChanged(nameof(ShowArrangementModeWarning));
            OnPropertyChanged(nameof(PlaybackMode));
        });
    }

    private void OnPlaybackTick() => RefreshSlotMeters();

    internal float GetTrackMeterLevel(Guid trackId)
    {
        var track = _project.Current.Tracks.FirstOrDefault(t => t.Id == trackId);
        return track?.MeterLevel ?? 0f;
    }

    private void RefreshSlotMeters()
    {
        var now = Environment.TickCount64;
        if (now - _lastMeterMs < 33) return;
        _lastMeterMs = now;

        foreach (var row in TrackRows)
        foreach (var slot in row.Slots)
            slot.RefreshMeter();
    }

    private void RefreshSlotStates()
    {
        foreach (var row in TrackRows)
        foreach (var slot in row.Slots)
            slot.RefreshState();
    }

    private void OnCapturePendingChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(PendingCaptureCount));
            (CaptureCommand as RelayCommand)?.RaiseCanExecuteChanged();
        });
    }

    private void OnSessionRecordArmedChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(SessionRecordArmed));
            OnPropertyChanged(nameof(ShowCaptureControls));
            OnPropertyChanged(nameof(PendingCaptureCount));
            (CaptureCommand as RelayCommand)?.RaiseCanExecuteChanged();
        });
    }

    private void OnSessionMidiLearnChanged()
        => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshSlotLearnState);

    private void RefreshSlotLearnState()
    {
        foreach (var column in SceneColumns)
            column.RefreshLearnState();
        foreach (var row in TrackRows)
        foreach (var slot in row.Slots)
            slot.RefreshLearnState();
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
    private readonly ISessionMidiMapService _sessionMidi;

    public SessionSceneColumnViewModel(int sceneIndex, string colorKey, Action launch, Action stop, Action learn,
        ISessionMidiMapService sessionMidi)
    {
        SceneIndex = sceneIndex;
        Label = $"Scene {sceneIndex + 1}";
        ColorKey = colorKey;
        _sessionMidi = sessionMidi;
        LaunchCommand = new RelayCommand(launch);
        StopCommand = new RelayCommand(stop);
        LearnLaunchCommand = new RelayCommand(learn);
    }

    public int SceneIndex { get; }
    public string Label { get; }
    public string ColorKey { get; }
    public IBrush LabelForeground => ContrastForeground.BrushForColorKey(ColorKey);
    public ICommand LaunchCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand LearnLaunchCommand { get; }

    public bool IsLearningLaunch =>
        _sessionMidi.LearnTarget is { Action: SessionMidiAction.LaunchScene, SceneIndex: var s } && s == SceneIndex;

    public string LearnLaunchText => IsLearningLaunch ? "Listening…" : "Learn MIDI";

    public void NotifyThemeChanged() => OnPropertyChanged(nameof(LabelForeground));

    public void RefreshLearnState()
    {
        OnPropertyChanged(nameof(IsLearningLaunch));
        OnPropertyChanged(nameof(LearnLaunchText));
    }
}

public sealed class SessionTrackRowViewModel : ViewModelBase
{
    private double _slotsGridWidth;
    private int _sceneCount;

    public SessionTrackRowViewModel(string trackName, string colorKey)
    {
        TrackName = trackName;
        ColorKey = colorKey;
    }

    public string TrackName { get; }
    public string ColorKey { get; }
    public ObservableCollection<SessionSlotViewModel> Slots { get; } = new();

    public int SceneCount
    {
        get => _sceneCount;
        set
        {
            if (_sceneCount == value) return;
            _sceneCount = value;
            OnPropertyChanged();
        }
    }

    public double SlotsGridWidth
    {
        get => _slotsGridWidth;
        set
        {
            if (Math.Abs(_slotsGridWidth - value) < 1e-9) return;
            _slotsGridWidth = value;
            OnPropertyChanged();
        }
    }
}

public sealed class SessionSlotViewModel : ViewModelBase
{
    private readonly SessionClip? _clip;
    private readonly Guid _trackId;
    private readonly int _sceneIndex;
    private readonly IPlaybackModeService _playback;
    private readonly SessionViewModel _owner;
    private readonly ISelectionService _selection;
    private readonly ISessionMidiMapService _sessionMidi;

    public SessionSlotViewModel(SessionClip? clip, Guid trackId, int sceneIndex, string trackColorKey,
        IPlaybackModeService playback, SessionViewModel owner, ISelectionService selection,
        ISessionMidiMapService sessionMidi)
    {
        _clip = clip;
        _trackId = trackId;
        _sceneIndex = sceneIndex;
        TrackColorKey = trackColorKey;
        _playback = playback;
        _owner = owner;
        _selection = selection;
        _sessionMidi = sessionMidi;

        LaunchCommand = new RelayCommand(Launch, () => _clip is not null);
        StopCommand = new RelayCommand(Stop, () => _clip is not null && IsPlaying);
        QueueCommand = new RelayCommand(Queue, () => _clip is not null && !IsPlaying);
        SelectCommand = new RelayCommand(() => _owner.SelectSlot(this), () => _clip is not null);
        AssignFromSelectedCommand = new RelayCommand(AssignFromSelected, CanAssignFromSelected);
        ClearSlotCommand = new RelayCommand(ClearSlot, () => _clip is not null);
        LearnLaunchCommand = new RelayCommand(LearnLaunchMidi, () => _clip is not null);
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

    private float _meterLevel;

    public float MeterLevel => _meterLevel;

    public string SlotToolTip => IsEmpty
        ? Loc.Get("Session_Choose_arrangement_clip_Tip")
        : Loc.Get("Session_Click_slot_to_launch_Tip");

    public ICommand LaunchCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand QueueCommand { get; }
    public ICommand SelectCommand { get; }
    public ICommand AssignFromSelectedCommand { get; }
    public ICommand ClearSlotCommand { get; }
    public ICommand LearnLaunchCommand { get; }

    public bool IsLearningLaunch =>
        _sessionMidi.LearnTarget is { Action: SessionMidiAction.LaunchSlot, TrackId: var tid, SceneIndex: var s }
        && tid == _trackId && s == _sceneIndex;

    public string LearnLaunchText => IsLearningLaunch ? "Listening…" : "Learn MIDI launch";

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

    public void OpenAssignPicker(Control anchor)
    {
        if (!IsEmpty) return;

        var clips = _owner.GetArrangementClipsForTrack(_trackId);
        var flyout = new MenuFlyout();

        if (clips.Count == 0)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = Loc.Get("Session_No_clips_on_track"),
                IsEnabled = false
            });
        }
        else
        {
            foreach (var clip in clips)
            {
                var arrangementClip = clip;
                var item = new MenuItem { Header = _owner.FormatClipPickerLabel(arrangementClip, clips) };
                item.Click += (_, _) => _owner.TryAssignToSlot(_sceneIndex, _trackId, arrangementClip);
                flyout.Items.Add(item);
            }
        }

        flyout.ShowAt(anchor, true);
    }

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

    private void LearnLaunchMidi() => _owner.BeginLearnLaunchSlot(_trackId, _sceneIndex);

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
        RefreshMeter(force: true);
        (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (QueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearSlotCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void RefreshMeter(bool force = false)
    {
        var level = IsPlaying ? _owner.GetTrackMeterLevel(_trackId) : 0f;
        if (!force && Math.Abs(level - _meterLevel) < 0.02f) return;
        _meterLevel = level;
        OnPropertyChanged(nameof(MeterLevel));
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

    public void RefreshLearnState()
    {
        OnPropertyChanged(nameof(IsLearningLaunch));
        OnPropertyChanged(nameof(LearnLaunchText));
    }
}
