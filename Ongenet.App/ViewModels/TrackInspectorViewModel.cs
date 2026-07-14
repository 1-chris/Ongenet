using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Modulation;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// Left-hand inspector for the selected track: name, mute/solo, volume, pan, colour, and sends.
    /// Edits mutate the underlying <see cref="Track"/> and publish a <see cref="TrackChangedEvent"/>
    /// so the timeline lane reflects them.
    /// </summary>
    public class TrackInspectorViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly IProjectService _project;
        private readonly IEventAggregator _events;
        private readonly ITransportService _transport;
        private readonly IHistoryService _history;
        private readonly IAudioEngine _engine;
        private readonly IInstrumentRegistry _instruments;
        private readonly IEffectRegistry _effects;
        private readonly IModulatorRegistry _modulators;

        public TrackInspectorViewModel(ISelectionService selection, IProjectService project,
            IEventAggregator events, ITransportService transport, IPlaybackClock clock, IHistoryService history,
            IAudioEngine engine, IInstrumentRegistry instruments, IEffectRegistry effects,
            IModulatorRegistry modulators, Panels.GrooveSettingsViewModel groove,
            Panels.PatternTrackInspectorViewModel patternTrackInspector, Panels.InstrumentRackViewModel instrumentRack)
        {
            _selection = selection;
            _project = project;
            _events = events;
            _transport = transport;
            _history = history;
            _engine = engine;
            _instruments = instruments;
            _effects = effects;
            _modulators = modulators;
            Groove = groove;
            PatternTrackInspector = patternTrackInspector;
            InstrumentRack = instrumentRack;
            _selection.SelectionChanged += OnSelectionChanged;
            _project.ProjectChanged += RebuildSends;
            _events.Subscribe<TracksChangedEvent>(_ => RebuildSends());
            _events.Subscribe<TrackChangedEvent>(e =>
            {
                if (ReferenceEquals(e.Track, Track)) OnSelectionChanged();
            });
            clock.Tick += OnPlaybackTick;

            AddSendCommand = new RelayCommand(AddSend, () => Track is not null && ReturnTracks.Count > 0);
            FreezeTrackCommand = new RelayCommand(FreezeTrack, () => Track is not null && !Track.IsBus && !Track.IsFrozen);
            UnfreezeTrackCommand = new RelayCommand(UnfreezeTrack, () => Track is not null && Track.IsFrozen);
            AddVolumeLfoCommand = new RelayCommand(AddVolumeLfo, () => Track is not null && !Track.IsBus && VolumeLfo is null);
            RemoveVolumeLfoCommand = new RelayCommand(RemoveVolumeLfo, () => VolumeLfo is not null);
            AddPanLfoCommand = new RelayCommand(AddPanLfo, () => Track is not null && !Track.IsBus && PanLfo is null);
            RemovePanLfoCommand = new RelayCommand(RemovePanLfo, () => PanLfo is not null);
        }

        private void OnPlaybackTick()
        {
            if (Track is null || _transport.State != TransportState.Playing) return;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(Pan));
            OnPropertyChanged(nameof(SurroundWidth));
        }

        private Track? Track => _selection.SelectedTrack;

        public bool HasTrack => Track is not null;

        public bool IsPatternTrack => Track?.Kind == TrackKind.Pattern;

        public bool IsInstrumentTrack => Track is { Kind: TrackKind.Instrument or TrackKind.Midi };

        public IReadOnlyList<DrumMap> DrumMaps => _project.Current.DrumMaps;

        public Guid? DrumMapId
        {
            get => Track?.DrumMapId;
            set
            {
                if (Track is null || Track.DrumMapId == value) return;
                _history.Capture("Set drum map");
                Track.DrumMapId = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public bool RouteToExternalMidi
        {
            get => Track?.RouteToExternalMidi ?? false;
            set
            {
                if (Track is null || Track.RouteToExternalMidi == value) return;
                _history.Capture("Toggle external MIDI");
                Track.RouteToExternalMidi = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public int ExternalMidiChannel
        {
            get => Track?.ExternalMidiChannel ?? 1;
            set
            {
                if (Track is null || Track.ExternalMidiChannel == value) return;
                Track.ExternalMidiChannel = Math.Clamp(value, 1, 16);
                OnPropertyChanged();
                Notify();
            }
        }

        public bool IsFrozen => Track?.IsFrozen ?? false;

        public bool HasSends => Sends.Count > 0;

        public ObservableCollection<TrackSendEditorViewModel> Sends { get; } = new();

        public ObservableCollection<Track> ReturnTracks { get; } = new();

        public RelayCommand AddSendCommand { get; }
        public RelayCommand FreezeTrackCommand { get; }
        public RelayCommand UnfreezeTrackCommand { get; }
        public RelayCommand AddVolumeLfoCommand { get; }
        public RelayCommand RemoveVolumeLfoCommand { get; }
        public RelayCommand AddPanLfoCommand { get; }
        public RelayCommand RemovePanLfoCommand { get; }

        private TrackModulator? VolumeLfo =>
            Track?.Modulators.FirstOrDefault(m =>
                m.Kind == TrackModulatorKind.Lfo &&
                m.Target.Kind == AutomationTargetKind.TrackVolume);

        private TrackModulator? PanLfo =>
            Track?.Modulators.FirstOrDefault(m =>
                m.Kind == TrackModulatorKind.Lfo &&
                m.Target.Kind == AutomationTargetKind.TrackPan);

        public bool HasVolumeLfo => VolumeLfo is not null;
        public bool HasPanLfo => PanLfo is not null;

        public double ModulationMacro
        {
            get => Track?.Modulators.Count > 0 ? Track.Modulators.Average(m => m.Depth) : 0;
            set
            {
                if (Track is null || Track.Modulators.Count == 0) return;
                foreach (var mod in Track.Modulators) mod.Depth = Math.Clamp(value, 0, 1);
                CommitModulators();
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumeLfoDepth));
                OnPropertyChanged(nameof(PanLfoDepth));
            }
        }

        public bool PanLfoEnabled
        {
            get => PanLfo?.Enabled ?? false;
            set
            {
                if (PanLfo is null) return;
                PanLfo.Enabled = value;
                CommitModulators();
                OnPropertyChanged();
            }
        }

        public double PanLfoRate
        {
            get => PanLfo?.RateHz ?? 0.25;
            set
            {
                if (PanLfo is null) return;
                PanLfo.RateHz = Math.Clamp(value, 0.01, 20);
                CommitModulators();
                OnPropertyChanged();
            }
        }

        public double PanLfoDepth
        {
            get => PanLfo?.Depth ?? 0.5;
            set
            {
                if (PanLfo is null) return;
                PanLfo.Depth = Math.Clamp(value, 0, 1);
                CommitModulators();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ModulationMacro));
            }
        }

        public bool VolumeLfoEnabled
        {
            get => VolumeLfo?.Enabled ?? false;
            set
            {
                if (VolumeLfo is null || VolumeLfo.Enabled == value) return;
                _history.Capture("Toggle volume LFO");
                VolumeLfo.Enabled = value;
                CommitModulators();
                OnPropertyChanged();
                Notify();
            }
        }

        public double VolumeLfoRate
        {
            get => VolumeLfo?.RateHz ?? 0.25;
            set
            {
                if (VolumeLfo is null || VolumeLfo.RateHz == value) return;
                VolumeLfo.RateHz = Math.Clamp(value, 0.01, 20);
                CommitModulators();
                OnPropertyChanged();
            }
        }

        public bool VolumeLfoTempoSync
        {
            get => VolumeLfo?.TempoSync ?? false;
            set
            {
                if (VolumeLfo is null || VolumeLfo.TempoSync == value) return;
                _history.Capture("Toggle LFO tempo sync");
                VolumeLfo.TempoSync = value;
                CommitModulators();
                OnPropertyChanged();
            }
        }

        public double SurroundFrontLeft
        {
            get => Track?.SurroundPan.FrontLeft ?? 1;
            set => SetSurroundPanField(p => p.FrontLeft = Math.Clamp(value, 0, 2));
        }

        public double SurroundFrontRight
        {
            get => Track?.SurroundPan.FrontRight ?? 1;
            set => SetSurroundPanField(p => p.FrontRight = Math.Clamp(value, 0, 2));
        }

        public double SurroundCenter
        {
            get => Track?.SurroundPan.Center ?? 0;
            set => SetSurroundPanField(p => p.Center = Math.Clamp(value, 0, 2));
        }

        public double SurroundLfe
        {
            get => Track?.SurroundPan.Lfe ?? 0;
            set => SetSurroundPanField(p => p.Lfe = Math.Clamp(value, 0, 2));
        }

        public double SurroundSideLeft
        {
            get => Track?.SurroundPan.SurroundLeft ?? 0;
            set => SetSurroundPanField(p => p.SurroundLeft = Math.Clamp(value, 0, 2));
        }

        public double SurroundSideRight
        {
            get => Track?.SurroundPan.SurroundRight ?? 0;
            set => SetSurroundPanField(p => p.SurroundRight = Math.Clamp(value, 0, 2));
        }

        public double SurroundRearLeft
        {
            get => Track?.SurroundPan.RearLeft ?? 0;
            set => SetSurroundPanField(p => p.RearLeft = Math.Clamp(value, 0, 2));
        }

        public double SurroundRearRight
        {
            get => Track?.SurroundPan.RearRight ?? 0;
            set => SetSurroundPanField(p => p.RearRight = Math.Clamp(value, 0, 2));
        }

        private void SetSurroundPanField(Action<SurroundChannelPan> apply)
        {
            if (Track is null) return;
            _history.Capture("Adjust surround pan");
            apply(Track.SurroundPan);
            Notify();
            OnPropertyChanged(nameof(SurroundFrontLeft));
            OnPropertyChanged(nameof(SurroundFrontRight));
            OnPropertyChanged(nameof(SurroundCenter));
            OnPropertyChanged(nameof(SurroundLfe));
            OnPropertyChanged(nameof(SurroundSideLeft));
            OnPropertyChanged(nameof(SurroundSideRight));
            OnPropertyChanged(nameof(SurroundRearRight));
            OnPropertyChanged(nameof(SurroundRearLeft));
        }

        public double VolumeLfoDepth
        {
            get => VolumeLfo?.Depth ?? 0.5;
            set
            {
                if (VolumeLfo is null || VolumeLfo.Depth == value) return;
                VolumeLfo.Depth = Math.Clamp(value, 0, 1);
                CommitModulators();
                OnPropertyChanged();
            }
        }

        public int VolumeLfoWave
        {
            get => VolumeLfo is null ? (int)LfoWave.Sine : (int)VolumeLfo.Wave;
            set
            {
                if (VolumeLfo is null) return;
                var wave = (LfoWave)Math.Clamp(value, 0, 3);
                if (VolumeLfo.Wave == wave) return;
                VolumeLfo.Wave = wave;
                CommitModulators();
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<string> LfoWaveNames { get; } = new[] { "Sine", "Triangle", "Saw", "Square" };

        public Panels.GrooveSettingsViewModel Groove { get; }

        public Panels.PatternTrackInspectorViewModel PatternTrackInspector { get; }
        public Panels.InstrumentRackViewModel InstrumentRack { get; }

        public IReadOnlyList<string> ColorKeys { get; } = new[]
        {
            "CatppuccinRed", "CatppuccinPeach", "CatppuccinYellow", "CatppuccinGreen",
            "CatppuccinTeal", "CatppuccinSky", "CatppuccinBlue", "CatppuccinMauve",
            "CatppuccinPink", "CatppuccinLavender"
        };

        public string Name
        {
            get => Track?.Name ?? string.Empty;
            set
            {
                if (Track is null || Track.Name == value) return;
                _history.Capture("Rename track");
                Track.Name = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public bool IsMuted
        {
            get => Track?.IsMuted ?? false;
            set
            {
                if (Track is null || Track.IsMuted == value) return;
                _history.Capture(value ? "Mute track" : "Unmute track");
                Track.IsMuted = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public bool IsSoloed
        {
            get => Track?.IsSoloed ?? false;
            set
            {
                if (Track is null || Track.IsSoloed == value) return;
                _history.Capture(value ? "Solo track" : "Unsolo track");
                Track.IsSoloed = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public double Volume
        {
            get => Track?.Volume ?? 0.0;
            set
            {
                if (Track is null || Track.Volume == value) return;
                Track.Volume = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public double Pan
        {
            get => Track?.Pan ?? 0.0;
            set
            {
                if (Track is null || Track.Pan == value) return;
                Track.Pan = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public double SurroundWidth
        {
            get => Track?.SurroundWidth ?? 1.0;
            set
            {
                if (Track is null || Track.SurroundWidth == value) return;
                Track.SurroundWidth = value;
                OnPropertyChanged();
            }
        }

        public string ColorKey
        {
            get => Track?.ColorKey ?? "CatppuccinMauve";
            set
            {
                if (Track is null || value is null || Track.ColorKey == value) return;
                _history.Capture("Change track colour");
                Track.ColorKey = value;
                OnPropertyChanged();
                Notify();
            }
        }

        private void AddSend()
        {
            if (Track is null || ReturnTracks.Count == 0) return;
            var target = ReturnTracks.FirstOrDefault(t => Track.Sends.All(s => s.TargetTrackId != t.Id))
                         ?? ReturnTracks[0];
            _history.Capture("Add send");
            Track.Sends.Add(new TrackSend { TargetTrackId = target.Id });
            RebuildSends();
            Notify();
        }

        private void FreezeTrack()
        {
            if (Track is null || Track.IsBus) return;
            _history.Capture("Freeze track");
            TrackFreezeService.FreezeTrack(_project.Current, Track, _engine.Format, _transport.Tempo.BeatsPerMinute);
            _events.Publish(new TracksChangedEvent());
            OnSelectionChanged();
        }

        private void UnfreezeTrack()
        {
            if (Track is null) return;
            _history.Capture("Unfreeze track");
            TrackFreezeService.UnfreezeTrack(Track);
            OnPropertyChanged(nameof(IsFrozen));
            FreezeTrackCommand.RaiseCanExecuteChanged();
            UnfreezeTrackCommand.RaiseCanExecuteChanged();
            Notify();
        }

        private void RebuildSends()
        {
            Sends.Clear();
            ReturnTracks.Clear();
            foreach (var t in _project.Current.Tracks.Where(t => t.Kind == TrackKind.Return))
                ReturnTracks.Add(t);

            if (Track is not null)
            {
                foreach (var send in Track.Sends)
                    Sends.Add(CreateSendEditor(send));
            }

            OnPropertyChanged(nameof(HasSends));
            AddSendCommand.RaiseCanExecuteChanged();
        }

        private TrackSendEditorViewModel CreateSendEditor(TrackSend send) =>
            new(Track!, send, _project, _history, Notify, RemoveSendEditor);

        private void RemoveSendEditor(TrackSendEditorViewModel editor)
        {
            if (Track is null) return;
            Track.Sends.Remove(editor.Send);
            RebuildSends();
            Notify();
        }

        private void Notify()
        {
            if (Track is not null) _events.Publish(new TrackChangedEvent(Track));
        }

        private void CommitModulators()
        {
            Track?.CommitModulators();
        }

        private void AddVolumeLfo()
        {
            if (Track is null || Track.IsBus || VolumeLfo is not null) return;
            _history.Capture("Add volume LFO");
            Track.Modulators.Add(new TrackModulator
            {
                Kind = TrackModulatorKind.Lfo,
                Target = new AutomationBinding(AutomationTargetKind.TrackVolume, -1, -1)
            });
            CommitModulators();
            RefreshModulatorBindings();
            Notify();
        }

        private void RemoveVolumeLfo()
        {
            if (Track is null || VolumeLfo is null) return;
            _history.Capture("Remove volume LFO");
            Track.Modulators.Remove(VolumeLfo);
            CommitModulators();
            RefreshModulatorBindings();
            Notify();
        }

        private void AddPanLfo()
        {
            if (Track is null || Track.IsBus || PanLfo is not null) return;
            _history.Capture("Add pan LFO");
            Track.Modulators.Add(new TrackModulator
            {
                Kind = TrackModulatorKind.Lfo,
                Target = new AutomationBinding(AutomationTargetKind.TrackPan, -1, -1)
            });
            CommitModulators();
            RefreshModulatorBindings();
            Notify();
        }

        private void RemovePanLfo()
        {
            if (Track is null || PanLfo is null) return;
            _history.Capture("Remove pan LFO");
            Track.Modulators.Remove(PanLfo);
            CommitModulators();
            RefreshModulatorBindings();
            Notify();
        }

        private void RefreshModulatorBindings()
        {
            OnPropertyChanged(nameof(HasVolumeLfo));
            OnPropertyChanged(nameof(VolumeLfoEnabled));
            OnPropertyChanged(nameof(VolumeLfoRate));
            OnPropertyChanged(nameof(VolumeLfoTempoSync));
            OnPropertyChanged(nameof(VolumeLfoDepth));
            OnPropertyChanged(nameof(VolumeLfoWave));
            OnPropertyChanged(nameof(HasPanLfo));
            OnPropertyChanged(nameof(PanLfoEnabled));
            OnPropertyChanged(nameof(PanLfoRate));
            OnPropertyChanged(nameof(PanLfoDepth));
            OnPropertyChanged(nameof(ModulationMacro));
            AddVolumeLfoCommand.RaiseCanExecuteChanged();
            RemoveVolumeLfoCommand.RaiseCanExecuteChanged();
            AddPanLfoCommand.RaiseCanExecuteChanged();
            RemovePanLfoCommand.RaiseCanExecuteChanged();
        }

        /// <summary>Appends modulator slots from a factory/user modulator-chain preset onto the selected track.</summary>
        public void ApplyModulatorChainPreset(string presetPath)
        {
            if (Track is not { } track) return;
            try
            {
                using var fs = File.OpenRead(presetPath);
                var loaded = PresetFile.Load(fs, _instruments, _effects, _modulators);
                if (loaded?.ModulatorSlots is not { Count: > 0 } slots) return;
                _history.Capture("Load modulator chain");
                foreach (var slot in slots)
                {
                    track.ModulatorSlots.Add(new ModulatorSlot
                    {
                        Enabled = slot.Enabled,
                        Depth = slot.Depth,
                        Source = ModulatorCloner.Clone(slot.Source, _modulators),
                        Target = slot.Target
                    });
                }

                track.CommitModulatorSlots();
                _events.Publish(new TrackChangedEvent(track));
            }
            catch { /* ignore invalid preset */ }
        }

        private void OnSelectionChanged()
        {
            InstrumentRack.BindTrack(Track);
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(IsPatternTrack));
            OnPropertyChanged(nameof(IsInstrumentTrack));
            OnPropertyChanged(nameof(DrumMapId));
            OnPropertyChanged(nameof(RouteToExternalMidi));
            OnPropertyChanged(nameof(ExternalMidiChannel));
            OnPropertyChanged(nameof(IsFrozen));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(IsSoloed));
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(Pan));
            OnPropertyChanged(nameof(SurroundWidth));
            OnPropertyChanged(nameof(ColorKey));
            RefreshModulatorBindings();
            RebuildSends();
            FreezeTrackCommand.RaiseCanExecuteChanged();
            UnfreezeTrackCommand.RaiseCanExecuteChanged();
        }
    }
}
