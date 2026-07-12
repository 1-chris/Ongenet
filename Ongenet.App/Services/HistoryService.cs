using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.ViewModels;

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
        private readonly Func<PianoRollViewModel> _pianoRoll;

        private readonly List<Entry> _states = new();
        private int _index;
        private string? _pending;
        private bool _restoring;

        public HistoryService(IProjectService project, ITransportService transport, ISelectionService selection,
            IInstrumentRegistry instruments, IEffectRegistry effects, Func<PianoRollViewModel> pianoRoll)
        {
            _project = project;
            _transport = transport;
            _selection = selection;
            _instruments = instruments;
            _effects = effects;
            _pianoRoll = pianoRoll;
        }

        public bool CanUndo => _pending is not null || _index > 0;
        public bool CanRedo => _pending is null && _index < _states.Count - 1;
        public event Action? Changed;
        public event Action<IReadOnlyList<NoteSelectionKey>>? NoteSelectionRestored;

        public IReadOnlyList<HistoryEntry> Timeline
        {
            get
            {
                var list = new List<HistoryEntry>(_states.Count + 1);
                for (var i = 0; i < _states.Count; i++)
                    list.Add(new HistoryEntry(i, _states[i].Label, _pending is null && i == _index));
                if (_pending is not null)
                    list.Add(new HistoryEntry(_states.Count, _pending, true));
                return list;
            }
        }

        public void Capture(string label)
        {
            if (_restoring) return;
            EnsureSeed();
            Commit();
            DropRedoBranch();
            _pending = label;
            Changed?.Invoke();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            Commit();
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
            _states.Add(Take("Open"));
            Changed?.Invoke();
        }

        private void EnsureSeed()
        {
            if (_states.Count == 0) { _states.Add(Take("Open")); _index = 0; }
        }

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
            _transport.LoopStart, _transport.LoopEnd, _transport.StartBeat, label,
            _selection.SelectedTrack?.Id,
            _selection.SelectedClip?.Id,
            _selection.SelectedPatternClip?.Id,
            CaptureNoteSelection());

        private List<NoteSelectionKey> CaptureNoteSelection()
            => _pianoRoll().SelectedNotes
                .Select(n => new NoteSelectionKey(n.Model.StartBeat, n.Model.Note))
                .ToList();

        private void Apply(Entry entry)
        {
            _restoring = true;
            try
            {
                var live = ProjectCloner.Clone(entry.Project, _instruments, _effects);
                _project.SetCurrentProject(live);
                _transport.LoopStart = entry.LoopStart;
                _transport.LoopEnd = entry.LoopEnd;
                _transport.StartBeat = entry.StartBeat;

                RestoreSelection(live, entry);
                NoteSelectionRestored?.Invoke(entry.NoteSelection);
            }
            finally
            {
                _restoring = false;
            }
        }

        private void RestoreSelection(Project live, Entry entry)
        {
            Track? track = entry.SelectedTrackId is { } tid
                ? live.Tracks.Find(t => t.Id == tid)
                : null;

            if (entry.SelectedClipId is { } clipId && track is not null)
            {
                var clip = track.Clips.Find(c => c.Id == clipId);
                _selection.SelectClip(clip, track);
                return;
            }

            if (entry.SelectedPatternClipId is { } pcId)
            {
                var pc = live.PatternClips.Find(c => c.Id == pcId);
                track ??= pc is not null ? live.Tracks.Find(t => t.Id == pc.TrackId) : null;
                _selection.SelectPatternClip(pc, track);
                return;
            }

            _selection.SelectTrack(track);
        }

        private sealed record Entry(
            Project Project,
            double LoopStart,
            double LoopEnd,
            double StartBeat,
            string Label,
            Guid? SelectedTrackId,
            Guid? SelectedClipId,
            Guid? SelectedPatternClipId,
            List<NoteSelectionKey> NoteSelection);
    }
}
