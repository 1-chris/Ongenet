using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.MidiFx;
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
    private readonly IMidiEffectRegistry _midiEffects;
    private readonly ISelectionService _selection;

    private bool _suppressDirty;
    private string? _displayNameOverride;

    public ProjectFileService(IProjectService project, ITransportService transport,
        IInstrumentRegistry instruments, IEffectRegistry effects, IMidiEffectRegistry midiEffects,
        ISelectionService selection,
        IEventAggregator events)
    {
        _project = project;
        _transport = transport;
        _instruments = instruments;
        _effects = effects;
        _midiEffects = midiEffects;
        _selection = selection;

        // Anything that mutates the project marks it dirty.
        events.Subscribe<TracksChangedEvent>(_ => MarkDirty());
        events.Subscribe<TrackChangedEvent>(_ => MarkDirty());
        events.Subscribe<ClipChangedEvent>(_ => MarkDirty());
        events.Subscribe<ClipAddedEvent>(_ => MarkDirty());
        events.Subscribe<ClipNotesChangedEvent>(_ => MarkDirty());
        events.Subscribe<AutomationChangedEvent>(_ => MarkDirty());
        events.Subscribe<ArrangementLengthChangedEvent>(_ => MarkDirty());
        events.Subscribe<SessionClipsChangedEvent>(_ => MarkDirty());
        events.Subscribe<SessionClipChangedEvent>(_ => MarkDirty());
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
        CurrentPath is { } p
            ? Path.GetFileNameWithoutExtension(p)
            : (_displayNameOverride ?? "Untitled");

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
        _displayNameOverride = null;
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
                return ProjectFile.Load(fs, _instruments, _effects, _midiEffects);
            });
        }
        finally
        {
            ClearBusy();
        }

        ApplyLoadedProject(result);
        CurrentPath = path;
        _displayNameOverride = null;
        OpenedFromNewerVersion = result.FromNewerVersion;
        SetDirty(false);
        Changed?.Invoke();
        return result;
    }

    public async Task SaveAsync(Stream stream, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var project = _project.Current;
        var appVersion = AppVersion();
        var loopStart = _transport.LoopStart;
        var loopEnd = _transport.LoopEnd;
        var startBeat = _transport.StartBeat;

        SetBusy("Saving…");
        try
        {
            // Buffer on a worker so ProjectFile.Save can seek; then copy to the caller stream
            // (browser StorageProvider streams are often write-only / non-seekable).
            await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                ProjectFile.Save(project, ms, appVersion, loopStart, loopEnd, startBeat);
                ms.Position = 0;
                ms.CopyTo(stream);
            });
            await stream.FlushAsync();
        }
        finally
        {
            ClearBusy();
        }

        // No durable path in the sandbox — keep CurrentPath null so the next Save prompts again.
        CurrentPath = null;
        _displayNameOverride = SanitizeDisplayName(displayName);
        OpenedFromNewerVersion = false;
        SetDirty(false);
        Changed?.Invoke();
    }

    public async Task<ProjectFile.LoadResult> LoadAsync(Stream stream, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        SetBusy("Loading…");
        ProjectFile.LoadResult result;
        try
        {
            // Copy first so Load can seek freely even if the upload stream can't.
            var bytes = await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            });
            result = await Task.Run(() =>
            {
                using var ms = new MemoryStream(bytes, writable: false);
                return ProjectFile.Load(ms, _instruments, _effects, _midiEffects);
            });
        }
        finally
        {
            ClearBusy();
        }

        ApplyLoadedProject(result);
        CurrentPath = null;
        _displayNameOverride = SanitizeDisplayName(displayName);
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
        _displayNameOverride = null;
        OpenedFromNewerVersion = false;
        SetDirty(false);
        Changed?.Invoke();
    }

    public void LoadProject(Models.Audio.Project project)
    {
        _suppressDirty = true;
        try
        {
            _transport.Stop();
            _transport.LoopStart = 0;
            _transport.LoopEnd = 0;
            _transport.StartBeat = 0;
            _selection.SelectTrack(null);
            _project.SetCurrentProject(project);
            _transport.Tempo = project.Tempo;
        }
        finally
        {
            _suppressDirty = false;
        }

        CurrentPath = null;
        _displayNameOverride = null;
        OpenedFromNewerVersion = false;
        SetDirty(false);
        Changed?.Invoke();
    }

    private void ApplyLoadedProject(ProjectFile.LoadResult result)
    {
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
    }

    private static string? SanitizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        return Path.GetFileNameWithoutExtension(displayName.Trim());
    }

    private void SetDirtyFlag()
    {
        if (_suppressDirty || IsDirty) return;
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void MarkDirty() => SetDirtyFlag();


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
