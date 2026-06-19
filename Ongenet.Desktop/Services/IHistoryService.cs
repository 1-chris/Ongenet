using System;

namespace Ongenet.Desktop.Services
{
    /// <summary>
    /// Undo/redo history. <see cref="Capture"/> is called just BEFORE a user action mutates the project;
    /// it snapshots the current project + transport state so the action can be reverted. Restoring a
    /// snapshot swaps the project back in via <c>IProjectService.SetCurrentProject</c>, which rebuilds the
    /// whole UI + audio engine (the same path project load uses).
    /// </summary>
    public interface IHistoryService
    {
        bool CanUndo { get; }
        bool CanRedo { get; }

        /// <summary>Raised after the stacks change (so buttons can re-evaluate their enabled state).</summary>
        event Action? Changed;

        /// <summary>Snapshots the current state under <paramref name="label"/>, before a mutation. No-op while restoring.</summary>
        void Capture(string label);

        void Undo();
        void Redo();

        /// <summary>Drops all history (e.g. on New / Open).</summary>
        void Clear();
    }
}
