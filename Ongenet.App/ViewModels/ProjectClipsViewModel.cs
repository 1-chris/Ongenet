using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>One unique clip in the Project Clips panel: a representative clip plus every identical
/// instance of it across the project. Dragging carries the representative's id; the context-menu
/// actions (rename / delete all) apply to every instance.</summary>
public sealed class ProjectClipItemViewModel : ViewModelBase
{
    private readonly ProjectClipsViewModel _owner;

    public ProjectClipItemViewModel(ProjectClipsViewModel owner, Clip representative, IReadOnlyList<Clip> instances)
    {
        _owner = owner;
        Representative = representative;
        Instances = instances;
        RenameAllCommand = new RelayCommand(() => _ = _owner.RenameAllAsync(this));
        DeleteAllCommand = new RelayCommand(() => _ = _owner.DeleteAllAsync(this));
    }

    /// <summary>The clip whose id rides in the drag payload (any instance would do — they're identical).</summary>
    public Clip Representative { get; }

    /// <summary>Every clip in the project with this exact content.</summary>
    public IReadOnlyList<Clip> Instances { get; }

    public string Name => Representative.Name;
    public bool IsAudio => Representative.IsAudio;
    public bool IsMidi => Representative.IsMidi;
    public string DragPayload => Representative.Id.ToString();

    // Preview bindings — the same data the timeline's clip miniatures read.
    public IReadOnlyList<MidiNote> Notes => Representative.Notes;
    public double ClipLengthBeats => Representative.LengthBeats;
    public Ongenet.Core.Audio.Files.AudioWaveform? Waveform => Representative.Waveform;

    /// <summary>Fraction of the audio source where this clip's window begins (sliced clips).</summary>
    public double WaveStartFraction
    {
        get
        {
            if (Representative.Waveform is not { } wf || wf.DurationSeconds <= 0) return 0.0;
            return Math.Clamp(Representative.SourceOffsetSeconds / wf.DurationSeconds, 0.0, 1.0);
        }
    }

    /// <summary>Fraction of the audio source where this clip's window ends (1 = whole source).</summary>
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
}

/// <summary>A titled group ("MIDI Clips" / "Audio Clips") in the Project Clips panel.</summary>
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
/// (identical copies are collapsed into one entry with a ×N count). Entries drag onto the timeline
/// — MIDI clips onto instrument tracks, audio clips onto audio tracks — and their context menu can
/// rename or delete every instance at once. Works for any project, including the built-in ones.
/// </summary>
public sealed class ProjectClipsViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly TimelineViewModel _timeline;
    private readonly DispatcherTimer _refreshDebounce;

    public ProjectClipsViewModel(IProjectService project, IEventAggregator events,
        TimelineViewModel timeline, Services.IHistoryService history)
    {
        _project = project;
        _timeline = timeline;

        // Coalesce refresh triggers: edits often arrive in bursts (multi-delete, project load).
        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Refresh(); };

        _project.ProjectChanged += ScheduleRefresh;
        events.Subscribe<TracksChangedEvent>(_ => ScheduleRefresh());
        events.Subscribe<ClipChangedEvent>(_ => ScheduleRefresh());
        events.Subscribe<ClipAddedEvent>(_ => ScheduleRefresh());
        events.Subscribe<ClipNotesChangedEvent>(_ => ScheduleRefresh());
        // Timeline clip deletes don't publish a dedicated event, but they capture history first —
        // the posted handler runs after the mutation, so the panel stays in sync.
        history.Changed += ScheduleRefresh;

        Refresh();
    }

    public ObservableCollection<ProjectClipGroupViewModel> Groups { get; } = new();

    public bool IsEmpty => Groups.Count == 0;

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
        var unique = _project.Current.Tracks
            .Where(t => !t.IsBus)
            .SelectMany(t => t.Clips)
            .GroupBy(Signature)
            .Select(g => new ProjectClipItemViewModel(this, g.First(), g.ToList()))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Groups.Clear();
        var midi = unique.Where(i => !i.IsAudio).ToList();
        var audio = unique.Where(i => i.IsAudio).ToList();
        if (midi.Count > 0) Groups.Add(new ProjectClipGroupViewModel("MIDI Clips", midi));
        if (audio.Count > 0) Groups.Add(new ProjectClipGroupViewModel("Audio Clips", audio));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Two clips are "the same clip" when their name, length and content match — for MIDI the full
    /// note data, for audio the source (file or in-memory buffer) and its slice window. Copies made
    /// by duplicating/dragging collapse into one entry; an edited copy becomes its own entry.
    /// </summary>
    private static string Signature(Clip c)
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

    private static Avalonia.Controls.Window? OwnerWindow()
        => (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
