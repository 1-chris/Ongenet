using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Periodic autosave and recovery scan for orphaned temp saves. Backups land in
/// <c>~/Library/Application Support/Ongenet/autosave</c> (macOS) or the platform equivalent.
/// </summary>
public sealed class ProjectAutosaveService : IDisposable
{
    private readonly IProjectFileService _files;
    private readonly Func<bool> _isEnabled;
    private readonly Func<TimeSpan> _interval;
    private Timer? _timer;
    private int _running;

    public ProjectAutosaveService(IProjectFileService files, Func<bool> isEnabled, Func<TimeSpan> interval)
    {
        _files = files;
        _isEnabled = isEnabled;
        _interval = interval;
        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public static string AutosaveRoot
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Ongenet", "autosave");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public void Dispose() => _timer?.Dispose();

    private async Task TickAsync()
    {
        if (!_isEnabled() || !_files.IsDirty || _files.IsBusy) return;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        try
        {
            var path = _files.CurrentPath;
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var name = path is { } p
                ? $"{Path.GetFileNameWithoutExtension(p)}.{stamp}.ongen"
                : $"Untitled.{stamp}.ongen";
            var dest = Path.Combine(AutosaveRoot, name);
            await _files.SaveAsync(dest).ConfigureAwait(false);
            PruneOldBackups(keep: 20);
        }
        catch
        {
            // Autosave must never crash the app.
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public static void PruneOldBackups(int keep)
    {
        try
        {
            var dir = AutosaveRoot;
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "*.ongen")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(keep)
                .ToList();
            foreach (var f in files)
            {
                try { f.Delete(); } catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>Find recoverable autosave backups and orphaned atomic temp files near a project path.</summary>
    public static IReadOnlyList<RecoveryCandidate> ScanForRecovery(string? projectPath)
    {
        var results = new List<RecoveryCandidate>();

        if (Directory.Exists(AutosaveRoot))
        {
            var prefix = projectPath is { } p ? Path.GetFileNameWithoutExtension(p) : "Untitled";
            foreach (var file in Directory.GetFiles(AutosaveRoot, $"{prefix}.*.ongen"))
            {
                var info = new FileInfo(file);
                results.Add(new RecoveryCandidate(file, info.LastWriteTimeUtc, IsAutosave: true));
            }
        }

        if (projectPath is { } path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            var baseName = Path.GetFileName(path);
            foreach (var temp in Directory.GetFiles(dir, $".{baseName}.*.tmp"))
            {
                var info = new FileInfo(temp);
                results.Add(new RecoveryCandidate(temp, info.LastWriteTimeUtc, IsAutosave: false));
            }
        }

        return results.OrderByDescending(r => r.TimestampUtc).ToList();
    }

    public readonly record struct RecoveryCandidate(string Path, DateTime TimestampUtc, bool IsAutosave);
}
