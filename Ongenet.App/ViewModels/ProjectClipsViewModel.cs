using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Ongenet.App.Localization;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>One unique clip in the Project Clips panel: a representative clip plus every identical
/// instance of it across the project. Dragging carries the representative's id; the context-menu
/// actions (rename / delete all / categories) apply to every instance.</summary>
public sealed class ProjectClipItemViewModel : ViewModelBase
{
    private readonly ProjectClipsViewModel _owner;

    public ProjectClipItemViewModel(ProjectClipsViewModel owner, Clip representative, IReadOnlyList<Clip> instances,
        Track primaryTrack, int trackOrder, string contentKey)
    {
        _owner = owner;
        Representative = representative;
        Instances = instances;
        PrimaryTrack = primaryTrack;
        TrackOrder = trackOrder;
        ContentKey = contentKey;
        ColorKey = primaryTrack.ColorKey;
        TrackName = primaryTrack.Name;
        RenameAllCommand = new RelayCommand(() => _ = _owner.RenameAllAsync(this));
        DeleteAllCommand = new RelayCommand(() => _ = _owner.DeleteAllAsync(this));
        AddToCategoryCommand = new RelayCommand(() => _ = _owner.AddToCategoryAsync(this));
        RemoveFromCategoriesCommand = new RelayCommand(() => _owner.RemoveFromCategories(this));
    }

    public Clip Representative { get; }
    public IReadOnlyList<Clip> Instances { get; }
    public Track PrimaryTrack { get; }
    public int TrackOrder { get; }
    public string ContentKey { get; }
    public string ColorKey { get; }
    public string TrackName { get; }

    public string Name => Representative.Name;
    public bool IsAudio => Representative.IsAudio;
    public bool IsMidi => Representative.IsMidi;
    public string DragPayload => Representative.Id.ToString();

    public IReadOnlyList<MidiNote> Notes => Representative.Notes;
    public double ClipLengthBeats => Representative.LengthBeats;
    public Ongenet.Core.Audio.Files.AudioWaveform? Waveform => Representative.Waveform;

    public double WaveStartFraction
    {
        get
        {
            if (Representative.Waveform is not { } wf || wf.DurationSeconds <= 0) return 0.0;
            return Math.Clamp(Representative.SourceOffsetSeconds / wf.DurationSeconds, 0.0, 1.0);
        }
    }

    public double WaveEndFraction
    {
        get
        {
            if (Representative.Waveform is not { } wf || wf.DurationSeconds <= 0) return 1.0;
            var end = Representative.SourceLengthSeconds is { } len
                ? Representative.SourceOffsetSeconds + len
                : wf.DurationSeconds;
            return Math.Clamp(end / wf.DurationSeconds, 0.0, 1.0);
        }
    }

    public string Detail
    {
        get
        {
            var bars = Representative.LengthBeats / 4.0;
            var length = bars >= 1
                ? L(Math.Abs(bars - 1) < 1e-9 ? "ProjectClips_BarSingular" : "ProjectClips_BarPlural",
                    bars.ToString(bars % 1 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture))
                : L("ProjectClips_Beats",
                    Representative.LengthBeats.ToString("0.#", CultureInfo.InvariantCulture));
            var kind = Representative.IsAudio
                ? L("ProjectClips_Audio")
                : L("ProjectClips_MidiNotes", Representative.Notes.Count);
            var count = Instances.Count > 1 ? L("ProjectClips_InstanceCount", Instances.Count) : "";
            return L("ProjectClips_Detail", kind, length, count);
        }
    }

    public RelayCommand RenameAllCommand { get; }
    public RelayCommand DeleteAllCommand { get; }
    public RelayCommand AddToCategoryCommand { get; }
    public RelayCommand RemoveFromCategoriesCommand { get; }
}

/// <summary>A titled group in the Project Clips panel.</summary>
public sealed class ProjectClipGroupViewModel
{
    public ProjectClipGroupViewModel(string title, IEnumerable<ProjectClipItemViewModel> items)
    {
        Title = title;
        foreach (var i in items) Items.Add(i);
    }

    public string Title { get; }
    public ObservableCollection<ProjectClipItemViewModel> Items { get; } = new();
}

/// <summary>
/// The left sidebar's Project Clips tab: every unique MIDI and audio clip in the current project
/// (identical copies are collapsed into one entry with a ×N count). Supports user categories and
/// auto-sort by kind / colour / track.
/// </summary>
public sealed class ProjectClipsViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly TimelineViewModel _timeline;
    private readonly IProjectFileService _projectFile;
    private readonly DispatcherTimer _refreshDebounce;
    private ProjectClipsSortMode _sortMode;

    public ProjectClipsViewModel(IProjectService project, IEventAggregator events,
        TimelineViewModel timeline, Services.IHistoryService history, IProjectFileService projectFile)
    {
        _project = project;
        _timeline = timeline;
        _projectFile = projectFile;

        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Refresh(); };

        _project.ProjectChanged += ScheduleRefresh;
        events.Subscribe<TracksChangedEvent>(_ => ScheduleRefresh());
        events.Subscribe<ClipChangedEvent>(_ => ScheduleRefresh());
        events.Subscribe<ClipAddedEvent>(_ => ScheduleRefresh());
        events.Subscribe<ClipNotesChangedEvent>(_ => ScheduleRefresh());
        history.Changed += ScheduleRefresh;

        SortModes = new[]
        {
            new ProjectClipsSortModeOption(ProjectClipsSortMode.ByKind, Loc.Get("ProjectClips_Sort_ByKind", "By kind")),
            new ProjectClipsSortModeOption(ProjectClipsSortMode.ByColour, Loc.Get("ProjectClips_Sort_ByColour", "By colour")),
            new ProjectClipsSortModeOption(ProjectClipsSortMode.ByTrack, Loc.Get("ProjectClips_Sort_ByTrack", "By track")),
        };

        NewCategoryCommand = new RelayCommand(() => _ = NewCategoryAsync());
        Refresh();
    }

    public ObservableCollection<ProjectClipGroupViewModel> Groups { get; } = new();

    public bool IsEmpty => Groups.Count == 0;

    public IReadOnlyList<ProjectClipsSortModeOption> SortModes { get; }

    public ProjectClipsSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (!SetField(ref _sortMode, value)) return;
            _project.Current.ProjectClipsSortMode = value;
            _projectFile.MarkDirty();
            Refresh();
        }
    }

    public ProjectClipsSortModeOption? SelectedSortMode
    {
        get => SortModes.FirstOrDefault(s => s.Mode == SortMode);
        set
        {
            if (value is null) return;
            SortMode = value.Mode;
            OnPropertyChanged();
        }
    }

    public RelayCommand NewCategoryCommand { get; }

    private void ScheduleRefresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ScheduleRefresh);
            return;
        }

        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    private void Refresh()
    {
        var project = _project.Current;
        _sortMode = project.ProjectClipsSortMode;
        OnPropertyChanged(nameof(SortMode));
        OnPropertyChanged(nameof(SelectedSortMode));

        var trackIndex = 0;
        var pairs = new List<(Track Track, Clip Clip, int Order)>();
        foreach (var t in project.Tracks.Where(t => !t.IsBus))
        {
            var order = trackIndex++;
            foreach (var c in t.Clips)
                pairs.Add((t, c, order));
        }

        var unique = pairs
            .GroupBy(p => Signature(p.Clip))
            .Select(g =>
            {
                var first = g.First();
                var clips = g.Select(x => x.Clip).ToList();
                return new ProjectClipItemViewModel(this, first.Clip, clips, first.Track, first.Order, g.Key);
            })
            .ToList();

        Groups.Clear();

        // User categories first (only those with at least one matching clip).
        var byKey = unique.ToDictionary(i => i.ContentKey, StringComparer.Ordinal);
        foreach (var cat in project.ProjectClipCategories)
        {
            var items = cat.ClipKeys
                .Select(k => byKey.TryGetValue(k, out var item) ? item : null)
                .Where(i => i is not null)
                .Cast<ProjectClipItemViewModel>()
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (items.Count > 0)
                Groups.Add(new ProjectClipGroupViewModel(cat.Name, items));
        }

        switch (project.ProjectClipsSortMode)
        {
            case ProjectClipsSortMode.ByColour:
                foreach (var g in unique.GroupBy(i => i.ColorKey)
                             .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Groups.Add(new ProjectClipGroupViewModel(
                        ColourLabel(g.Key),
                        g.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)));
                }
                break;

            case ProjectClipsSortMode.ByTrack:
                foreach (var g in unique.GroupBy(i => (i.TrackOrder, i.TrackName))
                             .OrderBy(g => g.Key.TrackOrder))
                {
                    Groups.Add(new ProjectClipGroupViewModel(
                        g.Key.TrackName,
                        g.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)));
                }
                break;

            default:
                var midi = unique.Where(i => !i.IsAudio)
                    .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var audio = unique.Where(i => i.IsAudio)
                    .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (midi.Count > 0)
                    Groups.Add(new ProjectClipGroupViewModel(Loc.Get("ProjectClips_Group_Midi", "MIDI Clips"), midi));
                if (audio.Count > 0)
                    Groups.Add(new ProjectClipGroupViewModel(Loc.Get("ProjectClips_Group_Audio", "Audio Clips"), audio));
                break;
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private static string ColourLabel(string colorKey)
        => colorKey.StartsWith("Catppuccin", StringComparison.Ordinal)
            ? colorKey["Catppuccin".Length..]
            : colorKey;

    /// <summary>
    /// Two clips are "the same clip" when their name, length and content match — for MIDI the full
    /// note data, for audio the source (file or in-memory buffer) and its slice window.
    /// </summary>
    internal static string Signature(Clip c)
    {
        var sb = new StringBuilder();
        sb.Append(c.IsAudio ? "A|" : "M|").Append(c.Name).Append('|')
            .Append(c.LengthBeats.ToString("0.####", CultureInfo.InvariantCulture));

        if (c.IsAudio)
        {
            sb.Append('|').Append(c.AudioFilePath
                    ?? (c.Samples is { } s ? RuntimeHelpers.GetHashCode(s).ToString() : "none"))
                .Append('|').Append(c.SourceOffsetSeconds.ToString("0.###", CultureInfo.InvariantCulture))
                .Append('|').Append(c.SourceLengthSeconds?.ToString("0.###", CultureInfo.InvariantCulture) ?? "full")
                .Append('|').Append(c.StretchToTempo).Append('|').Append(c.PitchCorrected);
        }
        else
        {
            foreach (var n in c.Notes.OrderBy(n => n.StartBeat).ThenBy(n => n.Note))
            {
                sb.Append('|').Append(n.Note)
                    .Append(',').Append(n.StartBeat.ToString("0.####", CultureInfo.InvariantCulture))
                    .Append(',').Append(n.LengthBeats.ToString("0.####", CultureInfo.InvariantCulture))
                    .Append(',').Append(n.Velocity.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    internal async System.Threading.Tasks.Task RenameAllAsync(ProjectClipItemViewModel item)
    {
        var owner = OwnerWindow();
        if (owner is null) return;
        var label = item.Instances.Count > 1 ? $"New name for all {item.Instances.Count} instances:" : "Clip name:";
        var name = await Views.Windows.InputDialog.Prompt(owner, "Rename clip", label, item.Name, "Rename");
        if (name is null || name == item.Name) return;

        _timeline.RenameClips(item.Instances, name);
        ScheduleRefresh();
    }

    internal async System.Threading.Tasks.Task DeleteAllAsync(ProjectClipItemViewModel item)
    {
        var owner = OwnerWindow();
        if (owner is not null && item.Instances.Count > 1)
        {
            var ok = await Views.Windows.MessageDialog.Confirm(owner, "Delete all from project?",
                $"This removes all {item.Instances.Count} instances of \"{item.Name}\" from the project.",
                "Delete all", "Cancel");
            if (!ok) return;
        }

        _timeline.DeleteClips(item.Instances);
        ScheduleRefresh();
    }

    internal async System.Threading.Tasks.Task NewCategoryAsync()
    {
        var owner = OwnerWindow();
        if (owner is null) return;
        var name = await Views.Windows.InputDialog.Prompt(owner,
            Loc.Get("ProjectClips_NewCategory_Title", "New category"),
            Loc.Get("ProjectClips_NewCategory_Label", "Category name:"),
            "", Loc.Get("LibraryOrg_Create", "Create"));
        if (string.IsNullOrWhiteSpace(name)) return;
        _project.Current.ProjectClipCategories.Add(new ProjectClipCategory { Name = name.Trim() });
        MarkProjectDirty();
        Refresh();
    }

    internal async System.Threading.Tasks.Task AddToCategoryAsync(ProjectClipItemViewModel item)
    {
        var owner = OwnerWindow();
        if (owner is null) return;
        var cats = _project.Current.ProjectClipCategories;
        if (cats.Count == 0)
        {
            await NewCategoryAsync();
            cats = _project.Current.ProjectClipCategories;
            if (cats.Count == 0) return;
            cats[^1].ClipKeys.Add(item.ContentKey);
            MarkProjectDirty();
            Refresh();
            return;
        }

        var names = string.Join(", ", cats.Select(c => c.Name));
        var chosen = await Views.Windows.InputDialog.Prompt(owner,
            Loc.Get("ProjectClips_AddToCategory_Title", "Add to category"),
            Loc.Get("LibraryOrg_AddToCategory_Label", "Category name ({0}):", names),
            cats[0].Name, Loc.Get("LibraryOrg_Add", "Add"));
        if (string.IsNullOrWhiteSpace(chosen)) return;
        var cat = cats.FirstOrDefault(c =>
            string.Equals(c.Name, chosen.Trim(), StringComparison.OrdinalIgnoreCase));
        if (cat is null)
        {
            cat = new ProjectClipCategory { Name = chosen.Trim() };
            cats.Add(cat);
        }
        if (!cat.ClipKeys.Contains(item.ContentKey))
            cat.ClipKeys.Add(item.ContentKey);
        MarkProjectDirty();
        Refresh();
    }

    internal void RemoveFromCategories(ProjectClipItemViewModel item)
    {
        var changed = false;
        foreach (var cat in _project.Current.ProjectClipCategories)
        {
            if (cat.ClipKeys.Remove(item.ContentKey)) changed = true;
        }
        if (!changed) return;
        MarkProjectDirty();
        Refresh();
    }

    private void MarkProjectDirty() => _projectFile.MarkDirty();

    private static Avalonia.Controls.Window? OwnerWindow()
        => (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}

public sealed record ProjectClipsSortModeOption(ProjectClipsSortMode Mode, string Label)
{
    public override string ToString() => Label;
}
