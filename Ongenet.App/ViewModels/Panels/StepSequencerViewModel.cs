using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Services;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Step sequencer grid with per-step velocity.</summary>
public sealed class StepSequencerViewModel : ViewModelBase
{
    private static readonly string[] StepColors =
    [
        "CatppuccinMauve", "CatppuccinBlue", "CatppuccinGreen", "CatppuccinPeach",
        "CatppuccinPink", "CatppuccinTeal", "CatppuccinYellow", "CatppuccinRed"
    ];

    private Pattern? _pattern;
    private StepSequence? _sequence;
    private bool _suppressChangeEvent;
    private readonly IHistoryService _history;
    private readonly IEventAggregator _events;

    public StepSequencerViewModel(IHistoryService history, IEventAggregator events)
    {
        _history = history;
        _events = events;
    }

    public ObservableCollection<StepCellViewModel> Steps { get; } = new();

    public string ChannelLabel => _pattern?.Channels
        .FirstOrDefault(c => c.Id == _sequence?.PatternChannelId)?.Name ?? "Steps";

    public bool HasSequence => _sequence is not null;

    public double LengthBeats
    {
        get => _pattern?.LengthBeats ?? 4;
        set
        {
            if (_pattern is null || Math.Abs(_pattern.LengthBeats - value) < 1e-9) return;
            _history.Capture("Pattern length");
            _pattern.LengthBeats = Math.Max(1, value);
            OnPropertyChanged();
            _events.Publish(new PatternsChangedEvent());
        }
    }

    public int StepCount
    {
        get => _sequence?.StepCount > 0 ? _sequence.StepCount : PatternEditorViewModel.StepCountOptions[2];
        set
        {
            if (_sequence is null || _pattern is null || value <= 0) return;
            if (_sequence.StepCount == value) return;
            _history.Capture("Step count");
            ResizeSequence(_sequence, value);
            foreach (var seq in _pattern.StepSequences)
                ResizeSequence(seq, value);
            RebuildCells();
            OnPropertyChanged();
            RaiseStepsChanged();
            _events.Publish(new PatternsChangedEvent());
        }
    }

    /// <summary>Raised after step data changes (toggle, velocity, or external sync).</summary>
    public event Action? StepsChanged;

    public void Bind(Pattern? pattern, StepSequence? sequence)
    {
        _pattern = pattern;
        _sequence = sequence;
        RebuildCells();
    }

    public void RefreshFromSequence()
    {
        if (_sequence is null) return;
        _suppressChangeEvent = true;
        try
        {
            foreach (var cell in Steps)
                cell.Refresh();
        }
        finally
        {
            _suppressChangeEvent = false;
        }
        OnPropertyChanged(nameof(StepCount));
        OnPropertyChanged(nameof(LengthBeats));
    }

    private void RebuildCells()
    {
        Steps.Clear();

        if (_sequence is null)
        {
            OnPropertyChanged(nameof(ChannelLabel));
            OnPropertyChanged(nameof(HasSequence));
            OnPropertyChanged(nameof(StepCount));
            OnPropertyChanged(nameof(LengthBeats));
            return;
        }

        var count = StepCount;
        ResizeSequence(_sequence, count);
        for (var i = 0; i < count; i++)
        {
            var step = _sequence.Steps[i];
            var index = i;
            Steps.Add(new StepCellViewModel(index, step, StepColors[i % StepColors.Length], ToggleStep, OnStepChanged));
        }

        OnPropertyChanged(nameof(ChannelLabel));
        OnPropertyChanged(nameof(HasSequence));
        OnPropertyChanged(nameof(StepCount));
        OnPropertyChanged(nameof(LengthBeats));
    }

    private void ToggleStep(int index)
    {
        if (_sequence is null || index < 0 || index >= _sequence.Steps.Count) return;
        var step = _sequence.Steps[index];
        step.Active = !step.Active;
        Steps[index].Refresh();
        RaiseStepsChanged();
    }

    private void OnStepChanged(int index)
    {
        if (_sequence is null || index < 0 || index >= _sequence.Steps.Count) return;
        RaiseStepsChanged();
    }

    private void RaiseStepsChanged()
    {
        if (_suppressChangeEvent) return;
        StepsChanged?.Invoke();
    }

    private static void ResizeSequence(StepSequence sequence, int count)
    {
        while (sequence.Steps.Count < count)
            sequence.Steps.Add(new StepData());
        while (sequence.Steps.Count > count)
            sequence.Steps.RemoveAt(sequence.Steps.Count - 1);
        sequence.StepCount = count;
    }
}

public sealed class StepCellViewModel : ViewModelBase
{
    private readonly StepData _model;
    private readonly Action<int> _toggle;
    private readonly Action<int> _changed;
    private readonly int _index;

    public StepCellViewModel(int index, StepData model, string colorKey, Action<int> toggle, Action<int> changed)
    {
        _index = index;
        _model = model;
        ColorKey = colorKey;
        _toggle = toggle;
        _changed = changed;
        ToggleCommand = new RelayCommand(() => _toggle(_index));
    }

    public int Index => _index;
    public string ColorKey { get; }
    public bool IsActive => _model.Active;
    public float Velocity
    {
        get => _model.Velocity;
        set
        {
            if (Math.Abs(_model.Velocity - value) < 0.001f) return;
            _model.Velocity = Math.Clamp(value, 0f, 1f);
            OnPropertyChanged();
            OnPropertyChanged(nameof(VelocityHeight));
            _changed(_index);
        }
    }

    public float Pan
    {
        get => _model.Pan;
        set
        {
            if (Math.Abs(_model.Pan - value) < 0.001f) return;
            _model.Pan = Math.Clamp(value, -1f, 1f);
            OnPropertyChanged();
            _changed(_index);
        }
    }

    public int MicroTimingTicks
    {
        get => _model.MicroTimingTicks;
        set
        {
            if (_model.MicroTimingTicks == value) return;
            _model.MicroTimingTicks = Math.Clamp(value, -48, 48);
            OnPropertyChanged();
            _changed(_index);
        }
    }

    /// <summary>Velocity bar height as a fraction 0..1 for the step cell.</summary>
    public double VelocityHeight => Velocity;

    public RelayCommand ToggleCommand { get; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(Velocity));
        OnPropertyChanged(nameof(VelocityHeight));
        OnPropertyChanged(nameof(Pan));
        OnPropertyChanged(nameof(MicroTimingTicks));
    }
}
