using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Instruments.Sfz;
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
        private const int PreviewSampleRate = 44100;

        private readonly ISelectionService _selection;
        private readonly IAudioFileService _audioFiles;
        private readonly IPreviewService _preview;
        private readonly ITransportService _transport;

        // Debounced preview rendering (coalesces knob-drag and tick storms into ~10 renders/sec).
        private readonly DispatcherTimer _previewTimer;
        private readonly List<ParameterViewModel> _subscribedParams = new();
        private float[] _previewBuffer = Array.Empty<float>();

        public InstrumentInspectorViewModel(ISelectionService selection, IAudioFileService audioFiles,
            IPreviewService preview, ITransportService transport, IPlaybackClock clock)
        {
            _selection = selection;
            _audioFiles = audioFiles;
            _preview = preview;
            _transport = transport;
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _previewTimer.Tick += OnPreviewTimerTick;
            _selection.SelectionChanged += OnSelectionChanged;
            _preview.ActiveNotesChanged += UpdateKeyHighlights;
            Keys = BuildKeyboard();
            RebuildParameters();
            RenderPreview();
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

        // --- Preset support ---

        private int _selectedPreset = -1;

        private IPresetProvider? PresetProvider => Instrument as IPresetProvider;

        /// <summary>True when the selected instrument ships built-in presets (shows the preset picker).</summary>
        public bool IsPresetProvider => PresetProvider is not null;

        /// <summary>The selected instrument's preset names, or empty.</summary>
        public IReadOnlyList<string> PresetNames => PresetProvider?.PresetNames ?? Array.Empty<string>();

        /// <summary>
        /// The chosen preset index (-1 = none picked yet). Setting it applies the preset to the
        /// instrument and rebuilds the parameter editor to reflect the new values.
        /// </summary>
        public int SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset == value) return;
                _selectedPreset = value;
                OnPropertyChanged();
                if (value < 0 || PresetProvider is not { } provider) return;
                App.ServiceProvider?.GetService<IHistoryService>()?.Capture("Load preset");
                provider.LoadPreset(value);
                RebuildParameters();
                RenderPreview();
            }
        }

        // --- Sampler support ---

        private ISampleHost? SampleHost => Instrument as ISampleHost;
        public bool IsSampler => SampleHost is not null;
        public string SampleName => SampleHost?.SampleName ?? "(no sample loaded)";

        // --- SFZ "Sampler" support ---

        private SfzInstrument? Sfz => Instrument as SfzInstrument;

        /// <summary>True when the selected instrument is the SFZ "Sampler" (shows the SFZ loader).</summary>
        public bool IsSfz => Sfz is not null;

        private bool _sfzLoading;
        private double _sfzLoadProgress;

        /// <summary>Loaded SFZ file + region count, a "loading" notice, or a placeholder.</summary>
        public string SfzStatus => _sfzLoading
            ? $"Loading… {_sfzLoadProgress * 100:0}%"
            : Sfz is { SfzPath.Length: > 0 } s
                ? $"{System.IO.Path.GetFileName(s.SfzPath)} — {s.Regions.Count} region(s)"
                : "(no SFZ loaded)";

        /// <summary>True while a patch is loading (shows the progress bar).</summary>
        public bool IsSfzLoading => _sfzLoading;

        /// <summary>Load progress, 0..1.</summary>
        public double SfzLoadProgress => _sfzLoadProgress;

        /// <summary>
        /// Parses an .sfz file and loads it into the selected SFZ instrument. Loading (which can take a
        /// while for a large library) runs on a background thread so the UI never freezes, reporting progress.
        /// </summary>
        public async void LoadSfzFromPath(string path)
        {
            if (Sfz is not { } sfz || _sfzLoading) return;
            var loader = App.ServiceProvider?.GetService<ISfzLoadService>();
            if (loader is null) return;

            _sfzLoading = true;
            _sfzLoadProgress = 0;
            OnPropertyChanged(nameof(SfzStatus));
            OnPropertyChanged(nameof(IsSfzLoading));
            OnPropertyChanged(nameof(SfzLoadProgress));

            // Progress<double> marshals callbacks back to this (UI) thread.
            var progress = new Progress<double>(p =>
            {
                _sfzLoadProgress = p;
                OnPropertyChanged(nameof(SfzLoadProgress));
                OnPropertyChanged(nameof(SfzStatus));
            });
            var result = await Task.Run(() => loader.Load(path, progress));

            _sfzLoading = false;
            OnPropertyChanged(nameof(IsSfzLoading));
            if (result is null) { OnPropertyChanged(nameof(SfzStatus)); return; }

            App.ServiceProvider?.GetService<IHistoryService>()?.Capture("Load SFZ");
            sfz.ApplyLoad(result);
            RebuildParameters();
            OnPropertyChanged(nameof(SfzStatus));
            OnPropertyChanged(nameof(InstrumentName));
            NotifySfzVisuals();
        }

        /// <summary>The loaded regions, for the zone-map control.</summary>
        public IReadOnlyList<SfzRegionRuntime> SfzZones => Sfz?.Regions ?? Array.Empty<SfzRegionRuntime>();

        /// <summary>True when there are regions to visualise.</summary>
        public bool HasZones => SfzZones.Count > 0;

        private int _sfzRevision;

        /// <summary>Bumped when a new patch loads, to force the zone map to repaint.</summary>
        public int SfzRevision => _sfzRevision;

        // Representative amp envelope (the first region) for the envelope display.
        private SfzRegionRuntime? FirstZone => Sfz is { Regions.Count: > 0 } s ? s.Regions[0] : null;
        public double EnvDelay => FirstZone?.AmpEg.Delay ?? 0;
        public double EnvAttack => FirstZone?.AmpEg.Attack ?? 0;
        public double EnvHold => FirstZone?.AmpEg.Hold ?? 0;
        public double EnvDecay => FirstZone?.AmpEg.Decay ?? 0;
        public double EnvSustain => FirstZone?.AmpEg.Sustain ?? 1.0;
        public double EnvRelease => FirstZone?.AmpEg.Release ?? 0;

        // --- Live MIDI controllers (drive the selected instrument via the preview service) ---

        private double _modWheel;

        /// <summary>Mod wheel (CC1), 0..127.</summary>
        public double ModWheel
        {
            get => _modWheel;
            set { if (SetField(ref _modWheel, value)) _preview.ControlChange(1, (int)value); }
        }

        private double _pitchBendValue = 8192;

        /// <summary>Pitch bend, 0..16383 (centre 8192).</summary>
        public double PitchBendValue
        {
            get => _pitchBendValue;
            set { if (SetField(ref _pitchBendValue, value)) _preview.PitchBend((int)value); }
        }

        /// <summary>Recentres the pitch-bend control (called when the user lets go of the slider).</summary>
        public void ResetPitchBend() => PitchBendValue = 8192;

        private void NotifySfzVisuals()
        {
            _sfzRevision++;
            OnPropertyChanged(nameof(SfzZones));
            OnPropertyChanged(nameof(HasZones));
            OnPropertyChanged(nameof(SfzRevision));
            OnPropertyChanged(nameof(EnvDelay));
            OnPropertyChanged(nameof(EnvAttack));
            OnPropertyChanged(nameof(EnvHold));
            OnPropertyChanged(nameof(EnvDecay));
            OnPropertyChanged(nameof(EnvSustain));
            OnPropertyChanged(nameof(EnvRelease));
        }

        // --- Granular support (grain monitor) ---

        /// <summary>True when the selected instrument is the granular synth (shows the grain monitor).</summary>
        public bool IsGranular => Instrument is GranularInstrument;

        /// <summary>The granular synth's grain feed for the monitor, or null.</summary>
        public GrainMonitor? GrainMonitor => (Instrument as GranularInstrument)?.Monitor;

        // --- Waveform preview (any IPreviewRenderer instrument) ---

        private IPreviewRenderer? PreviewRenderer => Instrument as IPreviewRenderer;

        /// <summary>True when the selected instrument can render a preview waveform (shows the preview).</summary>
        public bool IsPreviewable => PreviewRenderer is not null;

        /// <summary>A peak summary of the current patch's one-shot, for the preview control.</summary>
        public AudioWaveform? InstrumentPreview { get; private set; }

        /// <summary>Bumped whenever <see cref="InstrumentPreview"/> is re-rendered, to force a redraw.</summary>
        public int PreviewRevision { get; private set; }

        // Restart the debounce so a burst of edits coalesces into one render.
        private void SchedulePreview()
        {
            if (!IsPreviewable) return;
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void OnPreviewTimerTick(object? sender, EventArgs e)
        {
            _previewTimer.Stop();
            RenderPreview();
        }

        // Render a one-shot off a DETACHED clone (never the live, audio-thread instrument).
        private void RenderPreview()
        {
            if (PreviewRenderer is not { } renderer || Instrument is not { } instrument)
            {
                if (InstrumentPreview is not null)
                {
                    InstrumentPreview = null;
                    OnPropertyChanged(nameof(InstrumentPreview));
                }

                return;
            }

            var clone = instrument.Clone() as IPreviewRenderer ?? renderer;
            var seconds = clone.PreviewSeconds <= 0 ? 1.0 : clone.PreviewSeconds;
            var length = Math.Max(1, (int)(seconds * PreviewSampleRate));
            if (_previewBuffer.Length < length) _previewBuffer = new float[length];

            var span = _previewBuffer.AsSpan(0, length);
            clone.RenderPreview(span, PreviewSampleRate);

            // Trim trailing silence so a short kick fills the view instead of a long flat tail.
            var used = length;
            while (used > 1 && Math.Abs(span[used - 1]) < 1e-4f) used--;
            used = Math.Min(length, used + PreviewSampleRate / 100); // ~10 ms of breathing room

            var samples = new float[used];
            span.Slice(0, used).CopyTo(samples);
            var buffer = new AudioSampleBuffer(samples, 1, PreviewSampleRate);
            InstrumentPreview = AudioWaveform.Build(buffer, Math.Max(8, used / 1000));

            OnPropertyChanged(nameof(InstrumentPreview));
            PreviewRevision++;
            OnPropertyChanged(nameof(PreviewRevision));
        }

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
            // Drop subscriptions from the previous instrument's parameter VMs to avoid leaks.
            foreach (var vm in _subscribedParams) vm.PropertyChanged -= OnParameterChanged;
            _subscribedParams.Clear();

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
                vm.PropertyChanged += OnParameterChanged; // redraw the preview on edit
                _subscribedParams.Add(vm);
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

        private void OnParameterChanged(object? sender, PropertyChangedEventArgs e) => SchedulePreview();

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
            _selectedPreset = -1; // reset the picker without re-applying a preset
            OnPropertyChanged(nameof(HasInstrument));
            OnPropertyChanged(nameof(InstrumentName));
            OnPropertyChanged(nameof(IsPresetProvider));
            OnPropertyChanged(nameof(PresetNames));
            OnPropertyChanged(nameof(SelectedPreset));
            OnPropertyChanged(nameof(IsSampler));
            OnPropertyChanged(nameof(SampleName));
            OnPropertyChanged(nameof(IsSfz));
            OnPropertyChanged(nameof(SfzStatus));
            NotifySfzVisuals();
            OnPropertyChanged(nameof(IsGranular));
            OnPropertyChanged(nameof(GrainMonitor));
            OnPropertyChanged(nameof(IsPreviewable));
            OnPropertyChanged(nameof(HasEditor));
            OnPropertyChanged(nameof(IsEditorOpen));
            OnPropertyChanged(nameof(EditorButtonText));
            RenderPreview();
        }
    }
}
