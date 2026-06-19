using System;
using System.Threading.Tasks;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Saves and loads the current project as a single <c>.ongen</c> file, and tracks the open file's path and
/// unsaved-changes ("dirty") state so the shell can show them and prompt before discarding edits.
/// </summary>
public interface IProjectFileService
{
    /// <summary>Path of the currently open file, or null for a never-saved project.</summary>
    string? CurrentPath { get; }

    /// <summary>"Untitled" or the open file's name (no extension), for the window title.</summary>
    string DisplayName { get; }

    /// <summary>True when there are unsaved changes.</summary>
    bool IsDirty { get; }

    /// <summary>True while a save or load is in progress (drives the busy indicator and blocks close).</summary>
    bool IsBusy { get; }

    /// <summary>Short status text for the busy indicator ("Saving…"/"Loading…"), or empty.</summary>
    string BusyStatus { get; }

    /// <summary>True when the open file came from a newer app version (saving may discard unknown data).</summary>
    bool OpenedFromNewerVersion { get; }

    /// <summary>Raised when <see cref="CurrentPath"/>, <see cref="DisplayName"/> or <see cref="IsDirty"/> change.</summary>
    event Action? Changed;

    /// <summary>Saves the current project to <paramref name="path"/> (the new current file).</summary>
    Task SaveAsync(string path);

    /// <summary>Loads a project from <paramref name="path"/>, swaps it in, and returns the load result (warnings).</summary>
    Task<ProjectFile.LoadResult> LoadAsync(string path);

    /// <summary>Replaces the project with a fresh blank one and clears the file/dirty state.</summary>
    void NewProject();
}
