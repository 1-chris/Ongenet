using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Default <see cref="IProjectFileService"/>. Bridges the in-memory <see cref="Project"/> (and transport
/// loop/tempo state) to the <see cref="ProjectFile"/> format, and tracks the open path + dirty state by
/// listening for the change events the rest of the app already publishes.
/// </summary>
public sealed class ProjectFileService : IProjectFileService
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;
    private readonly ISelectionService _selection;

    private bool _suppressDirty;

    public ProjectFileService(IProjectService project, ITransportService transport,
        IInstrumentRegistry instruments, IEffectRegistry effects, ISelectionService selection,
        IEventAggregator events)
    {
        _project = project;
        _transport = transport;
        _instruments = instruments;
        _effects = effects;
        _selection = selection;

        // Anything that mutates the project marks it dirty.
        events.Subscribe<TracksChangedEvent>(_ => MarkDirty());
        events.Subscribe<TrackChangedEvent>(_ => MarkDirty());
        events.Subscribe<ClipChangedEvent>(_ => MarkDirty());
        events.Subscribe<ClipAddedEvent>(_ => MarkDirty());
        events.Subscribe<ClipNotesChangedEvent>(_ => MarkDirty());
        events.Subscribe<AutomationChangedEvent>(_ => MarkDirty());
        events.Subscribe<ArrangementLengthChangedEvent>(_ => MarkDirty());
        _transport.TempoChanged += _ => MarkDirty();
        _transport.StartBeatChanged += MarkDirty;
        _transport.LoopChanged += MarkDirty;
    }

    public string? CurrentPath { get; private set; }
    public bool IsDirty { get; private set; }
    public bool IsBusy { get; private set; }
    public string BusyStatus { get; private set; } = "";
    public bool OpenedFromNewerVersion { get; private set; }

    public string DisplayName =>
        CurrentPath is { } p ? Path.GetFileNameWithoutExtension(p) : "Untitled";

    public event Action? Changed;

    public async Task SaveAsync(string path)
    {
        var project = _project.Current;
        var appVersion = AppVersion();
        var loopStart = _transport.LoopStart;
        var loopEnd = _transport.LoopEnd;
        var startBeat = _transport.StartBeat;

        // Write to a temp file in the same folder, then atomically swap it in. An interrupted save
        // (crash/close mid-write) leaves only the temp file — never a truncated, unreadable .ongen.
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        SetBusy("Saving…");
        try
        {
            await Task.Run(() =>
            {
                using (var fs = File.Create(temp))
                    ProjectFile.Save(project, fs, appVersion, loopStart, loopEnd, startBeat);
                File.Move(temp, path, overwrite: true);
            });
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
        finally
        {
            ClearBusy();
        }

        CurrentPath = path;
        OpenedFromNewerVersion = false;
        SetDirty(false);
        Changed?.Invoke();
    }

    public async Task<ProjectFile.LoadResult> LoadAsync(string path)
    {
        // Parse off the UI thread; the continuation resumes on the caller's (UI) context to swap it in.
        SetBusy("Loading…");
        ProjectFile.LoadResult result;
        try
        {
            result = await Task.Run(() =>
            {
                using var fs = File.OpenRead(path);
                return ProjectFile.Load(fs, _instruments, _effects);
            });
        }
        finally
        {
            ClearBusy();
        }

        _suppressDirty = true;
        try
        {
            _transport.Stop();
            _selection.SelectTrack(null); // drop any selection pointing at the old project
            _project.SetCurrentProject(result.Project);
            _transport.Tempo = result.Project.Tempo;
            _transport.StartBeat = result.StartBeat;
            _transport.LoopStart = result.LoopStart;
            _transport.LoopEnd = result.LoopEnd;
        }
        finally
        {
            _suppressDirty = false;
        }

        CurrentPath = path;
        OpenedFromNewerVersion = result.FromNewerVersion;
        SetDirty(false);
        Changed?.Invoke();
        return result;
    }

    public void NewProject()
    {
        _suppressDirty = true;
        try
        {
            _transport.Stop();
            _transport.LoopStart = 0;
            _transport.LoopEnd = 0;
            _selection.SelectTrack(null);
            _project.NewProject();
        }
        finally
        {
            _suppressDirty = false;
        }

        CurrentPath = null;
        OpenedFromNewerVersion = false;
        SetDirty(false);
        Changed?.Invoke();
    }

    private void MarkDirty()
    {
        if (_suppressDirty || IsDirty) return;
        IsDirty = true;
        Changed?.Invoke();
    }

    private void SetDirty(bool value) => IsDirty = value;

    private void SetBusy(string status)
    {
        IsBusy = true;
        BusyStatus = status;
        Changed?.Invoke();
    }

    private void ClearBusy()
    {
        IsBusy = false;
        BusyStatus = "";
        Changed?.Invoke();
    }

    private static string AppVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(ProjectFileService).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }
}
