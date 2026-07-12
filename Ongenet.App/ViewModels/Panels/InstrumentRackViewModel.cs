using System.Collections.ObjectModel;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Instrument / drum rack inspector — macros and pad grid.</summary>
public sealed class InstrumentRackViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private Track? _track;

    public InstrumentRackViewModel(IProjectService project)
    {
        _project = project;
        ToggleDrumRackCommand = new RelayCommand(ToggleDrumRack, () => _track is not null);
    }

    public ObservableCollection<RackMacroKnob> Macros { get; } = new();
    public ObservableCollection<DrumPadSlot> DrumPads { get; } = new();
    public RelayCommand ToggleDrumRackCommand { get; }

    public RackKind RackKind
    {
        get => _track?.Rack.Kind ?? RackKind.Standard;
        set
        {
            if (_track is null || _track.Rack.Kind == value) return;
            _track.Rack.Kind = value;
            if (value == RackKind.DrumPadGrid)
                _track.Rack.EnsureDefaultDrumPads();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDrumRack));
            Rebuild();
        }
    }

    public bool IsDrumRack => RackKind == RackKind.DrumPadGrid;

    public void BindTrack(Track? track)
    {
        _track = track;
        if (track is not null)
        {
            track.Rack.EnsureDefaultMacros();
            if (track.Rack.Kind == RackKind.DrumPadGrid)
                track.Rack.EnsureDefaultDrumPads();
        }
        Rebuild();
        ToggleDrumRackCommand.RaiseCanExecuteChanged();
    }

    private void ToggleDrumRack()
    {
        if (_track is null) return;
        RackKind = _track.Rack.Kind == RackKind.DrumPadGrid ? RackKind.Standard : RackKind.DrumPadGrid;
    }

    private void Rebuild()
    {
        Macros.Clear();
        DrumPads.Clear();
        if (_track is null) return;
        foreach (var m in _track.Rack.Macros) Macros.Add(m);
        foreach (var p in _track.Rack.DrumPads) DrumPads.Add(p);
        OnPropertyChanged(nameof(RackKind));
    }
}
