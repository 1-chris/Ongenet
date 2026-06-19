using System;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Owns the currently open <see cref="Project"/>. The single source of truth for
/// session content across the application.
/// </summary>
public interface IProjectService
{
    /// <summary>The project currently open.</summary>
    Project Current { get; }

    /// <summary>Raised when <see cref="Current"/> is replaced (load/new).</summary>
    event Action? ProjectChanged;

    /// <summary>Replaces <see cref="Current"/> with a fresh blank project (one instrument track).</summary>
    void NewProject();

    /// <summary>Replaces <see cref="Current"/> with a loaded project and raises <see cref="ProjectChanged"/>.</summary>
    void SetCurrentProject(Project project);
}
