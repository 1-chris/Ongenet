using System.Collections.Generic;
using System.Collections.ObjectModel;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Services;
using Ongenet.Desktop.ViewModels.Instruments;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Bottom-panel inspector for the selected instrument track. Renders the instrument's generic
    /// parameters, an on-screen keyboard, and (for the sampler) a sample loader.
    /// </summary>
    public class InstrumentInspectorViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly IAudioFileService _audioFiles;
        private readonly IPreviewService _preview;
        private readonly ITransportService _transport;

        public InstrumentInspectorViewModel(ISelectionService selection, IAudioFileService audioFiles,
            IPreviewService preview, ITransportService transport, IPlaybackClock clock)
        {
            _selection = selection;
            _audioFiles = audioFiles;
            _preview = preview;
            _transport = transport;
            _selection.SelectionChanged += OnSelectionChanged;
            _preview.ActiveNotesChanged += UpdateKeyHighlights;
            Keys = BuildKeyboard();
            RebuildParameters();
            // Reflect automation moving the instrument's knobs live during playback.
            clock.Tick += OnPlaybackTick;
        }

        // While playing, re-read each parameter so automation visibly turns the knobs.
        private void OnPlaybackTick()
        {
            if (_transport.State != TransportState.Playing) return;
            foreach (var p in Parameters) p.Refresh();
        }

        private IInstrument? Instrument => _selection.SelectedTrack?.Instrument;

        /// <summary>True when an instrument track is selected.</summary>
        public bool HasInstrument => Instrument is not null;

        /// <summary>Name of the selected track / instrument.</summary>
        public string InstrumentName => Instrument?.Name ?? "No instrument";

        /// <summary>Generic editable parameters of the selected instrument (flat; used for live refresh).</summary>
        public ObservableCollection<ParameterViewModel> Parameters { get; } = new();

        /// <summary>The same parameters arranged into titled groups for the fieldset layout.</summary>
        public ObservableCollection<ParameterGroupViewModel> ParameterGroups { get; } = new();

        /// <summary>The on-screen keyboard keys (one octave from C4).</summary>
        public IReadOnlyList<KeyViewModel> Keys { get; }

        // --- Sampler support ---

        private ISampleHost? SampleHost => Instrument as ISampleHost;
        public bool IsSampler => SampleHost is not null;
        public string SampleName => SampleHost?.SampleName ?? "(no sample loaded)";

        // --- Granular support (grain monitor) ---

        /// <summary>True when the selected instrument is the granular synth (shows the grain monitor).</summary>
        public bool IsGranular => Instrument is GranularInstrument;

        /// <summary>The granular synth's grain feed for the monitor, or null.</summary>
        public GrainMonitor? GrainMonitor => (Instrument as GranularInstrument)?.Monitor;

        /// <summary>Decodes a file and loads it into the selected sampler instrument.</summary>
        public void LoadSampleFromPath(string path)
        {
            if (SampleHost is not { } host) return;
            var loaded = _audioFiles.Load(path);
            if (loaded is null) return;
            host.LoadSample(loaded.Samples, System.IO.Path.GetFileName(path));
            OnPropertyChanged(nameof(SampleName));
        }

        public void NoteOn(int midiNote) => _preview.NoteOn(midiNote);
        public void NoteOff(int midiNote) => _preview.NoteOff(midiNote);

        // --- Plugin editor (CLAP GUI) ---

        private IPluginEditor? Editor => Instrument as IPluginEditor;

        /// <summary>The selected instrument's editor (captured by the view when opening a window).</summary>
        public IPluginEditor? CurrentEditor => Editor;

        /// <summary>True when the selected instrument has its own openable GUI (e.g. a CLAP plugin).</summary>
        public bool HasEditor => Editor is { HasEditor: true };
        public bool IsEditorOpen => Editor is { IsEditorOpen: true };
        public bool EditorPrefersFloating => Editor is { PrefersFloating: true };
        public int EditorWidth => Editor?.EditorWidth ?? 0;
        public int EditorHeight => Editor?.EditorHeight ?? 0;
        public string EditorButtonText => IsEditorOpen ? "Close plugin UI" : "Open plugin UI";

        /// <summary>Opens the plugin GUI into the host-supplied window (embedded) or floating.</summary>
        public void OpenEditor(nint windowHandle, string apiType, bool floating)
        {
            Editor?.OpenEditor(windowHandle, apiType, floating);
            NotifyEditorState();
        }

        /// <summary>Closes the plugin GUI.</summary>
        public void CloseEditor()
        {
            Editor?.CloseEditor();
            NotifyEditorState();
        }

        /// <summary>Re-reads the editor open state (button text), e.g. after the plugin window closes.</summary>
        public void NotifyEditorState()
        {
            OnPropertyChanged(nameof(IsEditorOpen));
            OnPropertyChanged(nameof(EditorButtonText));
        }

        /// <summary>Services an open plugin GUI (called on a UI timer by the view).</summary>
        public void PumpEditor() => Editor?.PumpEditor();

        private void UpdateKeyHighlights()
        {
            foreach (var key in Keys) key.IsActive = _preview.IsActive(key.MidiNote);
        }

        private void RebuildParameters()
        {
            Parameters.Clear();
            ParameterGroups.Clear();
            if (Instrument is not { } instrument) return;

            // Group by the parameter's Group label, preserving first-seen order; ungrouped → "".
            var order = new List<string>();
            var byGroup = new Dictionary<string, List<ParameterViewModel>>();
            foreach (var p in instrument.Parameters)
            {
                var vm = ParameterViewModel.Create(p);
                Parameters.Add(vm);
                var key = p.Group ?? string.Empty;
                if (!byGroup.TryGetValue(key, out var list))
                {
                    list = new List<ParameterViewModel>();
                    byGroup[key] = list;
                    order.Add(key);
                }

                list.Add(vm);
            }

            foreach (var key in order)
                ParameterGroups.Add(new ParameterGroupViewModel(key, byGroup[key]));
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

        private void OnSelectionChanged()
        {
            RebuildParameters();
            OnPropertyChanged(nameof(HasInstrument));
            OnPropertyChanged(nameof(InstrumentName));
            OnPropertyChanged(nameof(IsSampler));
            OnPropertyChanged(nameof(SampleName));
            OnPropertyChanged(nameof(IsGranular));
            OnPropertyChanged(nameof(GrainMonitor));
            OnPropertyChanged(nameof(HasEditor));
            OnPropertyChanged(nameof(IsEditorOpen));
            OnPropertyChanged(nameof(EditorButtonText));
        }
    }
}
