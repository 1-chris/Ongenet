using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.Panels
{
    /// <summary>Editor for project drum maps (note → label / velocity scale).</summary>
    public class DrumMapEditorViewModel : ViewModelBase
    {
        private readonly IHistoryService _history;
        private Project? _project;
        private DrumMap? _map;

        public DrumMapEditorViewModel(IHistoryService history)
        {
            _history = history;
            AddEntryCommand = new RelayCommand(AddEntry, () => _map is not null);
            AddMapCommand = new RelayCommand(AddMap, () => _project is not null);
        }

        public RelayCommand AddEntryCommand { get; }
        public RelayCommand AddMapCommand { get; }

        public ObservableCollection<DrumMapEntryViewModel> Entries { get; } = new();
        public ObservableCollection<DrumMap> Maps { get; } = new();

        public DrumMap? SelectedMap
        {
            get => _map;
            set
            {
                if (ReferenceEquals(_map, value)) return;
                _map = value;
                RebuildEntries();
                OnPropertyChanged();
            }
        }

        public string MapName
        {
            get => _map?.Name ?? string.Empty;
            set
            {
                if (_map is null || _map.Name == value) return;
                _history.Capture("Rename drum map");
                _map.Name = value;
                OnPropertyChanged();
            }
        }

        public void LoadProject(Project project)
        {
            _project = project;
            Maps.Clear();
            foreach (var dm in project.DrumMaps)
                Maps.Add(dm);
            if (Maps.Count == 0)
            {
                var map = new DrumMap();
                project.DrumMaps.Add(map);
                Maps.Add(map);
            }

            SelectedMap = Maps[0];
        }

        private void AddMap()
        {
            if (_project is null) return;
            _history.Capture("Add drum map");
            var map = new DrumMap { Name = $"Drum Map {_project.DrumMaps.Count + 1}" };
            _project.DrumMaps.Add(map);
            Maps.Add(map);
            SelectedMap = map;
            AddMapCommand.RaiseCanExecuteChanged();
        }

        private void AddEntry()
        {
            if (_map is null) return;
            _history.Capture("Add drum map entry");
            var note = _map.Entries.Count > 0 ? _map.Entries.Max(e => e.Note) + 1 : 36;
            _map.Entries.Add(new DrumMapEntry { Note = note, Label = $"Pad {note}" });
            RebuildEntries();
        }

        private void RebuildEntries()
        {
            Entries.Clear();
            if (_map is null) return;
            foreach (var entry in _map.Entries.OrderBy(e => e.Note))
                Entries.Add(new DrumMapEntryViewModel(entry, _history, RemoveEntry));
            OnPropertyChanged(nameof(MapName));
            AddEntryCommand.RaiseCanExecuteChanged();
        }

        private void RemoveEntry(DrumMapEntryViewModel entryVm)
        {
            if (_map is null) return;
            _history.Capture("Remove drum map entry");
            _map.Entries.Remove(entryVm.Entry);
            RebuildEntries();
        }
    }

    public sealed class DrumMapEntryViewModel : ViewModelBase
    {
        private readonly IHistoryService _history;
        private readonly Action<DrumMapEntryViewModel> _remove;

        public DrumMapEntryViewModel(DrumMapEntry entry, IHistoryService history, Action<DrumMapEntryViewModel> remove)
        {
            Entry = entry;
            _history = history;
            _remove = remove;
            RemoveCommand = new RelayCommand(() => _remove(this));
        }

        public DrumMapEntry Entry { get; }
        public RelayCommand RemoveCommand { get; }

        public int Note
        {
            get => Entry.Note;
            set
            {
                if (Entry.Note == value) return;
                Entry.Note = value;
                OnPropertyChanged();
            }
        }

        public string Label
        {
            get => Entry.Label;
            set
            {
                if (Entry.Label == value) return;
                Entry.Label = value;
                OnPropertyChanged();
            }
        }

        public float VelocityScale
        {
            get => Entry.VelocityScale;
            set
            {
                if (Math.Abs(Entry.VelocityScale - value) < 1e-6f) return;
                Entry.VelocityScale = value;
                OnPropertyChanged();
            }
        }

        public string SampleClipIdText
        {
            get => Entry.SampleClipId?.ToString() ?? string.Empty;
            set
            {
                Guid? id = Guid.TryParse(value, out var g) ? g : null;
                if (Entry.SampleClipId == id) return;
                Entry.SampleClipId = id;
                OnPropertyChanged();
            }
        }
    }
}
