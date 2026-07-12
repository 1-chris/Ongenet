using System;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels;

/// <summary>Bulk MIDI transforms on a single clip (transpose, quantize, velocity scale).</summary>
public sealed class LogicalMidiEditViewModel : ViewModelBase
{
    private readonly Clip _clip;
    private readonly IHistoryService _history;
    private readonly IEventAggregator _events;
    private int _transposeSemitones;
    private double _quantizeGrid = 0.25;
    private double _velocityScale = 1.0;
    private int _humanizeTicks = 12;
    private float _noteChance = 1f;

    public LogicalMidiEditViewModel(Clip clip, IHistoryService history, IEventAggregator events)
    {
        _clip = clip;
        _history = history;
        _events = events;
        ApplyCommand = new RelayCommand(Apply);
    }

    public string ClipName => _clip.Name;
    public int NoteCount => _clip.Notes.Count;

    public int TransposeSemitones
    {
        get => _transposeSemitones;
        set => SetField(ref _transposeSemitones, value);
    }

    public double QuantizeGrid
    {
        get => _quantizeGrid;
        set => SetField(ref _quantizeGrid, Math.Max(0.0625, value));
    }

    public double VelocityScale
    {
        get => _velocityScale;
        set => SetField(ref _velocityScale, Math.Clamp(value, 0.1, 4.0));
    }

    public int HumanizeTicks
    {
        get => _humanizeTicks;
        set => SetField(ref _humanizeTicks, Math.Clamp(value, 0, 96));
    }

    public float NoteChance
    {
        get => _noteChance;
        set => SetField(ref _noteChance, Math.Clamp(value, 0f, 1f));
    }

    public RelayCommand ApplyCommand { get; }

    private void Apply()
    {
        _history.Capture("Logical MIDI edit");
        if (TransposeSemitones != 0)
            LogicalMidiEdit.TransposeClip(_clip, TransposeSemitones);
        if (Math.Abs(VelocityScale - 1.0) > 1e-6)
            LogicalMidiEdit.ScaleVelocity(_clip, VelocityScale);
        if (QuantizeGrid > 0)
            LogicalMidiEdit.QuantizeClip(_clip, QuantizeGrid);
        if (HumanizeTicks > 0)
            LogicalMidiEdit.HumanizeClip(_clip, HumanizeTicks);
        if (Math.Abs(NoteChance - 1f) > 1e-6f)
            LogicalMidiEdit.ApplyChance(_clip, NoteChance);
        _events.Publish(new ClipNotesChangedEvent(_clip));
    }
}
