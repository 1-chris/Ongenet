using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Services;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

public sealed class ExpressionMapViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly IHistoryService _history;
    private VstExpressionMap? _selected;

    public ExpressionMapViewModel(IProjectService project, IHistoryService history)
    {
        _project = project;
        _history = history;
        AddMapCommand = new RelayCommand(AddMap);
        AddEntryCommand = new RelayCommand(AddEntry, () => Selected is not null);
        RemoveEntryCommand = new RelayCommand<ExpressionMapEntryRow>(RemoveEntry, r => r is not null);
        _project.ProjectChanged += Rebuild;
        Rebuild();
    }

    public ObservableCollection<VstExpressionMap> Maps { get; } = new();
    public ObservableCollection<ExpressionMapEntryRow> Entries { get; } = new();
    public RelayCommand AddMapCommand { get; }
    public RelayCommand AddEntryCommand { get; }
    public RelayCommand<ExpressionMapEntryRow> RemoveEntryCommand { get; }

    public VstExpressionMap? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            RebuildEntries();
            AddEntryCommand.RaiseCanExecuteChanged();
        }
    }

    private void Rebuild()
    {
        Maps.Clear();
        foreach (var map in _project.Current.ExpressionMaps)
            Maps.Add(map);
        if (Maps.Count == 0)
            AddMap();
        Selected ??= Maps.FirstOrDefault();
    }

    private void RebuildEntries()
    {
        Entries.Clear();
        if (Selected is null) return;
        foreach (var entry in Selected.Entries)
            Entries.Add(new ExpressionMapEntryRow(entry, this));
    }

    private void AddMap()
    {
        _history.Capture("Add expression map");
        var map = new VstExpressionMap { Name = $"Map {Maps.Count + 1}" };
        _project.Current.ExpressionMaps.Add(map);
        Maps.Add(map);
        Selected = map;
    }

    private void AddEntry()
    {
        if (Selected is null) return;
        _history.Capture("Add expression entry");
        var entry = new ExpressionMapEntry();
        Selected.Entries.Add(entry);
        Entries.Add(new ExpressionMapEntryRow(entry, this));
    }

    internal void RemoveEntry(ExpressionMapEntryRow row)
    {
        if (Selected is null) return;
        _history.Capture("Remove expression entry");
        Selected.Entries.Remove(row.Entry);
        Entries.Remove(row);
    }
}

public sealed class ExpressionMapEntryRow : ViewModelBase
{
    public ExpressionMapEntryRow(ExpressionMapEntry entry, ExpressionMapViewModel owner)
    {
        Entry = entry;
        RemoveCommand = new RelayCommand(() => owner.RemoveEntry(this));
    }

    public ExpressionMapEntry Entry { get; }
    public RelayCommand RemoveCommand { get; }

    public string Articulation
    {
        get => Entry.Articulation;
        set { Entry.Articulation = value; OnPropertyChanged(); }
    }

    public int KeyswitchNote
    {
        get => Entry.KeyswitchNote;
        set { Entry.KeyswitchNote = value; OnPropertyChanged(); }
    }
}
