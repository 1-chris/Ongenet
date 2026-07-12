using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Services;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Left-sidebar controls for a selected pattern track — row sources and ordering.</summary>
public sealed class PatternTrackInspectorViewModel : ViewModelBase
{
    private readonly ISelectionService _selection;
    private readonly IProjectService _project;
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private readonly IInstrumentRegistry _instruments;

    private Pattern? _pattern;
    private Track? _track;

    public PatternTrackInspectorViewModel(ISelectionService selection, IProjectService project,
        IEventAggregator events, IHistoryService history, IInstrumentRegistry instruments)
    {
        _selection = selection;
        _project = project;
        _events = events;
        _history = history;
        _instruments = instruments;

        _selection.SelectionChanged += Rebind;
        _events.Subscribe<PatternsChangedEvent>(_ => Rebind());
        _events.Subscribe<PatternClipsChangedEvent>(_ => Rebind());

        AddInstrumentRowCommand = new RelayCommand(AddInstrumentRow, () => CanAddInstrumentRow);
        AddSampleRowCommand = new RelayCommand(AddSampleRow, () => CanAddSampleRow);
        Rebind();
    }

    public ObservableCollection<PatternRowInspectorViewModel> Rows { get; } = new();
    public ObservableCollection<Track> InstrumentTrackOptions { get; } = new();
    public ObservableCollection<AudioClipOption> SampleOptions { get; } = new();

    public Track? SelectedInstrumentTrack { get; set; }
    public AudioClipOption? SelectedSample { get; set; }

    public bool HasPatternTrack => _track is { Kind: TrackKind.Pattern } && _pattern is not null;

    public string PatternName
    {
        get => _pattern?.Name ?? string.Empty;
        set
        {
            if (_pattern is null || _pattern.Name == value) return;
            _history.Capture("Rename pattern");
            _pattern.Name = value;
            OnPropertyChanged();
            NotifyPatternsChanged();
        }
    }

    public bool CanAddInstrumentRow => HasPatternTrack && SelectedInstrumentTrack is not null;
    public bool CanAddSampleRow => HasPatternTrack && SelectedSample is not null;

    public RelayCommand AddInstrumentRowCommand { get; }
    public RelayCommand AddSampleRowCommand { get; }

    public void Rebind()
    {
        _track = _selection.SelectedTrack is { Kind: TrackKind.Pattern } t ? t : null;
        _pattern = PatternTrackHelper.ResolvePattern(_project.Current, _track, _selection.SelectedPatternClip);

        Rows.Clear();
        InstrumentTrackOptions.Clear();
        SampleOptions.Clear();

        if (_pattern is not null)
        {
            foreach (var ch in _pattern.OrderedChannels)
                Rows.Add(new PatternRowInspectorViewModel(ch, _pattern, MoveRowUp, MoveRowDown, RemoveRow, NotifyPatternsChanged));

            foreach (var inst in _project.Current.Tracks.Where(t => t.Kind == TrackKind.Instrument))
                InstrumentTrackOptions.Add(inst);

            foreach (var track in _project.Current.Tracks)
            {
                foreach (var clip in track.Clips.Where(c => c.IsAudio && c.Samples is not null))
                    SampleOptions.Add(new AudioClipOption(clip, track));
            }

            SelectedInstrumentTrack ??= InstrumentTrackOptions.FirstOrDefault();
            SelectedSample ??= SampleOptions.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasPatternTrack));
        OnPropertyChanged(nameof(PatternName));
        OnPropertyChanged(nameof(CanAddInstrumentRow));
        OnPropertyChanged(nameof(CanAddSampleRow));
        AddInstrumentRowCommand.RaiseCanExecuteChanged();
        AddSampleRowCommand.RaiseCanExecuteChanged();
    }

    private void AddInstrumentRow()
    {
        if (_pattern is null || SelectedInstrumentTrack is null) return;
        _history.Capture("Add pattern row");
        PatternTrackHelper.AddInstrumentRow(_pattern, SelectedInstrumentTrack);
        NotifyPatternsChanged();
        Rebind();
    }

    private void AddSampleRow()
    {
        if (_pattern is null || SelectedSample is null) return;
        _history.Capture("Add sample pattern row");
        var sampler = new BasicSamplerInstrument();
        sampler.LoadSample(SelectedSample.Clip.Samples!, SelectedSample.Clip.Name);
        var track = new Track
        {
            Name = SelectedSample.Clip.Name,
            Kind = TrackKind.Instrument,
            ColorKey = "CatppuccinSky"
        };
        track.Instruments.Add(new InstrumentSlot(sampler));
        track.CommitInstruments();

        var masterIdx = _project.Current.Tracks.FindIndex(t => t.Kind == TrackKind.Master);
        if (masterIdx >= 0) _project.Current.Tracks.Insert(masterIdx, track);
        else _project.Current.Tracks.Add(track);

        PatternTrackHelper.AddSampleRow(_pattern, track, SelectedSample.Clip);
        _events.Publish(new TracksChangedEvent());
        NotifyPatternsChanged();
        Rebind();
    }

    private void MoveRowUp(PatternRowInspectorViewModel row)
    {
        if (_pattern is null) return;
        var ordered = _pattern.OrderedChannels.ToList();
        var index = ordered.FindIndex(c => c.Id == row.Channel.Id);
        if (index <= 0) return;
        _history.Capture("Reorder pattern row");
        _pattern.ReorderChannel(row.Channel.Id, index - 1);
        NotifyPatternsChanged();
        Rebind();
    }

    private void MoveRowDown(PatternRowInspectorViewModel row)
    {
        if (_pattern is null) return;
        var ordered = _pattern.OrderedChannels.ToList();
        var index = ordered.FindIndex(c => c.Id == row.Channel.Id);
        if (index < 0 || index >= ordered.Count - 1) return;
        _history.Capture("Reorder pattern row");
        _pattern.ReorderChannel(row.Channel.Id, index + 1);
        NotifyPatternsChanged();
        Rebind();
    }

    private void RemoveRow(PatternRowInspectorViewModel row)
    {
        if (_pattern is null) return;
        _history.Capture("Remove pattern row");
        var seq = _pattern.StepSequences.FirstOrDefault(s => s.PatternChannelId == row.Channel.Id);
        if (seq is not null) _pattern.StepSequences.Remove(seq);
        _pattern.Channels.Remove(row.Channel);
        NotifyPatternsChanged();
        Rebind();
    }

    private void NotifyPatternsChanged() => _events.Publish(new PatternsChangedEvent());
}

public sealed class AudioClipOption
{
    public AudioClipOption(Clip clip, Track track)
    {
        Clip = clip;
        Track = track;
        Label = $"{track.Name} / {clip.Name}";
    }

    public Clip Clip { get; }
    public Track Track { get; }
    public string Label { get; }
}

public sealed class PatternRowInspectorViewModel : ViewModelBase
{
    private readonly Pattern _pattern;
    private readonly Action<PatternRowInspectorViewModel> _moveUp;
    private readonly Action<PatternRowInspectorViewModel> _moveDown;
    private readonly Action<PatternRowInspectorViewModel> _remove;
    private readonly Action _notify;

    public PatternRowInspectorViewModel(PatternChannel channel, Pattern pattern,
        Action<PatternRowInspectorViewModel> moveUp, Action<PatternRowInspectorViewModel> moveDown,
        Action<PatternRowInspectorViewModel> remove, Action notify)
    {
        Channel = channel;
        _pattern = pattern;
        _moveUp = moveUp;
        _moveDown = moveDown;
        _remove = remove;
        _notify = notify;
        MoveUpCommand = new RelayCommand(() => _moveUp(this));
        MoveDownCommand = new RelayCommand(() => _moveDown(this));
        RemoveCommand = new RelayCommand(() => _remove(this));
    }

    public PatternChannel Channel { get; }

    public string Name
    {
        get => Channel.Name;
        set
        {
            if (Channel.Name == value) return;
            Channel.Name = value;
            OnPropertyChanged();
            _notify();
        }
    }

    public bool IsMuted
    {
        get => Channel.Muted;
        set
        {
            if (Channel.Muted == value) return;
            Channel.Muted = value;
            OnPropertyChanged();
            _notify();
        }
    }

    public double Volume
    {
        get => Channel.Volume;
        set
        {
            if (Math.Abs(Channel.Volume - value) < 1e-9) return;
            Channel.Volume = value;
            OnPropertyChanged();
            _notify();
        }
    }

    public string SourceLabel => Channel.SourceKind == PatternRowSourceKind.AudioSample ? "Sample" : "Instrument";

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand RemoveCommand { get; }
}
