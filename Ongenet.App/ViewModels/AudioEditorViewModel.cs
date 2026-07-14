using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Ongenet.App.Services;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

public sealed class AudioEditorSliceItemViewModel : ViewModelBase
{
    private bool _selected;

    public AudioEditorSliceItemViewModel(int index, AudioSliceRegion region, int sampleRate)
    {
        Index = index;
        Region = region;
        SampleRate = sampleRate;
        _selected = region.Selected;
    }

    public int Index { get; }
    public AudioSliceRegion Region { get; }
    public int SampleRate { get; private set; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
                Region.Selected = value;
        }
    }

    public string Label => string.Create(CultureInfo.InvariantCulture,
        $"#{Index + 1} · {StartSeconds:0.###}–{EndSeconds:0.###} s");

    public double StartSeconds => Region.StartFrame / (double)SampleRate;
    public double EndSeconds => Region.EndFrame / (double)SampleRate;
}

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
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private AudioEditorLaneViewModel? _activeLane;
    private BeatSliceDetectMode _sliceDetectMode = BeatSliceDetectMode.Transients;
    private int _divisionsPerBeat = 4;
    private bool _transientSafeWarp = true;
    private AudioEditorSliceItemViewModel? _selectedSlice;

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
        SliceToGridCommand = new RelayCommand(SliceToGrid, () => ActiveEditor?.HasSample == true);
        ApplyBeatGridWarpCommand = new RelayCommand(() => _ = ApplyBeatGridWarpAsync(), () => ActiveEditor?.HasSample == true);
        ExportToSamplerCommand = new RelayCommand(ExportToSampler, () => HasSlices);
        MoveSliceUpCommand = new RelayCommand<AudioEditorSliceItemViewModel>(MoveSliceUp, CanMoveSlice);
        MoveSliceDownCommand = new RelayCommand<AudioEditorSliceItemViewModel>(MoveSliceDown, CanMoveSlice);
    }

    private readonly ITransportService _transport;
    private readonly IAuditionPlayer _audition;
    private readonly IPlaybackClock _clock;

    public ObservableCollection<AudioEditorLaneViewModel> Lanes { get; } = new();
    public ObservableCollection<AudioEditorSliceItemViewModel> Slices { get; } = new();
    public RelayCommand<AudioEditorLaneViewModel> RemoveLaneCommand { get; }
    public RelayCommand SliceToGridCommand { get; }
    public RelayCommand ApplyBeatGridWarpCommand { get; }
    public RelayCommand ExportToSamplerCommand { get; }
    public RelayCommand<AudioEditorSliceItemViewModel> MoveSliceUpCommand { get; }
    public RelayCommand<AudioEditorSliceItemViewModel> MoveSliceDownCommand { get; }

    public Array SliceDetectModes => Enum.GetValues<BeatSliceDetectMode>();

    public BeatSliceDetectMode SliceDetectMode
    {
        get => _sliceDetectMode;
        set => SetField(ref _sliceDetectMode, value);
    }

    public int DivisionsPerBeat
    {
        get => _divisionsPerBeat;
        set => SetField(ref _divisionsPerBeat, Math.Clamp(value, 1, 64));
    }

    public bool TransientSafeWarp
    {
        get => _transientSafeWarp;
        set => SetField(ref _transientSafeWarp, value);
    }

    public SampleEditorCoreViewModel? ActiveEditor => _activeLane?.Editor;

    public string Title => _activeLane is null
        ? L("AudioEditor_Title")
        : string.Format(L("AudioEditor_TitleClip"), _activeLane.Clip.Name);

    public bool HasLanes => Lanes.Count > 0;
    public bool HasSlices => Slices.Count > 0;

    public AudioEditorSliceItemViewModel? SelectedSlice
    {
        get => _selectedSlice;
        set => SetField(ref _selectedSlice, value);
    }

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
        RebuildSliceList();
        OnPropertyChanged(nameof(ActiveEditor));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ActiveLane));
        RaiseSliceCommands();
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

    private void SliceToGrid()
    {
        if (_activeLane?.Clip.Samples is not { } buffer) return;
        var secondsPerBeat = ActiveEditor?.GridSecondsPerBeat ?? 0;
        if (secondsPerBeat <= 0) secondsPerBeat = 60.0 / Math.Max(1.0, _transport.Tempo.BeatsPerMinute);

        _history.Capture("Slice to grid");
        BeatSliceOps.SliceToGrid(buffer, SliceDetectMode, secondsPerBeat, DivisionsPerBeat);
        RebuildSliceList();
        RaiseSliceCommands();
    }

    private async Task ApplyBeatGridWarpAsync()
    {
        if (_activeLane?.Clip is not { Samples: { } buffer } clip) return;
        var secondsPerBeat = ActiveEditor?.GridSecondsPerBeat ?? 0;
        if (secondsPerBeat <= 0) secondsPerBeat = 60.0 / Math.Max(1.0, _transport.Tempo.BeatsPerMinute);

        _history.Capture("Beat-grid warp");
        var warped = await Task.Run(() =>
            BeatGridWarpOps.ApplyBeatGridWarp(buffer, secondsPerBeat, beatsPerSegment: 1.0, TransientSafeWarp));
        if (!ReferenceEquals(clip, _activeLane?.Clip)) return;

        ApplyBufferChange(buffer, warped);
        BeatSliceOps.SliceToGrid(warped, BeatSliceDetectMode.EqualDivisions, secondsPerBeat, DivisionsPerBeat);
        AfterBufferEdit(clip);
    }

    private void ExportToSampler()
    {
        if (_activeLane?.Clip.Samples is not { } buffer || buffer.SliceRegions.Count == 0) return;
        if (AudioEditorService.FindTargetSampler(_project.Current) is not { } sampler) return;

        _history.Capture("Export slices to sampler");
        var regions = SamplerSliceExport.BuildRegions(buffer, buffer.SliceRegions,
            namePrefix: _activeLane.Clip.Name);
        if (regions.Count == 0) return;
        sampler.AppendRegions(regions);
        _events.Publish(new TracksChangedEvent());
    }

    private void MoveSliceUp(AudioEditorSliceItemViewModel? item)
    {
        if (item is null || _activeLane?.Clip.Samples is not { } buffer) return;
        _history.Capture("Reorder slice");
        BeatSliceOps.MoveRegionUp(buffer, item.Index);
        RebuildSliceList();
    }

    private void MoveSliceDown(AudioEditorSliceItemViewModel? item)
    {
        if (item is null || _activeLane?.Clip.Samples is not { } buffer) return;
        _history.Capture("Reorder slice");
        BeatSliceOps.MoveRegionDown(buffer, item.Index);
        RebuildSliceList();
    }

    private bool CanMoveSlice(AudioEditorSliceItemViewModel? item) => item is not null && HasSlices;

    private void RebuildSliceList()
    {
        Slices.Clear();
        if (_activeLane?.Clip.Samples is not { } buffer) return;

        var ordered = BeatSliceOps.OrderedRegions(buffer);
        for (var i = 0; i < ordered.Count; i++)
            Slices.Add(new AudioEditorSliceItemViewModel(i, ordered[i], buffer.SampleRate));

        SelectedSlice = Slices.FirstOrDefault();
        OnPropertyChanged(nameof(HasSlices));
    }

    private void RaiseSliceCommands()
    {
        SliceToGridCommand.RaiseCanExecuteChanged();
        ApplyBeatGridWarpCommand.RaiseCanExecuteChanged();
        ExportToSamplerCommand.RaiseCanExecuteChanged();
        MoveSliceUpCommand.RaiseCanExecuteChanged();
        MoveSliceDownCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasSlices));
    }

    private void ApplyBufferChange(AudioSampleBuffer oldBuffer, AudioSampleBuffer newBuffer)
    {
        if (_activeLane?.Editor is not { } editor || _activeLane.Clip is not { } clip) return;
        var clips = ClipSharingOps.EnumerateClips(_project.Current);
        BeatSliceOps.CopySliceRegions(oldBuffer, newBuffer);
        SampleEditOps.ReplaceSharedBuffer(clips, oldBuffer, newBuffer);
        _events.Publish(new ClipChangedEvent(clip));
        editor.BindClip(clip);
        RebuildSliceList();
    }

    private void AfterBufferEdit(Clip clip)
    {
        _events.Publish(new ClipChangedEvent(clip));
        ActiveEditor?.BindClip(clip);
        RebuildSliceList();
        RaiseSliceCommands();
    }
}
