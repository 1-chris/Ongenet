using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Desktop.Services;

/// <summary>One asset (sample or sound font) discovered on disk.</summary>
public sealed record LibraryItem(string Name, string FullPath);

/// <summary>A named group of library items (one per configured scan folder). <see cref="Root"/> is the
/// scanned folder's full path, so callers can render items relative to it (e.g. as a folder tree).</summary>
public sealed record LibraryGroup(string Name, string Root, IReadOnlyList<LibraryItem> Items);

public interface ILibraryScanService
{
    IReadOnlyList<LibraryGroup> Samples { get; }
    IReadOnlyList<LibraryGroup> SoundFonts { get; }
    event Action? Changed;
    void Rescan();
}

/// <summary>
/// Scans the user-configured folders (from <see cref="AppSettings"/>) for samples (any audio file) and
/// sound fonts (<c>.sf2</c>/<c>.sfz</c>), grouped by scan folder. Rescans whenever the library settings
/// change. Enumeration runs off the UI thread; results are published back on it.
/// </summary>
public sealed class LibraryScanService : ILibraryScanService
{
    private static readonly string[] SoundFontExtensions = { ".sf2", ".sfz" };

    private readonly IAppSettingsService _settings;
    private readonly IAudioFileService _audioFiles;

    public LibraryScanService(IAppSettingsService settings, IAudioFileService audioFiles)
    {
        _settings = settings;
        _audioFiles = audioFiles;
        _settings.LibraryChanged += Rescan;
        Rescan();
    }

    public IReadOnlyList<LibraryGroup> Samples { get; private set; } = Array.Empty<LibraryGroup>();
    public IReadOnlyList<LibraryGroup> SoundFonts { get; private set; } = Array.Empty<LibraryGroup>();

    public event Action? Changed;

    public void Rescan()
    {
        var samplePaths = _settings.Current.SampleScanPaths.ToList();
        var sfPaths = _settings.Current.SoundFontScanPaths.ToList();

        Task.Run(() =>
        {
            var samples = samplePaths.Select(p => ScanFolder(p, IsSample)).Where(g => g.Items.Count > 0).ToList();
            var soundFonts = sfPaths.Select(p => ScanFolder(p, IsSoundFont)).Where(g => g.Items.Count > 0).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                Samples = samples;
                SoundFonts = soundFonts;
                Changed?.Invoke();
            });
        });
    }

    private bool IsSample(string path) => _audioFiles.IsAudioFile(path);

    private static bool IsSoundFont(string path)
        => SoundFontExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static LibraryGroup ScanFolder(string root, Func<string, bool> accept)
    {
        var items = new List<LibraryItem>();
        try
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (accept(file)) items.Add(new LibraryItem(Path.GetFileName(file), file));
                    if (items.Count >= 5000) break; // safety cap for huge trees
                }
            }
        }
        catch
        {
            // Unreadable folder — skip it.
        }

        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : root;
        return new LibraryGroup(name, root, items);
    }
}
