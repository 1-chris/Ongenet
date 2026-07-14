using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Containers;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Controls.Engine3D;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.Effects;
using Ongenet.App.ViewModels.Field;

namespace Ongenet.App.ViewModels.Instruments
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
        private readonly Action<InstrumentSlotViewModel, string, bool> _insertRelative; // (target, instrumentId, below)
        private readonly Action<InstrumentSlotViewModel, string> _replaceWith;          // (target, instrumentId)
        private readonly Action<InstrumentSlotViewModel, string, bool> _insertPresetRelative; // (target, presetPath, below)
        private readonly Action<InstrumentSlotViewModel, string> _replacePresetWith;          // (target, presetPath)
        private readonly Action<InstrumentSlotViewModel, string, bool> _insertSoundFontRelative; // (target, sfPath, below)
        private readonly Action<InstrumentSlotViewModel, string> _replaceSoundFontWith;          // (target, sfPath)
        private readonly IProjectService _project;
        private readonly Guid _ownerTrackId;

        private readonly DispatcherTimer _previewTimer;
        private readonly List<ParameterViewModel> _subscribedParams = new();
        private float[] _previewBuffer = Array.Empty<float>();
        private bool _isSelected;

        public event Action? SelectRequested;

        public InstrumentSlotViewModel(InstrumentSlot slot, Guid ownerTrackId, IProjectService project,
            IAudioFileService audioFiles,
            ITransportService transport, IHistoryService history, IEffectRegistry effects,
            IInstrumentRegistry instruments, IPlaybackClock clock,
            Action notifyChanged, Action<InstrumentSlotViewModel> remove, Action<InstrumentSlotViewModel, int> move,
            Action<InstrumentSlotViewModel, string, bool> insertRelative, Action<InstrumentSlotViewModel, string> replaceWith,
            Action<InstrumentSlotViewModel, string, bool> insertPresetRelative, Action<InstrumentSlotViewModel, string> replacePresetWith,
            Action<InstrumentSlotViewModel, string, bool> insertSoundFontRelative, Action<InstrumentSlotViewModel, string> replaceSoundFontWith)
        {
            _slot = slot;
            _ownerTrackId = ownerTrackId;
            _project = project;
            _audioFiles = audioFiles;
            _transport = transport;
            _history = history;
            _notifyChanged = notifyChanged;
            _remove = remove;
            _move = move;
            _insertRelative = insertRelative;
            _replaceWith = replaceWith;
            _insertPresetRelative = insertPresetRelative;
            _replacePresetWith = replacePresetWith;
            _insertSoundFontRelative = insertSoundFontRelative;
            _replaceSoundFontWith = replaceSoundFontWith;

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _previewTimer.Tick += OnPreviewTimerTick;

            Effects = new EffectChainViewModel(slot.Effects, slot.CommitEffects, notifyChanged,
                effects, history, transport, clock);

            if (Instrument is ContainerInstrumentBase containerBase)
            {
                Container = new ContainerInstrumentViewModel(containerBase, instruments, history, audioFiles,
                    effects, transport, clock, notifyChanged);
                Container.RebuildAddable();
            }

            RemoveCommand = new RelayCommand(() => _remove(this));
            ToggleEnabledCommand = new RelayCommand(() => IsEnabled = !IsEnabled);
            MoveUpCommand = new RelayCommand(() => _move(this, -1));
            MoveDownCommand = new RelayCommand(() => _move(this, +1));
            OpenZoneEditorCommand = new RelayCommand(OpenZoneEditor, () => IsSoundFont);

            clock.Tick += OnPlaybackTick;
            RebuildParameters();
            RebuildOutputRoutes();
            RenderPreview();
        }

        /// <summary>Plugin output bus index (0 = default stereo mix).</summary>
        public int OutputBusIndex
        {
            get => _slot.OutputBusIndex;
            set
            {
                if (_slot.OutputBusIndex == value) return;
                _history.Capture("Change output bus");
                _slot.OutputBusIndex = value;
                SyncMultiOutputRoute();
                OnPropertyChanged();
                _notifyChanged();
            }
        }

        /// <summary>When set, routes this slot to another track instead of the owner.</summary>
        public Guid? OutputTrackId
        {
            get => _slot.OutputTrackId;
            set
            {
                if (_slot.OutputTrackId == value) return;
                _history.Capture("Change output route");
                _slot.OutputTrackId = value;
                SyncMultiOutputRoute();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedOutputTrack));
                _notifyChanged();
            }
        }

        private void SyncMultiOutputRoute()
        {
            _project.Current.MultiOutputRoutes.RemoveAll(r =>
                r.SourceTrackId == _ownerTrackId && r.PluginOutputBus == _slot.OutputBusIndex);
            if (_slot.OutputTrackId is { } destId)
            {
                _project.Current.MultiOutputRoutes.Add(new MultiOutputRoute
                {
                    SourceTrackId = _ownerTrackId,
                    PluginOutputBus = _slot.OutputBusIndex,
                    DestinationTrackId = destId
                });
            }
        }

        public ObservableCollection<OutputRouteOption> OutputRouteOptions { get; } = new();

        public OutputRouteOption? SelectedOutputTrack
        {
            get => OutputRouteOptions.FirstOrDefault(o => o.TrackId == OutputTrackId);
            set => OutputTrackId = value?.TrackId;
        }

        private void RebuildOutputRoutes()
        {
            OutputRouteOptions.Clear();
            OutputRouteOptions.Add(new OutputRouteOption(null, "(owner track)"));
            foreach (var track in _project.Current.Tracks)
            {
                if (track.Id == _ownerTrackId || track.Kind == TrackKind.Master) continue;
                OutputRouteOptions.Add(new OutputRouteOption(track.Id, track.Name));
            }
            OnPropertyChanged(nameof(SelectedOutputTrack));
        }

        public InstrumentSlot Slot => _slot;
        public IInstrument Instrument => _slot.Instrument;
        public string InstrumentName => Instrument.Name;

        /// <summary>The slot's own (pre) effect chain editor.</summary>
        public EffectChainViewModel Effects { get; }

        /// <summary>Nested instrument editor when this slot hosts a container device.</summary>
        public ContainerInstrumentViewModel? Container { get; }

        public bool IsContainer => Instrument is ContainerInstrumentBase;

        public bool IsXyInstrument => Instrument is XyInstrument;

        public RelayCommand RemoveCommand { get; }
        public RelayCommand ToggleEnabledCommand { get; }
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }
        public RelayCommand OpenZoneEditorCommand { get; }

        public bool IsFirst { get; set; }
        public bool IsLast { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public void RequestSelect() => SelectRequested?.Invoke();

        /// <summary>Which edit a drag dropped onto this card performs, by the pointer's vertical zone.</summary>
        public enum RackDropZone { Above, Replace, Below }

        private string _presetName = string.Empty;

        /// <summary>The name typed into the "Save preset" flyout.</summary>
        public string PresetName
        {
            get => _presetName;
            set => SetField(ref _presetName, value);
        }

        /// <summary>Saves this instrument's current settings as a user <c>.ongenpreset</c>.</summary>
        public void SaveAsPreset()
        {
            var name = string.IsNullOrWhiteSpace(_presetName) ? Instrument.Name : _presetName.Trim();
            App.ServiceProvider?.GetService<IPresetLibrary>()?.SaveInstrument(Instrument, name);
            PresetName = string.Empty;
        }

        /// <summary>Applies a library instrument drop onto this card (insert above/below or replace).</summary>
        public void DropInstrument(string instrumentId, RackDropZone zone)
        {
            if (string.IsNullOrEmpty(instrumentId)) return;
            switch (zone)
            {
                case RackDropZone.Above: _insertRelative(this, instrumentId, false); break;
                case RackDropZone.Below: _insertRelative(this, instrumentId, true); break;
                default: _replaceWith(this, instrumentId); break;
            }
        }

        /// <summary>Applies an instrument-preset drop onto this card (insert above/below or replace).</summary>
        public void DropPreset(string presetPath, RackDropZone zone)
        {
            if (string.IsNullOrEmpty(presetPath)) return;
            switch (zone)
            {
                case RackDropZone.Above: _insertPresetRelative(this, presetPath, false); break;
                case RackDropZone.Below: _insertPresetRelative(this, presetPath, true); break;
                default: _replacePresetWith(this, presetPath); break;
            }
        }

        /// <summary>Applies a sound-font drop onto this card: insert a sampler above/below, or in the replace
        /// zone load into this card when it is already a Sampler, else replace it with a sampler.</summary>
        public bool DropSoundFont(string path, RackDropZone zone)
        {
            if (string.IsNullOrEmpty(path)) return false;
            switch (zone)
            {
                case RackDropZone.Above: _insertSoundFontRelative(this, path, false); break;
                case RackDropZone.Below: _insertSoundFontRelative(this, path, true); break;
                default:
                    if (IsSoundFont) LoadSamplerFromPath(path);   // already a Sampler — just load
                    else _replaceSoundFontWith(this, path);        // swap this instrument for a sampler + load
                    break;
            }
            return true;
        }

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
                if (Instrument is FieldInstrument)
                {
                    _fieldEditor?.ReloadSurfaceFromHost();
                    _fieldSurface = null;
                    _forceFieldEditor = false;
                    OnPropertyChanged(nameof(HasCustomFieldSurface));
                    OnPropertyChanged(nameof(ShowFieldEditor));
                    OnPropertyChanged(nameof(ShowFieldSurface));
                    OnPropertyChanged(nameof(ShowGenericParameters));
                    OnPropertyChanged(nameof(FieldSurface));
                    OnPropertyChanged(nameof(FieldEditorToggleText));
                }
                RebuildParameters();
                RenderPreview();
                OnPropertyChanged(nameof(SamplerStatus));
                OnPropertyChanged(nameof(InstrumentName));
                OnPropertyChanged(nameof(SamplerPresetNames));
                OnPropertyChanged(nameof(HasSamplerPresets));
                OnPropertyChanged(nameof(SelectedSamplerPreset));
                NotifySamplerVisuals();
                OpenZoneEditorCommand.RaiseCanExecuteChanged();
            }
        }

        // --- Sampler support ---

        private ISampleHost? SampleHost => Instrument as ISampleHost;
        public bool IsSampler => SampleHost is not null;
        public string SampleName => SampleHost?.SampleName ?? "(no sample loaded)";

        // --- Wavetable synth support (bespoke 3D inspector + built-in table presets) ---

        private WavetableInstrument? Wavetable => Instrument as WavetableInstrument;
        public bool IsWavetable => Wavetable is not null;

        /// <summary>Builds a fresh 3D visualization bound to this instrument's table (one per hosted view).</summary>
        public Func<IEngine3DVisualization>? WavetableVisualizationFactory =>
            Wavetable is { } wt ? () => new Wavetable3DVisualization(wt) : null;

        private RelayCommand? _wtBasic, _wtHarmonics, _wtRandom;
        public RelayCommand WavetableBasicCommand => _wtBasic ??= new RelayCommand(() => SetWavetablePreset(WavetablePreset.Basic));
        public RelayCommand WavetableHarmonicsCommand => _wtHarmonics ??= new RelayCommand(() => SetWavetablePreset(WavetablePreset.Harmonics));
        public RelayCommand WavetableRandomCommand => _wtRandom ??= new RelayCommand(() => SetWavetablePreset(WavetablePreset.Random));

        private void SetWavetablePreset(WavetablePreset preset)
        {
            if (Wavetable is not { } wt) return;
            _history.Capture("Wavetable preset");
            wt.LoadPreset(preset);
            OnPropertyChanged(nameof(SampleName));
        }

        // --- "Sampler" support (SFZ + SF2 sound fonts) ---

        private SamplerInstrument? SamplerInst => Instrument as SamplerInstrument;
        public bool IsSoundFont => SamplerInst is not null;

        private bool _samplerLoading;
        private double _samplerLoadProgress;

        public string SamplerStatus => _samplerLoading
            ? $"Loading… {_samplerLoadProgress * 100:0}%"
            : SamplerInst is { } s
                ? s.LayerCount == 0
                    ? "(no instrument loaded)"
                    : s.LayerCount == 1
                        ? $"{System.IO.Path.GetFileName(s.SourcePath)} — {s.Regions.Count} region(s)"
                        : $"{s.LayerCount} layers · {s.Regions.Count} regions"
                : "(no instrument loaded)";

        public bool IsSamplerLoading => _samplerLoading;
        public double SamplerLoadProgress => _samplerLoadProgress;

        /// <summary>Replaces all layers with one <c>.sfz</c> / <c>.sf2</c> file.</summary>
        public void LoadSamplerFromPath(string path) => _ = RunSamplerLoad(path, replace: true, "Load instrument");

        /// <summary>Appends an <c>.sfz</c> / <c>.sf2</c> as an additional stacked layer.</summary>
        public void AddSamplerLayerFromPath(string path) => _ = RunSamplerLoad(path, replace: false, "Add layer");

        // --- SF2 preset selection (first layer) ---

        public IReadOnlyList<string> SamplerPresetNames =>
            SamplerInst is not { } s || s.Presets.Count == 0
                ? Array.Empty<string>()
                : FormatSf2PresetsHierarchical(s.Presets);

        /// <summary>Bank-aware SF2 preset labels (1:1 with <see cref="SamplerInstrument.Presets"/> indices).</summary>
        public static IReadOnlyList<string> FormatSf2PresetsHierarchical(IReadOnlyList<SamplerPresetInfo> presets)
            => presets.Select(p => $"Bank {p.Bank} · {p.Program:D3}  {p.Name}").ToList();

        public bool HasSamplerPresets => SamplerPresetNames.Count > 0;

        public int SelectedSamplerPreset
        {
            get => SamplerInst?.PresetIndex ?? -1;
            set
            {
                if (SamplerInst is not { } s || _samplerLoading) return;
                if (value < 0 || value == s.PresetIndex || s.SourcePath.Length == 0) return;
                _ = RunFirstLayerPresetChange(value);
            }
        }

        private async Task RunFirstLayerPresetChange(int presetIndex)
        {
            if (SamplerInst is not { } sampler || _samplerLoading) return;
            _samplerLoading = true;
            _samplerLoadProgress = 0;
            OnPropertyChanged(nameof(SamplerStatus));
            OnPropertyChanged(nameof(IsSamplerLoading));
            OnPropertyChanged(nameof(SamplerLoadProgress));

            var progress = new Progress<double>(p =>
            {
                _samplerLoadProgress = p;
                OnPropertyChanged(nameof(SamplerLoadProgress));
                OnPropertyChanged(nameof(SamplerStatus));
            });
            var result = await Task.Run(() => sampler.LoadFirstLayerSf2Program(presetIndex, progress));

            _samplerLoading = false;
            OnPropertyChanged(nameof(IsSamplerLoading));
            if (result is null) { OnPropertyChanged(nameof(SamplerStatus)); return; }

            _history.Capture("Change preset");
            _notifyChanged();
            RebuildParameters();
            OnPropertyChanged(nameof(SamplerStatus));
            OnPropertyChanged(nameof(InstrumentName));
            OnPropertyChanged(nameof(SamplerPresetNames));
            OnPropertyChanged(nameof(HasSamplerPresets));
            OnPropertyChanged(nameof(SelectedSamplerPreset));
            NotifySamplerVisuals();
        }

        private async Task RunSamplerLoad(string path, bool replace, string historyLabel)
        {
            if (SamplerInst is not { } sampler || _samplerLoading) return;
            var loader = App.ServiceProvider?.GetService<ISamplerLoadService>();
            if (loader is null) return;

            _samplerLoading = true;
            _samplerLoadProgress = 0;
            OnPropertyChanged(nameof(SamplerStatus));
            OnPropertyChanged(nameof(IsSamplerLoading));
            OnPropertyChanged(nameof(SamplerLoadProgress));

            var progress = new Progress<double>(p =>
            {
                _samplerLoadProgress = p;
                OnPropertyChanged(nameof(SamplerLoadProgress));
                OnPropertyChanged(nameof(SamplerStatus));
            });
            var result = await Task.Run(() => loader.Load(path, -1, progress));

            _samplerLoading = false;
            OnPropertyChanged(nameof(IsSamplerLoading));
            if (result is null) { OnPropertyChanged(nameof(SamplerStatus)); return; }

            _history.Capture(historyLabel);
            if (replace) sampler.ApplyLoad(result);
            else sampler.AddLayer(result);
            _notifyChanged(); // a fresh patch needs the engine to (re)prepare the instrument
            RebuildParameters();
            OnPropertyChanged(nameof(SamplerStatus));
            OnPropertyChanged(nameof(InstrumentName));
            OnPropertyChanged(nameof(SamplerPresetNames));
            OnPropertyChanged(nameof(HasSamplerPresets));
            OnPropertyChanged(nameof(SelectedSamplerPreset));
            NotifySamplerVisuals();
            OpenZoneEditorCommand.RaiseCanExecuteChanged();

            if (sampler.LastLoadWarnings.Count > 0)
                SamplerLoadWarnings?.Invoke(sampler.LastLoadWarnings);
        }

        /// <summary>Raised after a load/add-layer when the loader emitted warnings.</summary>
        public event Action<IReadOnlyList<string>>? SamplerLoadWarnings;

        public IReadOnlyList<SamplerRegion> SamplerZones => SamplerInst?.Regions ?? Array.Empty<SamplerRegion>();
        public bool HasZones => SamplerZones.Count > 0;

        private void OpenZoneEditor()
        {
            if (SamplerInst is null) return;
            var vm = new SamplerZoneEditorViewModel();
            vm.Load(SamplerInst);
            var win = new Views.Windows.SamplerZoneEditorWindow { DataContext = vm };
            win.Closed += (_, _) =>
            {
                OnPropertyChanged(nameof(SamplerStatus));
                OnPropertyChanged(nameof(InstrumentName));
                NotifySamplerVisuals();
            };
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is not null)
                win.Show(desktop.MainWindow);
            else
                win.Show();
        }

        private int _samplerRevision;
        public int SamplerRevision => _samplerRevision;

        private SamplerRegion? FirstZone => SamplerInst is { Regions.Count: > 0 } s ? s.Regions[0] : null;
        public double EnvDelay => FirstZone?.AmpEg.Delay ?? 0;
        public double EnvAttack => FirstZone?.AmpEg.Attack ?? 0;
        public double EnvHold => FirstZone?.AmpEg.Hold ?? 0;
        public double EnvDecay => FirstZone?.AmpEg.Decay ?? 0;
        public double EnvSustain => FirstZone?.AmpEg.Sustain ?? 1.0;
        public double EnvRelease => FirstZone?.AmpEg.Release ?? 0;

        private void NotifySamplerVisuals()
        {
            _samplerRevision++;
            OnPropertyChanged(nameof(SamplerZones));
            OnPropertyChanged(nameof(HasZones));
            OnPropertyChanged(nameof(SamplerRevision));
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

        // --- Field modular instrument (embedded node-graph editor / custom surface) ---

        private FieldEditorViewModel? _fieldEditor;
        private FieldSurfaceViewModel? _fieldSurface;
        private bool _forceFieldEditor;
        private RelayCommand? _toggleFieldEditorCommand;

        public bool IsField => Instrument is FieldInstrument;
        public bool HasCustomFieldSurface => Instrument is FieldInstrument { HasCustomSurface: true };

        /// <summary>True when the graph editor should be shown (sandbox Field, or after Edit Graph).</summary>
        public bool ShowFieldEditor => IsField && (_forceFieldEditor || !HasCustomFieldSurface);

        /// <summary>True when the authored custom surface should be shown in the rack card.</summary>
        public bool ShowFieldSurface => IsField && HasCustomFieldSurface && !_forceFieldEditor;

        /// <summary>
        /// Generic parameter strip. Hidden for Field hosts that already have a custom surface — those
        /// exposed controls are rendered by the surface itself (showing both would duplicate every knob).
        /// Organ/Phase-4 use bespoke panels for their primary controls.
        /// </summary>
        public bool ShowGenericParameters => !(IsField && HasCustomFieldSurface) && !IsOrgan && !IsPhase4 &&
            !IsFmSynth && !IsXyInstrument;

        public RelayCommand ToggleFieldEditorCommand => _toggleFieldEditorCommand ??= new(() =>
        {
            _forceFieldEditor = !_forceFieldEditor;
            OnPropertyChanged(nameof(ShowFieldEditor));
            OnPropertyChanged(nameof(ShowFieldSurface));
            OnPropertyChanged(nameof(ShowGenericParameters));
            OnPropertyChanged(nameof(FieldEditorToggleText));
            if (!_forceFieldEditor) RebuildParameters();
        });

        public string FieldEditorToggleText => _forceFieldEditor ? "Show interface" : "Edit graph";

        /// <summary>The node-graph editor for the Field instrument, or null for other instruments.</summary>
        public FieldEditorViewModel? FieldEditor
        {
            get
            {
                if (Instrument is not FieldInstrument fi) return null;
                return _fieldEditor ??= new FieldEditorViewModel(fi.Graph,
                    App.ServiceProvider?.GetService<IFieldNodeRegistry>() ?? new FieldNodeRegistry(),
                    () => { fi.Recompile(); RebuildParameters(); }, fi.PresetNames,
                    i => { fi.LoadPreset(i); RebuildParameters(); }, () => fi.Compiled, isInstrument: true,
                    instrumentHost: () => fi);
            }
        }

        /// <summary>Playback surface for a user Field instrument with a custom UI.</summary>
        public FieldSurfaceViewModel? FieldSurface
        {
            get
            {
                if (Instrument is not FieldInstrument fi || !fi.HasCustomSurface) return null;
                return _fieldSurface ??= new FieldSurfaceViewModel(fi.Graph, fi.Surface,
                    () => { fi.SetSurface(_fieldSurface!.Surface); RebuildParameters(); });
            }
        }

        // --- Granular support (grain monitor) ---

        public bool IsGranular => Instrument is GranularInstrument;
        public GrainMonitor? GrainMonitor => (Instrument as GranularInstrument)?.Monitor;

        // --- Organ drawbar panel ---

        private OrganInstrument? OrganInst => Instrument as OrganInstrument;
        public bool IsOrgan => OrganInst is not null;

        public IReadOnlyList<ParameterViewModel> OrganDrawbars =>
            IsOrgan
                ? Parameters.Where(p => p.Group == "Drawbars").ToList()
                : Array.Empty<ParameterViewModel>();

        public ObservableCollection<ParameterGroupViewModel> NonDrawbarParameterGroups { get; } = new();

        // --- Phase-4 operator macro panel ---

        private Phase4Instrument? Phase4Inst => Instrument as Phase4Instrument;
        public bool IsPhase4 => Phase4Inst is not null;

        public IReadOnlyList<ParameterViewModel> Phase4Op1Params => Phase4GroupParams("Op 1");
        public IReadOnlyList<ParameterViewModel> Phase4Op2Params => Phase4GroupParams("Op 2");
        public IReadOnlyList<ParameterViewModel> Phase4Op3Params => Phase4GroupParams("Op 3");
        public IReadOnlyList<ParameterViewModel> Phase4Op4Params => Phase4GroupParams("Op 4");

        public ObservableCollection<ParameterGroupViewModel> Phase4TailParameterGroups { get; } = new();

        // --- FM Synth four-operator panel ---

        private FmSynthInstrument? FmInst => Instrument as FmSynthInstrument;
        public bool IsFmSynth => FmInst is not null;

        public IReadOnlyList<ParameterViewModel> FmOp1Params => FmGroupParams("Op 1");
        public IReadOnlyList<ParameterViewModel> FmOp2Params => FmGroupParams("Op 2");
        public IReadOnlyList<ParameterViewModel> FmOp3Params => FmGroupParams("Op 3");
        public IReadOnlyList<ParameterViewModel> FmOp4Params => FmGroupParams("Op 4");

        public ObservableCollection<ParameterGroupViewModel> FmTailParameterGroups { get; } = new();

        private IReadOnlyList<ParameterViewModel> FmGroupParams(string group)
            => IsFmSynth ? Parameters.Where(p => p.Group == group).ToList() : Array.Empty<ParameterViewModel>();

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
            {
                if (IsOrgan && key == "Drawbars") continue;
                if (IsPhase4 && key is "Op 1" or "Op 2" or "Op 3" or "Op 4") continue;
                if (IsFmSynth && key is "Op 1" or "Op 2" or "Op 3" or "Op 4") continue;
                ParameterGroups.Add(new ParameterGroupViewModel(key, byGroup[key]));
            }

            RebuildSpecializedParameterGroups(byGroup, order);
            NotifySpecializedInstrumentPanels();
        }

        private void NotifySpecializedInstrumentPanels()
        {
            OnPropertyChanged(nameof(IsOrgan));
            OnPropertyChanged(nameof(IsPhase4));
            OnPropertyChanged(nameof(IsFmSynth));
            OnPropertyChanged(nameof(OrganDrawbars));
            OnPropertyChanged(nameof(Phase4Op1Params));
            OnPropertyChanged(nameof(Phase4Op2Params));
            OnPropertyChanged(nameof(Phase4Op3Params));
            OnPropertyChanged(nameof(Phase4Op4Params));
            OnPropertyChanged(nameof(FmOp1Params));
            OnPropertyChanged(nameof(FmOp2Params));
            OnPropertyChanged(nameof(FmOp3Params));
            OnPropertyChanged(nameof(FmOp4Params));
        }

        private void RebuildSpecializedParameterGroups(Dictionary<string, List<ParameterViewModel>> byGroup,
            List<string> order)
        {
            NonDrawbarParameterGroups.Clear();
            Phase4TailParameterGroups.Clear();
            FmTailParameterGroups.Clear();

            if (IsOrgan)
            {
                foreach (var key in order)
                {
                    if (key == "Drawbars") continue;
                    NonDrawbarParameterGroups.Add(new ParameterGroupViewModel(key, byGroup[key]));
                }
            }

            if (IsPhase4)
            {
                foreach (var key in order)
                {
                    if (key is "Op 1" or "Op 2" or "Op 3" or "Op 4") continue;
                    Phase4TailParameterGroups.Add(new ParameterGroupViewModel(key, byGroup[key]));
                }
            }

            if (IsFmSynth)
            {
                foreach (var key in order)
                {
                    if (key is "Op 1" or "Op 2" or "Op 3" or "Op 4") continue;
                    FmTailParameterGroups.Add(new ParameterGroupViewModel(key, byGroup[key]));
                }
            }
        }

        private IReadOnlyList<ParameterViewModel> Phase4GroupParams(string group)
            => IsPhase4 ? Parameters.Where(p => p.Group == group).ToList() : Array.Empty<ParameterViewModel>();

        private void OnParameterChanged(object? sender, PropertyChangedEventArgs e) => SchedulePreview();
    }

    public sealed class OutputRouteOption
    {
        public OutputRouteOption(Guid? trackId, string name)
        {
            TrackId = trackId;
            Name = name;
        }

        public Guid? TrackId { get; }
        public string Name { get; }
    }
}
