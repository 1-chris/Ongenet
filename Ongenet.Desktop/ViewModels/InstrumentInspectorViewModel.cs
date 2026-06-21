using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Services;
using Ongenet.Desktop.ViewModels.Instruments;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Bottom-panel inspector for the selected instrument track: a rack of instrument cards (each its own
    /// <see cref="InstrumentSlotViewModel"/> with editor + pre-effect chain), an "add instrument" menu /
    /// drop zone, and a shared mini-keyboard that plays every enabled instrument at once.
    /// </summary>
    public class InstrumentInspectorViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly IAudioFileService _audioFiles;
        private readonly IPreviewService _preview;
        private readonly ITransportService _transport;
        private readonly IPlaybackClock _clock;
        private readonly IEffectRegistry _effects;
        private readonly IInstrumentRegistry _instruments;
        private readonly IHistoryService _history;
        private readonly IEventAggregator _events;

        // Preferred display order for the add-instrument menu categories.
        private static readonly string[] CategoryOrder = { "Synth", "Sampler", "Drum", "Plugins" };

        public InstrumentInspectorViewModel(ISelectionService selection, IAudioFileService audioFiles,
            IPreviewService preview, ITransportService transport, IPlaybackClock clock, IEffectRegistry effects,
            IInstrumentRegistry instruments, IHistoryService history, IEventAggregator events)
        {
            _selection = selection;
            _audioFiles = audioFiles;
            _preview = preview;
            _transport = transport;
            _clock = clock;
            _effects = effects;
            _instruments = instruments;
            _history = history;
            _events = events;

            _selection.SelectionChanged += OnSelectionChanged;
            _preview.ActiveNotesChanged += UpdateKeyHighlights;
            _instruments.Changed += () => Dispatcher.UIThread.Post(RebuildAddable);
            Keys = BuildKeyboard();
            RebuildAddable();
            Rebuild();
        }

        private Track? Track => _selection.SelectedTrack is { Kind: TrackKind.Instrument } t ? t : null;

        /// <summary>True when an instrument track is selected (shows the rack / drop zone).</summary>
        public bool HasInstrumentTrack => Track is not null;

        /// <summary>True when the selected instrument track has no instruments yet (shows the drop zone).</summary>
        public bool IsEmpty => Track is { } t && t.Instruments.Count == 0;

        public string TrackName => Track?.Name ?? string.Empty;

        /// <summary>The instrument cards for the selected track, in rack order.</summary>
        public ObservableCollection<InstrumentSlotViewModel> Slots { get; } = new();

        /// <summary>The "Add instrument" menu, grouped by category.</summary>
        public IReadOnlyList<InstrumentCategoryViewModel> AddableCategories { get; private set; } =
            new List<InstrumentCategoryViewModel>();

        /// <summary>The on-screen keyboard keys (one octave from C4); plays all enabled instruments.</summary>
        public IReadOnlyList<KeyViewModel> Keys { get; }

        public void NoteOn(int midiNote) => _preview.NoteOn(midiNote);
        public void NoteOff(int midiNote) => _preview.NoteOff(midiNote);

        /// <summary>Adds an instrument of the given type to the selected track's rack.</summary>
        public void AddInstrument(string instrumentId)
        {
            if (Create(instrumentId) is { } instrument) AddSlot(instrument, "Add instrument");
        }

        /// <summary>Inserts a new instrument at <paramref name="index"/> in the rack (clamped).</summary>
        public void InsertInstrument(int index, string instrumentId)
        {
            if (Create(instrumentId) is { } instrument) InsertSlot(index, instrument, "Add instrument");
        }

        /// <summary>Replaces the instrument at <paramref name="index"/> with a new one of the given type.</summary>
        public void ReplaceInstrument(int index, string instrumentId)
        {
            if (Create(instrumentId) is { } instrument) ReplaceSlotAt(index, instrument, "Replace instrument");
        }

        /// <summary>Adds an instrument loaded from a dropped <c>.ongenpreset</c> to the rack.</summary>
        public void AddInstrumentPreset(string presetPath)
        {
            if (LoadInstrumentPreset(presetPath) is { } instrument) AddSlot(instrument, "Add preset");
        }

        /// <summary>Loads a dropped sound font (.sf2/.sfz) into a new Sampler instrument at the end of the rack.</summary>
        public void AddSoundFont(string path)
        {
            if (Create(SamplerInstrument.TypeId) is not { } sampler) return;
            AddSlot(sampler, "Add instrument");
            LoadSoundFontInto(Slots.Count - 1, path);
        }

        /// <summary>Inserts a new Sampler at <paramref name="index"/> and loads the sound font into it.</summary>
        public void InsertSoundFont(int index, string path)
        {
            if (Create(SamplerInstrument.TypeId) is not { } sampler) return;
            index = Math.Clamp(index, 0, Slots.Count);
            InsertSlot(index, sampler, "Add instrument");
            LoadSoundFontInto(index, path);
        }

        /// <summary>Replaces the instrument at <paramref name="index"/> with a Sampler holding the sound font.</summary>
        public void ReplaceWithSoundFont(int index, string path)
        {
            if (index < 0 || index >= Slots.Count || Create(SamplerInstrument.TypeId) is not { } sampler) return;
            ReplaceSlotAt(index, sampler, "Replace instrument");
            LoadSoundFontInto(index, path);
        }

        // Loads a sound font into the (rebuilt) slot at index, if present.
        private void LoadSoundFontInto(int index, string path)
        {
            if (index >= 0 && index < Slots.Count) Slots[index].LoadSamplerFromPath(path);
        }

        private IInstrument? Create(string instrumentId)
        {
            if (string.IsNullOrEmpty(instrumentId)) return null;
            try { return _instruments.Create(instrumentId); }
            catch { return null; }
        }

        private IInstrument? LoadInstrumentPreset(string path)
        {
            try
            {
                using var fs = System.IO.File.OpenRead(path);
                return Ongenet.Core.Persistence.PresetFile.Load(fs, _instruments, _effects)?.Instrument;
            }
            catch { return null; }
        }

        // --- Shared slot mutators (commit + notify the engine + rebuild the cards) ---

        private void AddSlot(IInstrument instrument, string label)
        {
            if (Track is not { } track) return;
            _history.Capture(label);
            track.Instruments.Add(new InstrumentSlot(instrument));
            track.CommitInstruments();
            _events.Publish(new TracksChangedEvent());
            Rebuild();
        }

        private void InsertSlot(int index, IInstrument instrument, string label)
        {
            if (Track is not { } track) return;
            _history.Capture(label);
            track.Instruments.Insert(Math.Clamp(index, 0, track.Instruments.Count), new InstrumentSlot(instrument));
            track.CommitInstruments();
            _events.Publish(new TracksChangedEvent());
            Rebuild();
        }

        private void ReplaceSlotAt(int index, IInstrument instrument, string label)
        {
            if (Track is not { } track || index < 0 || index >= track.Instruments.Count) return;
            _history.Capture(label);
            var old = track.Instruments[index];
            old.Instrument.AllNotesOff();
            // Preserve the slot's pre-effect chain + enabled state; only the instrument changes.
            track.Instruments[index] = new InstrumentSlot(instrument) { Enabled = old.Enabled };
            track.CommitInstruments();
            _events.Publish(new TracksChangedEvent());
            Rebuild();
        }

        private int IndexOf(InstrumentSlotViewModel target)
            => Track is { } track ? track.Instruments.IndexOf(target.Slot) : -1;

        // --- Relative-to-card helpers used by the per-card drop zones (above / below / replace) ---

        private void InsertRelativeToSlot(InstrumentSlotViewModel target, string instrumentId, bool below)
        {
            var i = IndexOf(target);
            if (i >= 0) InsertInstrument(below ? i + 1 : i, instrumentId);
        }

        private void ReplaceSlotWith(InstrumentSlotViewModel target, string instrumentId)
        {
            var i = IndexOf(target);
            if (i >= 0) ReplaceInstrument(i, instrumentId);
        }

        private void InsertPresetRelativeToSlot(InstrumentSlotViewModel target, string presetPath, bool below)
        {
            var i = IndexOf(target);
            if (i >= 0 && LoadInstrumentPreset(presetPath) is { } inst) InsertSlot(below ? i + 1 : i, inst, "Add preset");
        }

        private void ReplacePresetForSlot(InstrumentSlotViewModel target, string presetPath)
        {
            var i = IndexOf(target);
            if (i >= 0 && LoadInstrumentPreset(presetPath) is { } inst) ReplaceSlotAt(i, inst, "Replace instrument");
        }

        private void InsertSoundFontRelativeToSlot(InstrumentSlotViewModel target, string path, bool below)
        {
            var i = IndexOf(target);
            if (i >= 0) InsertSoundFont(below ? i + 1 : i, path);
        }

        private void ReplaceSoundFontForSlot(InstrumentSlotViewModel target, string path)
        {
            var i = IndexOf(target);
            if (i >= 0) ReplaceWithSoundFont(i, path);
        }

        private void RemoveSlot(InstrumentSlotViewModel slot)
        {
            if (Track is not { } track) return;
            _history.Capture("Remove instrument");
            slot.Instrument.AllNotesOff();
            track.Instruments.Remove(slot.Slot);
            track.CommitInstruments();
            _events.Publish(new TracksChangedEvent());
            Rebuild();
        }

        private void MoveSlot(InstrumentSlotViewModel slot, int delta)
        {
            if (Track is not { } track) return;
            var index = track.Instruments.IndexOf(slot.Slot);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= track.Instruments.Count) return;
            _history.Capture("Reorder instrument");
            track.Instruments.RemoveAt(index);
            track.Instruments.Insert(target, slot.Slot);
            track.CommitInstruments();
            _events.Publish(new TracksChangedEvent());
            Rebuild();
        }

        /// <summary>Services an open plugin GUI for every slot (called on a UI timer by the view).</summary>
        public void PumpEditors()
        {
            foreach (var slot in Slots) slot.PumpEditor();
        }

        /// <summary>Refreshes the open/close button of whichever slot owns <paramref name="editor"/>.</summary>
        public void RefreshEditor(IPluginEditor editor)
        {
            foreach (var slot in Slots)
                if (ReferenceEquals(slot.CurrentEditor, editor)) { slot.NotifyEditorState(); return; }
        }

        private void RebuildAddable()
        {
            int Rank(string category)
            {
                var i = Array.IndexOf(CategoryOrder, category);
                return i < 0 ? CategoryOrder.Length : i;
            }

            AddableCategories = _instruments.Available
                .GroupBy(info => info.Category)
                .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key)
                .Select(g => new InstrumentCategoryViewModel(g.Key, g.ToList()))
                .ToList();
            OnPropertyChanged(nameof(AddableCategories));
        }

        private void Rebuild()
        {
            Slots.Clear();
            if (Track is { } track)
            {
                foreach (var slot in track.Instruments)
                    Slots.Add(new InstrumentSlotViewModel(slot, _audioFiles, _transport, _history, _effects,
                        _clock, () => _events.Publish(new TracksChangedEvent()), RemoveSlot, MoveSlot,
                        InsertRelativeToSlot, ReplaceSlotWith, InsertPresetRelativeToSlot, ReplacePresetForSlot,
                        InsertSoundFontRelativeToSlot, ReplaceSoundFontForSlot));
            }

            for (var i = 0; i < Slots.Count; i++)
            {
                Slots[i].IsFirst = i == 0;
                Slots[i].IsLast = i == Slots.Count - 1;
            }

            OnPropertyChanged(nameof(HasInstrumentTrack));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(TrackName));
        }

        private void UpdateKeyHighlights()
        {
            foreach (var key in Keys) key.IsActive = _preview.IsActive(key.MidiNote);
        }

        private static IReadOnlyList<KeyViewModel> BuildKeyboard()
        {
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            bool[] black = { false, true, false, true, false, false, true, false, true, false, true, false };

            var keys = new List<KeyViewModel>();
            for (var i = 0; i <= 12; i++)
            {
                var note = 60 + i;
                keys.Add(new KeyViewModel(note, $"{names[i % 12]}{4 + i / 12}", i < 12 && black[i]));
            }

            return keys;
        }

        private void OnSelectionChanged() => Rebuild();
    }
}
