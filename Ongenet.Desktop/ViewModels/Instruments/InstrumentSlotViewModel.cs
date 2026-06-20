using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Instruments.Sfz;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Services;
using Ongenet.Desktop.ViewModels.Effects;

namespace Ongenet.Desktop.ViewModels.Instruments
{
    /// <summary>
    /// One instrument card in a track's instrument rack: the instrument's generic parameters, its
    /// specialised UI (presets / sampler / SFZ / granular / preview / plugin editor), an enable (bypass)
    /// toggle, remove/reorder commands, and its own (pre) effect chain. Mirrors the effect-card pattern.
    /// </summary>
    public sealed class InstrumentSlotViewModel : ViewModelBase
    {
        private const int PreviewSampleRate = 44100;

        private readonly InstrumentSlot _slot;
        private readonly IAudioFileService _audioFiles;
        private readonly ITransportService _transport;
        private readonly IHistoryService _history;
        private readonly Action _notifyChanged;            // publish TracksChangedEvent (engine re-prepare)
        private readonly Action<InstrumentSlotViewModel> _remove;
        private readonly Action<InstrumentSlotViewModel, int> _move;

        private readonly DispatcherTimer _previewTimer;
        private readonly List<ParameterViewModel> _subscribedParams = new();
        private float[] _previewBuffer = Array.Empty<float>();

        public InstrumentSlotViewModel(InstrumentSlot slot, IAudioFileService audioFiles,
            ITransportService transport, IHistoryService history, IEffectRegistry effects, IPlaybackClock clock,
            Action notifyChanged, Action<InstrumentSlotViewModel> remove, Action<InstrumentSlotViewModel, int> move)
        {
            _slot = slot;
            _audioFiles = audioFiles;
            _transport = transport;
            _history = history;
            _notifyChanged = notifyChanged;
            _remove = remove;
            _move = move;

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _previewTimer.Tick += OnPreviewTimerTick;

            Effects = new EffectChainViewModel(slot.Effects, slot.CommitEffects, notifyChanged,
                effects, history, transport, clock);

            RemoveCommand = new RelayCommand(() => _remove(this));
            ToggleEnabledCommand = new RelayCommand(() => IsEnabled = !IsEnabled);
            MoveUpCommand = new RelayCommand(() => _move(this, -1));
            MoveDownCommand = new RelayCommand(() => _move(this, +1));

            clock.Tick += OnPlaybackTick;
            RebuildParameters();
            RenderPreview();
        }

        public InstrumentSlot Slot => _slot;
        public IInstrument Instrument => _slot.Instrument;
        public string InstrumentName => Instrument.Name;

        /// <summary>The slot's own (pre) effect chain editor.</summary>
        public EffectChainViewModel Effects { get; }

        public RelayCommand RemoveCommand { get; }
        public RelayCommand ToggleEnabledCommand { get; }
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }

        public bool IsFirst { get; set; }
        public bool IsLast { get; set; }

        /// <summary>Whether this instrument sounds; when false the engine skips it (matches the effect bypass dot).</summary>
        public bool IsEnabled
        {
            get => _slot.Enabled;
            set
            {
                if (_slot.Enabled == value) return;
                _history.Capture(value ? "Enable instrument" : "Disable instrument");
                _slot.Enabled = value;
                // If silencing, kill any sounding notes so a held note doesn't hang.
                if (!value) Instrument.AllNotesOff();
                OnPropertyChanged();
            }
        }

        // While playing, re-read each parameter so automation visibly turns the knobs.
        private void OnPlaybackTick()
        {
            if (_transport.State != TransportState.Playing) return;
            foreach (var p in Parameters) p.Refresh();
        }

        /// <summary>Generic editable parameters (flat; used for live refresh).</summary>
        public ObservableCollection<ParameterViewModel> Parameters { get; } = new();

        /// <summary>The same parameters arranged into titled groups for the fieldset layout.</summary>
        public ObservableCollection<ParameterGroupViewModel> ParameterGroups { get; } = new();

        // --- Preset support ---

        private int _selectedPreset = -1;
        private IPresetProvider? PresetProvider => Instrument as IPresetProvider;
        public bool IsPresetProvider => PresetProvider is not null;
        public IReadOnlyList<string> PresetNames => PresetProvider?.PresetNames ?? Array.Empty<string>();

        public int SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset == value) return;
                _selectedPreset = value;
                OnPropertyChanged();
                if (value < 0 || PresetProvider is not { } provider) return;
                _history.Capture("Load preset");
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
        public bool IsSfz => Sfz is not null;

        private bool _sfzLoading;
        private double _sfzLoadProgress;

        public string SfzStatus => _sfzLoading
            ? $"Loading… {_sfzLoadProgress * 100:0}%"
            : Sfz is { SfzPath.Length: > 0 } s
                ? $"{System.IO.Path.GetFileName(s.SfzPath)} — {s.Regions.Count} region(s)"
                : "(no SFZ loaded)";

        public bool IsSfzLoading => _sfzLoading;
        public double SfzLoadProgress => _sfzLoadProgress;

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

            _history.Capture("Load SFZ");
            sfz.ApplyLoad(result);
            _notifyChanged(); // a fresh patch needs the engine to (re)prepare the instrument
            RebuildParameters();
            OnPropertyChanged(nameof(SfzStatus));
            OnPropertyChanged(nameof(InstrumentName));
            NotifySfzVisuals();
        }

        public IReadOnlyList<SfzRegionRuntime> SfzZones => Sfz?.Regions ?? Array.Empty<SfzRegionRuntime>();
        public bool HasZones => SfzZones.Count > 0;

        private int _sfzRevision;
        public int SfzRevision => _sfzRevision;

        private SfzRegionRuntime? FirstZone => Sfz is { Regions.Count: > 0 } s ? s.Regions[0] : null;
        public double EnvDelay => FirstZone?.AmpEg.Delay ?? 0;
        public double EnvAttack => FirstZone?.AmpEg.Attack ?? 0;
        public double EnvHold => FirstZone?.AmpEg.Hold ?? 0;
        public double EnvDecay => FirstZone?.AmpEg.Decay ?? 0;
        public double EnvSustain => FirstZone?.AmpEg.Sustain ?? 1.0;
        public double EnvRelease => FirstZone?.AmpEg.Release ?? 0;

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

        // --- Live MIDI controllers (drive this slot's instrument directly) ---

        private double _modWheel;
        public double ModWheel
        {
            get => _modWheel;
            set { if (SetField(ref _modWheel, value)) Instrument.ControlChange(1, (int)value); }
        }

        private double _pitchBendValue = 8192;
        public double PitchBendValue
        {
            get => _pitchBendValue;
            set { if (SetField(ref _pitchBendValue, value)) Instrument.PitchBend((int)value); }
        }

        public void ResetPitchBend() => PitchBendValue = 8192;

        // --- Granular support (grain monitor) ---

        public bool IsGranular => Instrument is GranularInstrument;
        public GrainMonitor? GrainMonitor => (Instrument as GranularInstrument)?.Monitor;

        // --- Waveform preview (any IPreviewRenderer instrument) ---

        private IPreviewRenderer? PreviewRenderer => Instrument as IPreviewRenderer;
        public bool IsPreviewable => PreviewRenderer is not null;
        public AudioWaveform? InstrumentPreview { get; private set; }
        public int PreviewRevision { get; private set; }

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

        private void RenderPreview()
        {
            if (PreviewRenderer is not { } renderer)
            {
                if (InstrumentPreview is not null)
                {
                    InstrumentPreview = null;
                    OnPropertyChanged(nameof(InstrumentPreview));
                }

                return;
            }

            var clone = Instrument.Clone() as IPreviewRenderer ?? renderer;
            var seconds = clone.PreviewSeconds <= 0 ? 1.0 : clone.PreviewSeconds;
            var length = Math.Max(1, (int)(seconds * PreviewSampleRate));
            if (_previewBuffer.Length < length) _previewBuffer = new float[length];

            var span = _previewBuffer.AsSpan(0, length);
            clone.RenderPreview(span, PreviewSampleRate);

            var used = length;
            while (used > 1 && Math.Abs(span[used - 1]) < 1e-4f) used--;
            used = Math.Min(length, used + PreviewSampleRate / 100);

            var samples = new float[used];
            span.Slice(0, used).CopyTo(samples);
            var buffer = new AudioSampleBuffer(samples, 1, PreviewSampleRate);
            InstrumentPreview = AudioWaveform.Build(buffer, Math.Max(8, used / 1000));

            OnPropertyChanged(nameof(InstrumentPreview));
            PreviewRevision++;
            OnPropertyChanged(nameof(PreviewRevision));
        }

        public void LoadSampleFromPath(string path)
        {
            if (SampleHost is not { } host) return;
            var loaded = _audioFiles.Load(path);
            if (loaded is null) return;
            host.LoadSample(loaded.Samples, System.IO.Path.GetFileName(path));
            OnPropertyChanged(nameof(SampleName));
        }

        // --- Plugin editor (CLAP GUI) ---

        private IPluginEditor? Editor => Instrument as IPluginEditor;
        public IPluginEditor? CurrentEditor => Editor;
        public bool HasEditor => Editor is { HasEditor: true };
        public bool IsEditorOpen => Editor is { IsEditorOpen: true };
        public string EditorButtonText => IsEditorOpen ? "Close plugin UI" : "Open plugin UI";

        public void NotifyEditorState()
        {
            OnPropertyChanged(nameof(IsEditorOpen));
            OnPropertyChanged(nameof(EditorButtonText));
        }

        public void PumpEditor() => Editor?.PumpEditor();

        private void RebuildParameters()
        {
            foreach (var vm in _subscribedParams) vm.PropertyChanged -= OnParameterChanged;
            _subscribedParams.Clear();

            Parameters.Clear();
            ParameterGroups.Clear();

            var order = new List<string>();
            var byGroup = new Dictionary<string, List<ParameterViewModel>>();
            foreach (var p in Instrument.Parameters)
            {
                var vm = ParameterViewModel.Create(p);
                Parameters.Add(vm);
                vm.PropertyChanged += OnParameterChanged;
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
    }
}
