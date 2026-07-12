using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Services;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

public sealed class SectionPlaylistViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly SectionPlaylistService _player;
    private readonly IHistoryService _history;

    public SectionPlaylistViewModel(IProjectService project, SectionPlaylistService player, IHistoryService history)
    {
        _project = project;
        _player = player;
        _history = history;
        AddSectionCommand = new RelayCommand(AddSection, () => Markers.Count > 0);
        RemoveSectionCommand = new RelayCommand<SectionRow>(RemoveSection, row => row is not null);
        DuplicateSectionCommand = new RelayCommand<SectionRow>(DuplicateSection, row => row is not null);
        MoveUpCommand = new RelayCommand<SectionRow>(MoveUp, CanMoveUp);
        MoveDownCommand = new RelayCommand<SectionRow>(MoveDown, CanMoveDown);
        project.ProjectChanged += Rebuild;
        _player.PlaylistPositionChanged += OnPlaylistPositionChanged;
        _player.PlaylistPositionChanged += () => OnPropertyChanged(nameof(CurrentSectionDisplay));
        Rebuild();
    }

    public ObservableCollection<SectionRow> Sections { get; } = new();
    public IReadOnlyList<ArrangementMarker> Markers => _project.Current.Markers.OrderBy(m => m.Beat).ToList();
    public RelayCommand AddSectionCommand { get; }
    public RelayCommand<SectionRow> RemoveSectionCommand { get; }
    public RelayCommand<SectionRow> DuplicateSectionCommand { get; }
    public RelayCommand<SectionRow> MoveUpCommand { get; }
    public RelayCommand<SectionRow> MoveDownCommand { get; }

    public bool IsEnabled
    {
        get => _player.IsEnabled;
        set
        {
            _player.IsEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentSectionDisplay));
        }
    }

    public int CurrentIndex => _player.CurrentIndex;

    public string CurrentSectionDisplay
    {
        get
        {
            if (!IsEnabled || Sections.Count == 0) return L("Status_PlaylistOff");
            if (CurrentIndex < 0 || CurrentIndex >= Sections.Count) return L("Status_EmDash");
            var name = Sections[CurrentIndex].Marker?.Name ?? L("SectionPlaylist_Unknown");
            return L("SectionPlaylist_Playing", CurrentIndex + 1, Sections.Count, name);
        }
    }

    private void OnPlaylistPositionChanged()
    {
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(CurrentSectionDisplay));
        foreach (var row in Sections)
            row.NotifyPlaying(CurrentIndex);
    }

    private void AddSection()
    {
        var marker = Markers.FirstOrDefault();
        if (marker is null) return;
        _history.Capture("Add playlist section");
        _project.Current.ArrangementSections.Add(new ArrangementSection { MarkerId = marker.Id });
        _player.ResetIndex();
        Rebuild();
    }

    private void RemoveSection(SectionRow? row)
    {
        if (row is null) return;
        _history.Capture("Remove playlist section");
        _project.Current.ArrangementSections.Remove(row.Section);
        _player.ResetIndex();
        Rebuild();
    }

    private void DuplicateSection(SectionRow? row)
    {
        if (row is null) return;
        _history.Capture("Duplicate playlist section");
        var idx = _project.Current.ArrangementSections.IndexOf(row.Section);
        if (idx < 0) return;
        _project.Current.ArrangementSections.Insert(idx + 1,
            new ArrangementSection { MarkerId = row.Section.MarkerId });
        _player.ResetIndex();
        Rebuild();
    }

    private void MoveUp(SectionRow? row)
    {
        if (row is null || !CanMoveUp(row)) return;
        _history.Capture("Reorder playlist section");
        var list = _project.Current.ArrangementSections;
        var idx = list.IndexOf(row.Section);
        (list[idx - 1], list[idx]) = (list[idx], list[idx - 1]);
        _player.ResetIndex();
        Rebuild();
    }

    private void MoveDown(SectionRow? row)
    {
        if (row is null || !CanMoveDown(row)) return;
        _history.Capture("Reorder playlist section");
        var list = _project.Current.ArrangementSections;
        var idx = list.IndexOf(row.Section);
        (list[idx + 1], list[idx]) = (list[idx], list[idx + 1]);
        _player.ResetIndex();
        Rebuild();
    }

    private bool CanMoveUp(SectionRow? row)
        => row is not null && Sections.IndexOf(row) > 0;

    private bool CanMoveDown(SectionRow? row)
    {
        if (row is null) return false;
        var idx = Sections.IndexOf(row);
        return idx >= 0 && idx < Sections.Count - 1;
    }

    private void Rebuild()
    {
        Sections.Clear();
        var playIdx = CurrentIndex;
        var idx = 0;
        foreach (var section in _project.Current.ArrangementSections)
        {
            Sections.Add(new SectionRow(section, Markers, playIdx, idx, this));
            idx++;
        }
        OnPropertyChanged(nameof(Markers));
        OnPropertyChanged(nameof(CurrentSectionDisplay));
        AddSectionCommand.RaiseCanExecuteChanged();
    }

    internal void RemoveSectionAt(SectionRow row) => RemoveSection(row);
    internal void DuplicateSectionAt(SectionRow row) => DuplicateSection(row);
    internal void MoveUpAt(SectionRow row) => MoveUp(row);
    internal void MoveDownAt(SectionRow row) => MoveDown(row);
}

public sealed class SectionRow : ViewModelBase
{
    private readonly ArrangementSection _section;
    private readonly SectionPlaylistViewModel _owner;
    private int _playingIndex;
    private int _rowIndex;

    public SectionRow(ArrangementSection section, IReadOnlyList<ArrangementMarker> markers,
        int playingIndex, int rowIndex, SectionPlaylistViewModel owner)
    {
        _section = section;
        _owner = owner;
        Markers = markers;
        _playingIndex = playingIndex;
        _rowIndex = rowIndex;
        RemoveCommand = new RelayCommand(() => _owner.RemoveSectionAt(this));
        DuplicateCommand = new RelayCommand(() => _owner.DuplicateSectionAt(this));
        MoveUpCommand = new RelayCommand(() => _owner.MoveUpAt(this), () => _rowIndex > 0);
        MoveDownCommand = new RelayCommand(() => _owner.MoveDownAt(this),
            () => _rowIndex < _owner.Sections.Count - 1);
    }

    public ArrangementSection Section => _section;
    public IReadOnlyList<ArrangementMarker> Markers { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    public int RowIndex => _rowIndex + 1;

    public bool IsPlaying => _playingIndex == _rowIndex;

    public void NotifyPlaying(int playingIndex)
    {
        _playingIndex = playingIndex;
        OnPropertyChanged(nameof(IsPlaying));
    }

    public ArrangementMarker? Marker
    {
        get => Markers.FirstOrDefault(m => m.Id == _section.MarkerId);
        set
        {
            if (value is null) return;
            _section.MarkerId = value.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => Marker?.Name ?? "(select marker)";
}
