using System;
using System.Collections.Generic;

namespace Ongenet.App.Services
{
    /// <summary>One row in the history timeline (oldest first). <see cref="IsCurrent"/> marks the active state.</summary>
    public sealed record HistoryEntry(int Index, string Label, bool IsCurrent);

    /// <summary>
    /// Undo/redo history. <see cref="Core.Services.IHistoryCapture.Capture"/> is called just BEFORE a user action mutates the project;
    /// it snapshots the project + transport state so the action can be reverted. Restoring a snapshot swaps
    /// the project back in via <c>IProjectService.SetCurrentProject</c>, which rebuilds the whole UI + audio
    /// engine (the same path project load uses). The history is a single linear timeline with a cursor, so a
    /// history window can list every action and jump to any point (a bulk undo/redo).
    /// </summary>
    public interface IHistoryService : Core.Services.IHistoryCapture
    {
        bool CanUndo { get; }
        bool CanRedo { get; }

        /// <summary>The full timeline, oldest first, with the current state flagged.</summary>
        IReadOnlyList<HistoryEntry> Timeline { get; }

        /// <summary>Raised after the timeline or cursor changes (so UI can refresh).</summary>
        event Action? Changed;

        /// <summary>Raised after undo/redo/jump with note keys to re-select in the piano roll.</summary>
        event Action<IReadOnlyList<NoteSelectionKey>>? NoteSelectionRestored;

        void Undo();
        void Redo();

        /// <summary>Jumps directly to the state at <paramref name="index"/> in <see cref="Timeline"/> (bulk undo/redo).</summary>
        void JumpTo(int index);

        /// <summary>Drops all history (e.g. on New / Open).</summary>
        void Clear();
    }

    /// <summary>Identifies a MIDI note within a clip for selection restore.</summary>
    public readonly record struct NoteSelectionKey(double StartBeat, int Pitch);
}
