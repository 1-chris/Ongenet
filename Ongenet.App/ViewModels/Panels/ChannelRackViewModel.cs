using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Channel rack — pattern channel rows for the active pattern.</summary>
public sealed class ChannelRackViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly PianoRollViewModel _pianoRoll;
    private Pattern? _selectedPattern;
    private bool _suppressStepSync;

    public ChannelRackViewModel(IProjectService project, StepSequencerViewModel stepSequencer,
        PianoRollViewModel pianoRoll)
    {
        _project = project;
        _pianoRoll = pianoRoll;
        StepSequencer = stepSequencer;
        StepSequencer.StepsChanged += OnStepsChanged;
        _project.ProjectChanged += Rebuild;
        Rebuild();
    }

    public StepSequencerViewModel StepSequencer { get; }

    public ObservableCollection<PatternChannelRowViewModel> Channels { get; } = new();

    public string PatternName => _selectedPattern?.Name ?? "No pattern";

    public int SelectedChannelIndex
    {
        get => _selectedChannelIndex;
        set
        {
            if (!SetField(ref _selectedChannelIndex, value)) return;
            SelectChannel(value);
        }
    }

    private int _selectedChannelIndex = -1;

    /// <summary>Selects a pattern for editing (e.g. from a double-clicked playlist block).</summary>
    public void SelectPattern(Pattern? pattern)
    {
        _selectedPattern = pattern ?? _project.Current.Patterns.FirstOrDefault();
        RebuildChannels();
    }

    private void Rebuild()
    {
        _selectedPattern ??= _project.Current.Patterns.FirstOrDefault();
        RebuildChannels();
    }

    private void RebuildChannels()
    {
        Channels.Clear();
        if (_selectedPattern is null)
        {
            StepSequencer.Bind(null, null);
            _pianoRoll.EndPatternSync();
            OnPropertyChanged(nameof(PatternName));
            SelectedChannelIndex = -1;
            return;
        }

        foreach (var ch in _selectedPattern.OrderedChannels)
            Channels.Add(new PatternChannelRowViewModel(ch, _selectedPattern, SendChannelToPianoRoll, SendChannelToStepSeq));

        OnPropertyChanged(nameof(PatternName));
        SelectedChannelIndex = Channels.Count > 0 ? 0 : -1;
    }

    private void SelectChannel(int index)
    {
        if (_selectedPattern is null || index < 0 || index >= Channels.Count)
        {
            StepSequencer.Bind(null, null);
            _pianoRoll.EndPatternSync();
            return;
        }

        var row = Channels[index];
        var seq = _selectedPattern.StepSequences.FirstOrDefault(s => s.PatternChannelId == row.Model.Id)
                  ?? CreateSequence(row.Model);
        StepSequencer.Bind(_selectedPattern, seq);
        _pianoRoll.BeginPatternSync(_selectedPattern, seq, () =>
        {
            _suppressStepSync = true;
            try { StepSequencer.RefreshFromSequence(); }
            finally { _suppressStepSync = false; }
        });
    }

    private void OnStepsChanged()
    {
        if (_suppressStepSync || _selectedPattern is null) return;
        _pianoRoll.SyncFromSteps();
    }

    private void SendChannelToPianoRoll(PatternChannelRowViewModel row)
    {
        var index = Channels.IndexOf(row);
        if (index >= 0) SelectedChannelIndex = index;
        _pianoRoll.SyncFromSteps();
    }

    private void SendChannelToStepSeq(PatternChannelRowViewModel row)
    {
        var index = Channels.IndexOf(row);
        if (index >= 0) SelectedChannelIndex = index;
        if (_selectedPattern is null) return;
        var seq = _selectedPattern.StepSequences.FirstOrDefault(s => s.PatternChannelId == row.Model.Id)
                  ?? CreateSequence(row.Model);
        _pianoRoll.SendToStepSequence(seq, _selectedPattern.LengthBeats);
        StepSequencer.RefreshFromSequence();
    }

    private StepSequence CreateSequence(PatternChannel channel)
    {
        var seq = new StepSequence { PatternChannelId = channel.Id, StepCount = 16 };
        for (var i = 0; i < 16; i++)
            seq.Steps.Add(new StepData());
        _selectedPattern!.StepSequences.Add(seq);
        return seq;
    }
}

public sealed class PatternChannelRowViewModel : ViewModelBase
{
    private readonly Action<PatternChannelRowViewModel> _sendToPianoRoll;
    private readonly Action<PatternChannelRowViewModel> _sendToStepSeq;

        public PatternChannelRowViewModel(PatternChannel model, Pattern pattern,
        Action<PatternChannelRowViewModel> sendToPianoRoll, Action<PatternChannelRowViewModel> sendToStepSeq)
    {
        Model = model;
        Pattern = pattern;
        _sendToPianoRoll = sendToPianoRoll;
        _sendToStepSeq = sendToStepSeq;
        SendToPianoRollCommand = new RelayCommand(() => _sendToPianoRoll(this));
        SendToStepSeqCommand = new RelayCommand(() => _sendToStepSeq(this));
    }

    public PatternChannel Model { get; }
    public Pattern Pattern { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            Model.Name = value;
            OnPropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => Model.Muted;
        set
        {
            if (Model.Muted == value) return;
            Model.Muted = value;
            OnPropertyChanged();
        }
    }

    public double Volume
    {
        get => Model.Volume;
        set
        {
            if (Math.Abs(Model.Volume - value) < 1e-9) return;
            Model.Volume = value;
            OnPropertyChanged();
        }
    }
    public ICommand SendToPianoRollCommand { get; }
    public ICommand SendToStepSeqCommand { get; }
}
