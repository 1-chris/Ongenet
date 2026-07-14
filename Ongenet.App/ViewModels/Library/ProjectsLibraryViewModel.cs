using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Ongenet.App.Services;
using Ongenet.App.Views.Windows;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Music;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Library;

/// <summary>
/// The library's Projects tab: the built-in demo projects (code-generated factory songs) plus the
/// recently opened/saved .ongen files. Double-clicking an entry opens it (after the usual
/// unsaved-changes confirmation). Recents are tracked here — whenever the project file service
/// reports a new current path it goes to the top of the persisted list.
/// </summary>
public sealed class ProjectsLibraryViewModel : LibraryListViewModel
{
    private const int MaxRecents = 12;

    private readonly IProjectFileService _projectFile;
    private readonly IAppSettingsService _settings;
    private readonly IInstrumentRegistry _instruments;
    private readonly IHistoryService _history;
    private readonly ILogger? _logger;

    private string? _lastRecordedPath;

    public ProjectsLibraryViewModel(IProjectFileService projectFile, IAppSettingsService settings,
        IInstrumentRegistry instruments, IHistoryService history, ILibraryOrganizationService org,
        ILoggerFactory? loggerFactory = null)
    {
        _projectFile = projectFile;
        _settings = settings;
        _instruments = instruments;
        _history = history;
        _logger = loggerFactory?.CreateLogger("Projects");
        EmptyHint = "Built-in and recently opened projects appear here.";
        AttachOrganization(org, LibraryItemKeys.Project, LibraryItemKeys.Folder);

        _lastRecordedPath = projectFile.CurrentPath;
        _projectFile.Changed += () => Dispatcher.UIThread.Post(OnProjectFileChanged);
        Refresh();
    }

    private void OnProjectFileChanged()
    {
        var path = _projectFile.CurrentPath;
        if (path is not null && !string.Equals(path, _lastRecordedPath, StringComparison.OrdinalIgnoreCase))
            AddRecent(path);
        _lastRecordedPath = path;
    }

    private void AddRecent(string path)
    {
        var recents = _settings.Current.RecentProjects;
        recents.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        recents.Insert(0, path);
        if (recents.Count > MaxRecents) recents.RemoveRange(MaxRecents, recents.Count - MaxRecents);
        _settings.CaptureAndSave();
        Refresh();
    }

    private void Refresh()
    {
        var roots = new List<LibraryNode>
        {
            Folder("Built-in Projects", BuiltInProjects.All.Select(info => new LibraryNode
            {
                Title = info.Name,
                Subtitle = info.Description,
                Icon = "🎵",
                Activate = () => _ = OpenBuiltInAsync(info)
            }))
        };

        var recents = _settings.Current.RecentProjects.Where(File.Exists).ToList();
        if (recents.Count > 0)
        {
            roots.Add(Folder("Recent Projects", recents.Select(path => new LibraryNode
            {
                Title = Path.GetFileNameWithoutExtension(path),
                Subtitle = Path.GetDirectoryName(path) ?? "",
                Icon = "🕘",
                ItemKey = LibraryItemKeys.ProjectKey(path),
                Activate = () => _ = OpenRecentAsync(path)
            }), itemKey: LibraryItemKeys.NamedFolderKey("projects", "Recent")));
        }

        SetRoots(roots);
    }

    private async System.Threading.Tasks.Task OpenBuiltInAsync(BuiltInProjectInfo info)
    {
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            _projectFile.LoadProject(info.Create(_instruments));
            _history.Clear();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to build the '{Name}' built-in project.", info.Name);
        }
    }

    private async System.Threading.Tasks.Task OpenRecentAsync(string path)
    {
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            await _projectFile.LoadAsync(path);
            _history.Clear();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open recent project '{Path}'.", path);
            // Drop the entry so a deleted/corrupt file doesn't keep failing on every double-click.
            _settings.Current.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _settings.CaptureAndSave();
            Refresh();
        }
    }

    private async System.Threading.Tasks.Task<bool> ConfirmDiscardAsync()
    {
        if (!_projectFile.IsDirty) return true;
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return true; // single-view host: no dialog surface, proceed
        return await MessageDialog.Confirm(owner, "Discard changes?",
            "You have unsaved changes that will be lost. Continue?", "Discard", "Cancel");
    }
}
