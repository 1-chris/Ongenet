using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Services;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>FL-style multi-row step grid for the active pattern.</summary>
public sealed class PatternEditorViewModel : ViewModelBase
{
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private Pattern? _pattern;

    public static int[] StepCountOptions { get; } = [4, 8, 16, 32, 64];

    public PatternEditorViewModel(IEventAggregator events, IHistoryService history)
    {
        _events = events;
        _history = history;
        _events.Subscribe<PatternsChangedEvent>(_ => RefreshFromPattern());
    }

    public ObservableCollection<PatternGridRowViewModel> Rows { get; } = new();
    public ObservableCollection<int> StepHeaders { get; } = new();

    public string PatternTitle => _pattern?.Name ?? "Pattern";
    public bool HasPattern => _pattern is not null;

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
        get
        {
            if (_pattern is null) return 16;
            var seq = _pattern.StepSequences.FirstOrDefault();
            return seq?.StepCount > 0 ? seq.StepCount : StepCountOptions[2];
        }
        set
        {
            if (_pattern is null || value <= 0) return;
            var current = StepCount;
            if (current == value) return;
            _history.Capture("Pattern step count");
            foreach (var channel in _pattern.OrderedChannels)
            {
                var seq = _pattern.GetOrCreateSequence(channel, value);
                ResizeSequence(seq, value);
            }
            RebuildStepHeaders(value);
            RebuildRows();
            OnPropertyChanged();
            _events.Publish(new PatternsChangedEvent());
        }
    }

    public void Bind(Pattern? pattern)
    {
        _pattern = pattern;
        RebuildStepHeaders(StepCount);
        RebuildRows();
        OnPropertyChanged(nameof(PatternTitle));
        OnPropertyChanged(nameof(HasPattern));
        OnPropertyChanged(nameof(LengthBeats));
        OnPropertyChanged(nameof(StepCount));
    }

    public void RefreshFromPattern()
    {
        if (_pattern is null) return;
        RebuildStepHeaders(StepCount);
        RebuildRows();
        OnPropertyChanged(nameof(LengthBeats));
        OnPropertyChanged(nameof(StepCount));
    }

    private void RebuildStepHeaders(int count)
    {
        StepHeaders.Clear();
        for (var i = 0; i < count; i++)
            StepHeaders.Add(i);
    }

    private void RebuildRows()
    {
        Rows.Clear();
        if (_pattern is null) return;

        var stepCount = StepCount;
        foreach (var channel in _pattern.OrderedChannels)
        {
            var sequence = _pattern.GetOrCreateSequence(channel, stepCount);
            ResizeSequence(sequence, stepCount);
            Rows.Add(new PatternGridRowViewModel(channel, sequence, stepCount, OnStepChanged));
        }
    }

    private void OnStepChanged() => _events.Publish(new PatternsChangedEvent());

    private static void ResizeSequence(StepSequence sequence, int count)
    {
        while (sequence.Steps.Count < count)
            sequence.Steps.Add(new StepData());
        while (sequence.Steps.Count > count)
            sequence.Steps.RemoveAt(sequence.Steps.Count - 1);
        sequence.StepCount = count;
    }
}

public sealed class PatternGridRowViewModel : ViewModelBase
{
    private static readonly string[] StepColors =
    [
        "CatppuccinMauve", "CatppuccinBlue", "CatppuccinGreen", "CatppuccinPeach",
        "CatppuccinPink", "CatppuccinTeal", "CatppuccinYellow", "CatppuccinRed"
    ];

    private readonly StepSequence _sequence;
    private readonly Action _changed;

    public PatternGridRowViewModel(PatternChannel channel, StepSequence sequence, int stepCount, Action changed)
    {
        Channel = channel;
        _sequence = sequence;
        _changed = changed;
        Label = channel.Name;
        for (var i = 0; i < stepCount; i++)
        {
            var index = i;
            var step = _sequence.Steps[i];
            Steps.Add(new StepCellViewModel(index, step, StepColors[i % StepColors.Length], ToggleStep, OnVelocityChanged));
        }
    }

    private void OnVelocityChanged(int index) => _changed();

    public PatternChannel Channel { get; }
    public string Label { get; }
    public ObservableCollection<StepCellViewModel> Steps { get; } = new();

    private void ToggleStep(int index)
    {
        if (index < 0 || index >= _sequence.Steps.Count) return;
        var step = _sequence.Steps[index];
        step.Active = !step.Active;
        Steps[index].Refresh();
        _changed();
    }
}
