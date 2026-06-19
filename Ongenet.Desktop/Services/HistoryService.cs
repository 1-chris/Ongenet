using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.Services
{
    /// <summary>Default <see cref="IHistoryService"/> — see the interface for the contract.</summary>
    public sealed class HistoryService : IHistoryService
    {
        private const int MaxDepth = 50;

        private readonly IProjectService _project;
        private readonly ITransportService _transport;
        private readonly ISelectionService _selection;
        private readonly IInstrumentRegistry _instruments;
        private readonly IEffectRegistry _effects;

        // Lists used as stacks (last element = top), so the oldest entry can be trimmed from the front.
        private readonly List<Snapshot> _undo = new();
        private readonly List<Snapshot> _redo = new();
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

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public event Action? Changed;

        public void Capture(string label)
        {
            if (_restoring) return; // don't record snapshots while applying one

            _undo.Add(TakeSnapshot(label));
            if (_undo.Count > MaxDepth) _undo.RemoveAt(0);
            _redo.Clear();
            Changed?.Invoke();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(TakeSnapshot("redo"));        // freeze the current (post-action) state for redo
            var s = Pop(_undo);
            Apply(s);
            Changed?.Invoke();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(TakeSnapshot("undo"));
            var s = Pop(_redo);
            Apply(s);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke();
        }

        private Snapshot TakeSnapshot(string label) => new(
            ProjectCloner.Clone(_project.Current, _instruments, _effects),
            _transport.LoopStart, _transport.LoopEnd, _transport.StartBeat, label);

        private void Apply(Snapshot s)
        {
            _restoring = true;
            try
            {
                var selectedId = _selection.SelectedTrack?.Id;

                _project.SetCurrentProject(s.Project); // fires ProjectChanged → rebuilds timeline + engine
                _transport.LoopStart = s.LoopStart;
                _transport.LoopEnd = s.LoopEnd;
                _transport.StartBeat = s.StartBeat;

                // The swap invalidated the old selection references; re-resolve the track by its stable Id.
                _selection.SelectTrack(selectedId is { } id ? s.Project.Tracks.Find(t => t.Id == id) : null);
            }
            finally
            {
                _restoring = false;
            }
        }

        private static Snapshot Pop(List<Snapshot> stack)
        {
            var s = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return s;
        }

        private sealed record Snapshot(Project Project, double LoopStart, double LoopEnd, double StartBeat, string Label);
    }
}
