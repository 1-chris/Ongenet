using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Services;
using Ongenet.Core.Audio;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

public sealed class AudioEditorLaneViewModel : ViewModelBase
{
    public AudioEditorLaneViewModel(Clip clip, SampleEditorCoreViewModel editor)
    {
        Clip = clip;
        Editor = editor;
        TrackName = clip.Name;
    }

    public Clip Clip { get; }
    public SampleEditorCoreViewModel Editor { get; }
    public string TrackName { get; }

    public string DisplayName => $"{TrackName} — {Editor.SourceInfo}";
}

/// <summary>Edison-class multitrack audio editor with one lane per open clip.</summary>
public sealed class AudioEditorViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private AudioEditorLaneViewModel? _activeLane;

    public AudioEditorViewModel(
        IProjectService project,
        ITransportService transport,
        IEventAggregator events,
        IHistoryService history,
        IAuditionPlayer audition,
        IPlaybackClock clock)
    {
        _project = project;
        _transport = transport;
        _events = events;
        _history = history;
        _audition = audition;
        _clock = clock;
        RemoveLaneCommand = new RelayCommand<AudioEditorLaneViewModel>(RemoveLane, lane => lane is not null);
    }

    private readonly ITransportService _transport;
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private readonly IAuditionPlayer _audition;
    private readonly IPlaybackClock _clock;

    public ObservableCollection<AudioEditorLaneViewModel> Lanes { get; } = new();
    public RelayCommand<AudioEditorLaneViewModel> RemoveLaneCommand { get; }

    public SampleEditorCoreViewModel? ActiveEditor => _activeLane?.Editor;

    public string Title => _activeLane is null
        ? L("AudioEditor_Title")
        : string.Format(L("AudioEditor_TitleClip"), _activeLane.Clip.Name);

    public bool HasLanes => Lanes.Count > 0;

    public void OpenClip(Clip clip)
    {
        if (!clip.IsAudio) return;

        var existing = Lanes.FirstOrDefault(l => ReferenceEquals(l.Clip, clip));
        if (existing is not null)
        {
            SetActiveLane(existing);
            return;
        }

        var editor = new SampleEditorCoreViewModel(_transport, _events, _project, _history, _audition, _clock);
        editor.BindClip(clip);
        var lane = new AudioEditorLaneViewModel(clip, editor);
        Lanes.Add(lane);
        SetActiveLane(lane);
    }

    public void SetActiveLane(AudioEditorLaneViewModel? lane)
    {
        if (_activeLane is not null)
            _activeLane.Editor.StopAudition();

        _activeLane = lane;
        OnPropertyChanged(nameof(ActiveEditor));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ActiveLane));
    }

    public AudioEditorLaneViewModel? ActiveLane => _activeLane;

    private void RemoveLane(AudioEditorLaneViewModel? lane)
    {
        if (lane is null) return;
        lane.Editor.StopAudition();
        Lanes.Remove(lane);
        if (ReferenceEquals(_activeLane, lane))
            SetActiveLane(Lanes.LastOrDefault());
    }

    public void CloseAll()
    {
        foreach (var lane in Lanes.ToList())
            lane.Editor.StopAudition();
        Lanes.Clear();
        SetActiveLane(null);
    }
}
