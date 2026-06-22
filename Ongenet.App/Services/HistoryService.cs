using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Services
{
    /// <summary>
    /// Default <see cref="IHistoryService"/>. Keeps a single linear timeline of committed state snapshots
    /// plus a cursor. Because <see cref="Capture"/> runs BEFORE a mutation (we have no post-mutation hook),
    /// the result of an action is snapshotted lazily — on the NEXT capture / undo / jump — and shown in the
    /// meantime as a provisional "current" tip. See the interface for the contract.
    /// </summary>
    public sealed class HistoryService : IHistoryService
    {
        private const int MaxDepth = 50;

        private readonly IProjectService _project;
        private readonly ITransportService _transport;
        private readonly ISelectionService _selection;
        private readonly IInstrumentRegistry _instruments;
        private readonly IEffectRegistry _effects;

        private readonly List<Entry> _states = new();
        private int _index;        // index in _states of the current committed state
        private string? _pending;  // label of an action whose result hasn't been committed yet
        private bool _restoring;

        public HistoryService(IProjectService project, ITransportService transport, ISelectionService selection,
            IInstrumentRegistry instruments, IEffectRegistry effects)
        {
            _project = project;
            _transport = transport;
            _selection = selection;
            _instruments = instruments;
            _effects = effects;
        }

        public bool CanUndo => _pending is not null || _index > 0;
        public bool CanRedo => _pending is null && _index < _states.Count - 1;
        public event Action? Changed;

        public IReadOnlyList<HistoryEntry> Timeline
        {
            get
            {
                var list = new List<HistoryEntry>(_states.Count + 1);
                for (var i = 0; i < _states.Count; i++)
                    list.Add(new HistoryEntry(i, _states[i].Label, _pending is null && i == _index));
                // An action whose result isn't committed yet shows as the provisional current tip.
                if (_pending is not null)
                    list.Add(new HistoryEntry(_states.Count, _pending, true));
                return list;
            }
        }

        public void Capture(string label)
        {
            if (_restoring) return;
            EnsureSeed();
            Commit();                              // finalize the previous action's result
            DropRedoBranch();                      // a new action diverges from the current point
            _pending = label;                      // committed lazily once its mutation completes
            Changed?.Invoke();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            Commit();                              // ensure the latest action is on the timeline
            if (_index <= 0) return;
            _index--;
            Apply(_states[_index]);
            Changed?.Invoke();
        }

        public void Redo()
        {
            if (_pending is not null || _index >= _states.Count - 1) return;
            _index++;
            Apply(_states[_index]);
            Changed?.Invoke();
        }

        public void JumpTo(int index)
        {
            Commit();
            if (_states.Count == 0) return;
            index = Math.Clamp(index, 0, _states.Count - 1);
            if (index == _index) return;
            _index = index;
            Apply(_states[_index]);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _states.Clear();
            _pending = null;
            _index = 0;
            _states.Add(Take("Open")); // seed the baseline from the (current) project
            Changed?.Invoke();
        }

        // --- internals ---

        private void EnsureSeed()
        {
            if (_states.Count == 0) { _states.Add(Take("Open")); _index = 0; }
        }

        // Snapshots the now-complete result of the pending action onto the timeline.
        private void Commit()
        {
            if (_pending is null) return;
            var label = _pending;
            _pending = null;
            _states.Add(Take(label));
            _index = _states.Count - 1;
            if (_states.Count > MaxDepth) { _states.RemoveAt(0); _index--; }
        }

        private void DropRedoBranch()
        {
            if (_index < _states.Count - 1)
                _states.RemoveRange(_index + 1, _states.Count - _index - 1);
        }

        private Entry Take(string label) => new(
            ProjectCloner.Clone(_project.Current, _instruments, _effects),
            _transport.LoopStart, _transport.LoopEnd, _transport.StartBeat, label);

        private void Apply(Entry entry)
        {
            _restoring = true;
            try
            {
                var selectedId = _selection.SelectedTrack?.Id;

                // Install a fresh clone so the stored snapshot stays pristine for repeat visits.
                var live = ProjectCloner.Clone(entry.Project, _instruments, _effects);
                _project.SetCurrentProject(live); // fires ProjectChanged → rebuilds timeline + engine
                _transport.LoopStart = entry.LoopStart;
                _transport.LoopEnd = entry.LoopEnd;
                _transport.StartBeat = entry.StartBeat;

                // The swap invalidated the old selection references; re-resolve the track by its stable Id.
                _selection.SelectTrack(selectedId is { } id ? live.Tracks.Find(t => t.Id == id) : null);
            }
            finally
            {
                _restoring = false;
            }
        }

        private sealed record Entry(Project Project, double LoopStart, double LoopEnd, double StartBeat, string Label);
    }
}
