using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Services;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

public sealed class ChordTrackViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly ISelectionService _selection;
    private readonly IHistoryService _history;

    public ChordTrackViewModel(IProjectService project, ISelectionService selection, IHistoryService history)
    {
        _project = project;
        _selection = selection;
        _history = history;
        AddRegionCommand = new RelayCommand(AddRegion);
        ApplyToSelectedClipCommand = new RelayCommand(ApplyToSelectedClip, () => _selection.SelectedClip is not null);
        _selection.SelectionChanged += () => ApplyToSelectedClipCommand.RaiseCanExecuteChanged();
        Rebuild();
    }

    public RelayCommand AddRegionCommand { get; }
    public RelayCommand ApplyToSelectedClipCommand { get; }

    public ObservableCollection<ChordRegionRow> Regions { get; } = new();

    public bool Enabled
    {
        get => _project.Current.ChordTrack.Enabled;
        set
        {
            if (_project.Current.ChordTrack.Enabled == value) return;
            _history.Capture("Chord track");
            _project.Current.ChordTrack.Enabled = value;
            OnPropertyChanged();
        }
    }

    private double _newStartBeat;

    public double NewStartBeat
    {
        get => _newStartBeat;
        set => SetField(ref _newStartBeat, value);
    }

    private double _newLengthBeats = 4;

    public double NewLengthBeats
    {
        get => _newLengthBeats;
        set => SetField(ref _newLengthBeats, value);
    }

    private string _newSymbol = "C";

    public string NewSymbol
    {
        get => _newSymbol;
        set => SetField(ref _newSymbol, value);
    }

    public void Rebuild()
    {
        Regions.Clear();
        foreach (var region in _project.Current.ChordTrack.Regions.OrderBy(r => r.StartBeat))
            Regions.Add(new ChordRegionRow(region, this));
        OnPropertyChanged(nameof(Enabled));
    }

    private void AddRegion()
    {
        _history.Capture("Add chord region");
        ChordTrackService.AddRegion(_project.Current.ChordTrack, NewStartBeat, NewLengthBeats, NewSymbol);
        Rebuild();
    }

    internal void RemoveRegion(ChordRegionRow row)
    {
        _history.Capture("Remove chord region");
        _project.Current.ChordTrack.Regions.Remove(row.Region);
        Rebuild();
    }

    private void ApplyToSelectedClip()
    {
        if (_selection.SelectedClip is not { } clip) return;
        _history.Capture("Apply chord track");
        ChordTrackService.ApplyToClip(clip, _project.Current.ChordTrack, clip.StartBeat);
    }
}

public sealed class ChordRegionRow : ViewModelBase
{
    public ChordRegionRow(ChordRegion region, ChordTrackViewModel owner)
    {
        Region = region;
        RemoveCommand = new RelayCommand(() => owner.RemoveRegion(this));
    }

    public ChordRegion Region { get; }
    public RelayCommand RemoveCommand { get; }

    public double StartBeat
    {
        get => Region.StartBeat;
        set { Region.StartBeat = value; OnPropertyChanged(); }
    }

    public double LengthBeats
    {
        get => Region.LengthBeats;
        set { Region.LengthBeats = value; OnPropertyChanged(); }
    }

    public string Symbol
    {
        get => Region.Symbol;
        set { Region.Symbol = value; OnPropertyChanged(); }
    }
}
