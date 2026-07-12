using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Ongenet.App.ViewModels.Timeline;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>Editable pitch blob for one detected note segment.</summary>
public sealed class PitchSegmentItemViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private double _pitchCents;

    public PitchSegmentItemViewModel(PitchNoteSegment model, int index, Action onChanged)
    {
        Model = model;
        Index = index;
        _onChanged = onChanged;
        _pitchCents = model.PitchCents;
    }

    public PitchNoteSegment Model { get; }
    public int Index { get; }

    public long StartSample => Model.StartSample;
    public long EndSample => Model.EndSample;
    public float Amplitude => Model.Amplitude;

    public string Label => L("PolyPitch_Segment_label", Index + 1, StartSample, EndSample);

    public double PitchCents
    {
        get => _pitchCents;
        set
        {
            if (!SetField(ref _pitchCents, value)) return;
            Model.PitchCents = value;
            _onChanged();
        }
    }
}

/// <summary>Polyphonic pitch editor: analyze blobs, drag cents per segment, waveform overlay.</summary>
public sealed class PolyphonicPitchEditorViewModel : ViewModelBase
{
    private readonly ClipViewModel _clip;
    private readonly IHistoryService _history;
    private readonly IEventAggregator _events;
    private PitchSegmentItemViewModel? _selectedSegment;
    private int _segmentRevision;
    private bool _isAnalyzing;
    private string _status = "";

    public PolyphonicPitchEditorViewModel(ClipViewModel clip, IHistoryService history, IEventAggregator events)
    {
        _clip = clip;
        _history = history;
        _events = events;
        _status = L("PolyPitch_Ready");
        AnalyzeCommand = new RelayCommand(() => _ = AnalyzeAsync(), () => CanAnalyze);
        ReloadSegments();
    }

    public RelayCommand AnalyzeCommand { get; }

    public string ClipName => _clip.Name;
    public AudioWaveform? Waveform => _clip.Model.Waveform;
    public long TotalFrames => _clip.Model.Samples?.FrameCount ?? 0;

    public ObservableCollection<PitchSegmentItemViewModel> Segments { get; } = new();

    public IReadOnlyList<PitchNoteSegment> PitchSegments => _clip.Model.PitchSegments;

    public int SelectedIndex
    {
        get => SelectedSegment?.Index ?? -1;
        set
        {
            if (value >= 0 && value < Segments.Count)
                SelectedSegment = Segments[value];
        }
    }

    public PitchSegmentItemViewModel? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            if (!SetField(ref _selectedSegment, value)) return;
            OnPropertyChanged(nameof(SelectedIndex));
        }
    }

    public int SegmentRevision
    {
        get => _segmentRevision;
        private set => SetField(ref _segmentRevision, value);
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (!SetField(ref _isAnalyzing, value)) return;
            OnPropertyChanged(nameof(CanAnalyze));
            AnalyzeCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanAnalyze => !IsAnalyzing && _clip.Model.Samples is not null && _clip.Model.LengthBeats > 0;

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public void AdjustSelectedPitchCents(double deltaCents)
    {
        if (SelectedSegment is null) return;
        SelectedSegment.PitchCents = Math.Clamp(SelectedSegment.PitchCents + deltaCents, -2400, 2400);
    }

    private async Task AnalyzeAsync()
    {
        var clip = _clip.Model;
        if (clip.Samples is null || clip.LengthBeats <= 0) return;

        IsAnalyzing = true;
        Status = L("PolyPitch_Analyzing");
        try
        {
            var detected = await Task.Run(() =>
                PolyphonicPitchAnalyzer.Analyze(clip.Samples, clip.LengthBeats)).ConfigureAwait(true);

            _history.Capture(L("PolyPitch_Analyze_history"));
            clip.PitchSegments.Clear();
            foreach (var seg in detected)
                clip.PitchSegments.Add(seg);

            ReloadSegments();
            _clip.RefreshFromModel();
            _events.Publish(new ClipChangedEvent(clip));
            Status = L("PolyPitch_Analyzed_count", detected.Count);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void ReloadSegments()
    {
        Segments.Clear();
        var i = 0;
        foreach (var seg in _clip.Model.PitchSegments)
            Segments.Add(new PitchSegmentItemViewModel(seg, i++, OnSegmentEdited));
        SelectedSegment = Segments.Count > 0 ? Segments[0] : null;
        SegmentRevision++;
    }

    private void OnSegmentEdited()
    {
        _history.Capture(L("PolyPitch_Edit_pitch"));
        SegmentRevision++;
        _clip.RefreshFromModel();
        _events.Publish(new ClipChangedEvent(_clip.Model));
    }
}
