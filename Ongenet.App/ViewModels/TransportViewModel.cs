using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Music;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Core.Services;
using Ongenet.App.Services;
using Ongenet.App.Localization;
using Ongenet.Link;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// Top-bar transport: Play/Stop, tempo, arrangement length (bars), time readouts and the master
    /// loudness meter. Backed by <see cref="ITransportService"/> and the <see cref="IAudioEngine"/>.
    /// </summary>
    public class TransportViewModel : ViewModelBase
    {
        private readonly ITransportService _transport;
        private readonly IAudioEngine _engine;
        private readonly IProjectService _project;
        private readonly IEventAggregator _events;
        private readonly IEditModeService _editMode;
        private readonly ExportService _export;
        private readonly IRecordingService _recording;
        private readonly ISystemMetricsSampler _metrics;
        private readonly ILinkSession _link;
        private readonly IPlaybackModeService _playback;
        private readonly TimelineViewModel _timeline;
        private readonly IMidiInputService _midi;
        private readonly IAppSettingsService _appSettings;
        private readonly ISessionCaptureService _capture;
        private readonly IMasteringDeliveryTarget _deliveryTarget;
        private bool _isRendering;
        private bool _syncingLinkTempo;
        private readonly Queue<long> _tapTimes = new();

        public TransportViewModel(ITransportService transport, IAudioEngine engine,
            IProjectService project, IEventAggregator events, IEditModeService editMode,
            ExportService export, IRecordingService recording, AudioDevicesViewModel devices,
            ISystemMetricsSampler metrics, ILinkSession link, IPlaybackModeService playback,
            TimelineViewModel timeline, IMidiInputService midi, IAppSettingsService appSettings,
            ISessionCaptureService capture, IMasteringDeliveryTarget deliveryTarget)
        {
            _transport = transport;
            _engine = engine;
            _project = project;
            _events = events;
            _editMode = editMode;
            _export = export;
            _recording = recording;
            _metrics = metrics;
            _link = link;
            _playback = playback;
            _timeline = timeline;
            _midi = midi;
            _appSettings = appSettings;
            _capture = capture;
            _deliveryTarget = deliveryTarget;
            Devices = devices;

            _metrics.Updated += OnMetricsUpdated;
            _link.SyncChanged += OnLinkSyncChanged;

            _transport.StateChanged += OnTransportStateChanged;
            _transport.TempoChanged += _ => OnTempoChanged();
            _editMode.ModeChanged += () => OnPropertyChanged(nameof(IsSliceMode));
            // Recording state may flip from the audio thread (count-in finishing) — marshal to UI.
            _recording.StateChanged += () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(OnRecordingStateChanged);

            PlayCommand = new RelayCommand(_transport.Play);
            StopCommand = new RelayCommand(OnStop);
            RecordCommand = new RelayCommand(_recording.StartRecording);
            TapTempoCommand = new RelayCommand(TapTempo);
            SetLoopStartCommand = new RelayCommand(() => _transport.LoopStart = _transport.StartBeat);
            SetLoopEndCommand = new RelayCommand(() => _transport.LoopEnd = _transport.StartBeat);
            SetPunchInCommand = new RelayCommand(() => _transport.PunchInBeat = _transport.StartBeat);
            SetPunchOutCommand = new RelayCommand(() => _transport.PunchOutBeat = _transport.StartBeat);
            ClearPunchCommand = new RelayCommand(ClearPunch);
            AddMarkerAtPlayheadCommand = new RelayCommand(AddMarkerAtPlayhead);
            GoToNextMarkerCommand = new RelayCommand(GoToNextMarker, () => _project.Current.Markers.Count > 0);
            GoToPreviousMarkerCommand = new RelayCommand(GoToPreviousMarker, () => _project.Current.Markers.Count > 0);
            CaptureRetrospectiveMidiCommand = new RelayCommand(() => _timeline.CaptureRetrospectiveMidi());
            ResetMasterLoudnessCommand = new RelayCommand(() =>
            {
                _engine.ResetMasterLoudness();
                OnPropertyChanged(nameof(MasterLoudnessText));
                OnPropertyChanged(nameof(MasterLoudnessRangeLu));
                OnPropertyChanged(nameof(MasterTruePeakMaxDbTp));
                OnPropertyChanged(nameof(MasterTruePeakWarning));
            });
            CycleKSystemCommand = new RelayCommand(() =>
            {
                _kSystemIndex = (_kSystemIndex + 1) % 4;
                OnPropertyChanged(nameof(KSystemOffsetDb));
                OnPropertyChanged(nameof(KSystemLabel));
            });

            _transport.LoopChanged += () => OnPropertyChanged(nameof(IsLooping));
            _transport.PunchChanged += OnPunchChanged;
            _transport.MetronomeChanged += () => OnPropertyChanged(nameof(MetronomeEnabled));
            _playback.ModeChanged += OnPlaybackModeChanged;
            _capture.SessionRecordArmedChanged += OnSessionRecordArmedChanged;
            _deliveryTarget.Changed += OnDeliveryTargetChanged;

            _link.Quantum = Math.Max(1, _project.Current.TimeSignature.Numerator);
        }

        public Array PlaybackModes => Enum.GetValues<PlaybackMode>();

        public PlaybackMode PlaybackMode
        {
            get => _playback.Mode;
            set => _playback.Mode = value;
        }

        /// <summary>When true, session clip launches are logged for capture to the arrangement.</summary>
        public bool SessionRecordArmed
        {
            get => _capture.SessionRecordArmed;
            set => _capture.SetSessionRecordArmed(value);
        }

        /// <summary>When true, MIDI notes/CC are routed to the selected instrument track.</summary>
        public bool MidiInstrumentInputEnabled
        {
            get => _midi.InstrumentInputEnabled;
            set
            {
                if (_midi.InstrumentInputEnabled == value) return;
                if (_appSettings is AppSettingsService svc)
                {
                    svc.SetMidiInstrumentInputEnabled(value);
                    OnPropertyChanged();
                }
            }
        }

        private void OnPlaybackModeChanged()
        {
            OnPropertyChanged(nameof(PlaybackMode));
            if (_appSettings.Current.MidiInstrumentInputEnabled is null)
            {
                _midi.InstrumentInputEnabled = _playback.Mode == PlaybackMode.Arrangement;
                OnPropertyChanged(nameof(MidiInstrumentInputEnabled));
            }
        }

        private void OnSessionRecordArmedChanged() =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SessionRecordArmed)));

        private void ClearPunch()
        {
            _transport.PunchInBeat = null;
            _transport.PunchOutBeat = null;
        }

        private void OnPunchChanged()
        {
            OnPropertyChanged(nameof(LoopRecording));
            OnPropertyChanged(nameof(PunchInBeat));
            OnPropertyChanged(nameof(PunchOutBeat));
            OnPropertyChanged(nameof(HasPunchRegion));
            OnPropertyChanged(nameof(PunchInfo));
        }

        /// <summary>Sets punch-in to the current start marker.</summary>
        public RelayCommand SetPunchInCommand { get; }

        /// <summary>Sets punch-out to the current start marker.</summary>
        public RelayCommand SetPunchOutCommand { get; }

        /// <summary>Clears the punch region.</summary>
        public RelayCommand ClearPunchCommand { get; }

        public RelayCommand AddMarkerAtPlayheadCommand { get; }
        public RelayCommand GoToNextMarkerCommand { get; }
        public RelayCommand GoToPreviousMarkerCommand { get; }
        public RelayCommand CaptureRetrospectiveMidiCommand { get; }

        /// <summary>Global key root (0 = C) for scale-aware editing.</summary>
        public int KeyRootIndex
        {
            get => _project.Current.KeyRootPitchClass;
            set
            {
                var clamped = ((value % 12) + 12) % 12;
                if (_project.Current.KeyRootPitchClass == clamped) return;
                _project.Current.KeyRootPitchClass = clamped;
                OnPropertyChanged();
            }
        }

        /// <summary>Global project scale/mode.</summary>
        public ScaleType KeyScale
        {
            get => _project.Current.KeyScale;
            set
            {
                if (_project.Current.KeyScale == value) return;
                _project.Current.KeyScale = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<string> KeyRootNotes { get; } = MusicTheory.NoteNames;
        public IReadOnlyList<ScaleType> KeyScales { get; } = (ScaleType[])Enum.GetValues(typeof(ScaleType));

        public bool LoopRecording
        {
            get => _transport.LoopRecording;
            set => _transport.LoopRecording = value;
        }

        public double? PunchInBeat
        {
            get => _transport.PunchInBeat;
            set => _transport.PunchInBeat = value;
        }

        public double? PunchOutBeat
        {
            get => _transport.PunchOutBeat;
            set => _transport.PunchOutBeat = value;
        }

        public bool HasPunchRegion => PunchInBeat is { } pi && PunchOutBeat is { } po && po > pi;

        public string PunchInfo => HasPunchRegion
            ? $"Punch {PunchInBeat:0.##} – {PunchOutBeat:0.##} beats"
            : "No punch region";

        /// <summary>True when libabl-link is available (desktop build with native library).</summary>
        public bool ShowLink => _link.IsAvailable;

        /// <summary>Whether Ableton Link session participation is enabled.</summary>
        public bool IsLinkEnabled
        {
            get => _link.IsEnabled;
            set
            {
                if (_link.IsEnabled == value) return;
                _link.IsEnabled = value;
                if (value)
                {
                    _link.Quantum = Math.Max(1, TimeSigNumerator);
                    PushTempoToLink();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(LinkPeerText));
                OnPropertyChanged(nameof(LinkPhaseText));
            }
        }

        /// <summary>Formatted Link peer count for the transport bar.</summary>
        public string LinkPeerText => _link.PeerCount == 0 ? "Link" : $"Link ({_link.PeerCount})";

        /// <summary>Shared Link phase within the current quantum (shown when Link is enabled).</summary>
        public string LinkPhaseText => _link.IsEnabled
            ? $"Phase {_link.Phase:0.0}/{_link.Quantum:0.0}"
            : "";

        public string LinkSessionBeatText => _link.IsEnabled && IsPlaying
            ? $"Link beat {_link.SessionBeat:0.00}"
            : "";

        /// <summary>Sets the loop start ("[") to the current start marker.</summary>
        public RelayCommand SetLoopStartCommand { get; }

        /// <summary>Sets the loop end ("]") to the current start marker.</summary>
        public RelayCommand SetLoopEndCommand { get; }

        /// <summary>True when a loop region is active (drives the loop-button highlight).</summary>
        public bool IsLooping => _transport.IsLoopActive;

        /// <summary>Audio input/output device pickers shown in the top bar.</summary>
        public AudioDevicesViewModel Devices { get; }

        // Stop ends a recording session (committing its clips) or just stops the transport.
        private void OnStop()
        {
            if (_recording.IsRecording) _recording.StopRecording();
            else _transport.Stop();
        }

        /// <summary>Toggles Slice mode (click a clip to cut it in two). Slice can also be armed by holding CTRL.</summary>
        public bool IsSliceMode
        {
            get => _editMode.Mode == EditMode.Slice;
            set => _editMode.Mode = value ? EditMode.Slice : EditMode.Edit;
        }

        /// <summary>True while an export render is in progress (disables the Render button).</summary>
        public bool IsRendering
        {
            get => _isRendering;
            private set
            {
                if (SetField(ref _isRendering, value)) OnPropertyChanged(nameof(CanRender));
            }
        }

        public bool CanRender => !IsRendering;

        private double _renderProgress;

        /// <summary>Render completion, 0..1 — drives the little progress bar next to the Render button.</summary>
        public double RenderProgress
        {
            get => _renderProgress;
            private set => SetField(ref _renderProgress, value);
        }

        /// <summary>
        /// Renders the whole arrangement to <paramref name="path"/> off the UI thread. The format
        /// follows the file extension: .wav writes directly, .mp3 (320 kbps) and .flac render to a
        /// temporary WAV and encode it with the system ffmpeg.
        /// </summary>
        public async System.Threading.Tasks.Task RenderToFileAsync(string path)
        {
            if (IsRendering) return;
            IsRendering = true;
            RenderProgress = 0;
            try
            {
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                var options = new ExportOptions
                {
                    Kind = ExportKind.Master,
                    BitDepth = 16,
                    ApplyDither = true,
                    AnalyzeLoudness = true,
                    DeliveryPlatform = _deliveryTarget.PlatformName,
                    TargetIntegratedLufs = _deliveryTarget.TargetIntegratedLufs,
                    TargetTruePeakDbTp = _deliveryTarget.TargetTruePeakDbTp,
                    AudioFormat = ext switch
                    {
                        ".mp3" => ExportAudioFormat.Mp3,
                        ".flac" => ExportAudioFormat.Flac,
                        ".ogg" => ExportAudioFormat.Ogg,
                        _ => ExportAudioFormat.Wav
                    }
                };
                var progress = new Progress<double>(p => RenderProgress = p);
                await System.Threading.Tasks.Task.Run(() => _export.Export(
                    _project.Current, _engine.Format, _transport.Tempo.BeatsPerMinute, path, options, progress));
                RenderProgress = 1;
            }
            finally
            {
                IsRendering = false;
                RenderProgress = 0;
            }
        }

        public RelayCommand PlayCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand RecordCommand { get; }
        public RelayCommand TapTempoCommand { get; }

        private void TapTempo()
        {
            var now = Environment.TickCount64;
            if (_tapTimes.Count > 0 && now - _tapTimes.Last() > 2000)
                _tapTimes.Clear();
            _tapTimes.Enqueue(now);
            while (_tapTimes.Count > 4) _tapTimes.Dequeue();
            if (_tapTimes.Count < 2) return;

            var taps = _tapTimes.ToArray();
            var averageMs = (taps[^1] - taps[0]) / (double)(taps.Length - 1);
            if (averageMs <= 0) return;
            Bpm = Math.Clamp(60_000.0 / averageMs, 20.0, 300.0);
        }

        public bool IsPlaying => _transport.State == TransportState.Playing;
        public bool IsRecording => _recording.IsRecording;
        public bool CanPlay => !IsPlaying && !IsRecording;
        public bool CanStop => IsPlaying || IsRecording;
        public bool CanRecord => !IsPlaying && !IsRecording;

        /// <summary>Tempo in beats per minute; two-way bound to the BPM editor. Reads the project tempo so
        /// the editor follows Tempo automation live during playback (it's the value the lane writes).</summary>
        public double Bpm
        {
            get => _project.Current.Tempo.BeatsPerMinute;
            set
            {
                if (value <= 0 || _project.Current.Tempo.BeatsPerMinute == value) return;
                App.ServiceProvider?.GetService<IHistoryService>()?.Capture("Change tempo");
                _transport.Tempo = new Tempo(value);
                _project.Current.Tempo = new Tempo(value); // keep the project model in sync
                PushTempoToLink();
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalTime));
            }
        }

        /// <summary>Arrangement length in bars; two-way bound to the Bars editor.</summary>
        public int Bars
        {
            get => _project.Current.BarCount;
            set
            {
                var clamped = value < 1 ? 1 : value;
                if (_project.Current.BarCount == clamped) return;
                _project.Current.BarCount = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalTime));
                _events.Publish(new ArrangementLengthChangedEvent());
            }
        }

        /// <summary>Time-signature numerator (beats per bar).</summary>
        public int TimeSigNumerator
        {
            get => _project.Current.TimeSignature.Numerator;
            set
            {
                if (value < 1 || value == TimeSigNumerator) return;
                _project.Current.TimeSignature = new TimeSignature(value, TimeSigDenominator);
                _link.Quantum = Math.Max(1, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalTime));
                _events.Publish(new ArrangementLengthChangedEvent());
            }
        }

        /// <summary>Available time-signature denominators.</summary>
        public int[] Denominators { get; } = { 1, 2, 4, 8, 16 };

        /// <summary>Time-signature denominator (note value).</summary>
        public int TimeSigDenominator
        {
            get => _project.Current.TimeSignature.Denominator;
            set
            {
                if (value < 1 || value == TimeSigDenominator) return;
                _project.Current.TimeSignature = new TimeSignature(TimeSigNumerator, value);
                OnPropertyChanged();
                _events.Publish(new ArrangementLengthChangedEvent());
            }
        }

        /// <summary>Total arrangement time (m:ss.mmm).</summary>
        public string TotalTime => FormatTime(Bars * BeatsPerBar / _transport.Tempo.BeatsPerMinute * 60.0);

        /// <summary>Current playhead time (m:ss.mmm).</summary>
        public string PlayheadTime => FormatTime(_transport.PlayheadBeats / _transport.Tempo.BeatsPerMinute * 60.0);

        public double MasterLevelLeft => _engine.MasterLevelLeft;
        public double MasterLevelRight => _engine.MasterLevelRight;

        public double MasterTruePeakLeftDbTp => _engine.MasterTruePeakLeftDbTp;
        public double MasterTruePeakRightDbTp => _engine.MasterTruePeakRightDbTp;
        public double MasterTruePeakMaxDbTp => _engine.MasterTruePeakMaxDbTp;
        public double MasterMomentaryLufs => _engine.MasterMomentaryLufs;
        public double MasterShortTermLufs => _engine.MasterShortTermLufs;
        public double MasterIntegratedLufs => _engine.MasterIntegratedLufs;
        public double MasterLoudnessRangeLu => _engine.MasterLoudnessRangeLu;
        public string MeterTapLabel => _engine.MasterMeterTap switch
        {
            MasterMeterTap.PreLimiter => Loc.Get("Mastering_MeterTap_PreLimiter", "Pre Limiter"),
            MasterMeterTap.PostChain => Loc.Get("Mastering_MeterTap_PostChain", "Post Chain"),
            _ => Loc.Get("Mastering_MeterTap_PostFader", "Post Fader")
        };
        public double TargetTruePeakDbTp => _deliveryTarget.TargetTruePeakDbTp;

        private int _kSystemIndex; // 0=digital, 1=K-12, 2=K-14, 3=K-20
        public double KSystemOffsetDb => _kSystemIndex switch { 1 => 12, 2 => 14, 3 => 20, _ => 0 };
        public string KSystemLabel => _kSystemIndex switch
        {
            1 => "K-12",
            2 => "K-14",
            3 => "K-20",
            _ => "FS"
        };
        public RelayCommand CycleKSystemCommand { get; }

        public string DeliveryTargetText
        {
            get
            {
                static string N(double value, string format) => value.ToString(format).Replace("-", "−");
                return $"{_deliveryTarget.PlatformName} {N(_deliveryTarget.TargetIntegratedLufs, "0.#")} LUFS / " +
                       $"{N(_deliveryTarget.TargetTruePeakDbTp, "0.0")} dBTP";
            }
        }

        private void OnDeliveryTargetChanged()
        {
            OnPropertyChanged(nameof(TargetTruePeakDbTp));
            OnPropertyChanged(nameof(DeliveryTargetText));
            OnPropertyChanged(nameof(MasterTruePeakWarning));
        }

        public bool MasterTruePeakWarning =>
            MasterTruePeakMaxDbTp > TargetTruePeakDbTp + 0.05;

        public string MasterLoudnessText
        {
            get
            {
                var m = MasterMomentaryLufs;
                var st = MasterShortTermLufs;
                var integ = MasterIntegratedLufs;
                var lra = MasterLoudnessRangeLu;
                var tp = MasterTruePeakMaxDbTp;
                static string Fmt(double v) =>
                    float.IsNegativeInfinity((float)v) ? "−∞" : v.ToString("0.0");
                var lraText = double.IsNaN(lra) ? "—" : lra.ToString("0.0");
                return $"M {Fmt(m)}  ST {Fmt(st)}  I {Fmt(integ)} LUFS  ·  LRA {lraText} LU  ·  {tp:0.0} dBTP";
            }
        }

        public RelayCommand ResetMasterLoudnessCommand { get; }

        /// <summary>Standalone metronome click during playback (independent of record count-in).</summary>
        public bool MetronomeEnabled
        {
            get => _transport.MetronomeEnabled;
            set => _transport.MetronomeEnabled = value;
        }

        /// <summary>Master bus output gain (0..1), bound to the master track volume.</summary>
        public double MasterVolume
        {
            get => _project.Current.Master?.Volume ?? Track.DefaultVolume;
            set
            {
                var master = _project.Current.Master;
                if (master is null) return;
                var clamped = value < 0 ? 0 : value > 1 ? 1 : value;
                if (Math.Abs(master.Volume - clamped) < 1e-9) return;
                App.ServiceProvider?.GetService<IHistoryService>()?.Capture("Change master volume");
                master.Volume = clamped;
                OnPropertyChanged();
            }
        }

        /// <summary>True when the host exposes process CPU/RAM sampling (desktop).</summary>
        public bool ShowSystemMetrics => _metrics.IsAvailable;

        /// <summary>Formatted process CPU usage for the transport bar.</summary>
        public string CpuText => _metrics.CpuPercent is { } pct ? $"{pct:0}%" : "—";

        /// <summary>Last audio-block load relative to the render deadline (100% = budget).</summary>
        public string AudioLoadText =>
            _metrics.AudioLoadPercent is { } load
                ? (_metrics.UnderrunCount > 0
                    ? $"DSP {load:0}% !{_metrics.UnderrunCount}"
                    : $"DSP {load:0}%")
                : "DSP —";

        /// <summary>Formatted process working-set size for the transport bar.</summary>
        public string RamText => FormatBytes(_metrics.MemoryBytes);

        /// <summary>Formatted managed heap size for the transport bar.</summary>
        public string HeapText => $"Heap {FormatBytes(_metrics.ManagedHeapBytes)}";

        private long _lastTimeRefreshMs;
        private double _lastMasterL = -1;
        private double _lastMasterR = -1;

        /// <summary>Refreshes the polled values — called once per render frame via the PlaybackClock.
        /// Meters (cheap, no text) refresh every call; the sub-second time readout is throttled because
        /// re-shaping its text every frame makes the compositor miss vsync.</summary>
        public void RefreshMeters()
        {
            var l = MasterLevelLeft;
            var r = MasterLevelRight;
            if (Math.Abs(l - _lastMasterL) >= 0.002)
            {
                _lastMasterL = l;
                OnPropertyChanged(nameof(MasterLevelLeft));
            }

            if (Math.Abs(r - _lastMasterR) >= 0.002)
            {
                _lastMasterR = r;
                OnPropertyChanged(nameof(MasterLevelRight));
            }

            OnPropertyChanged(nameof(MasterTruePeakLeftDbTp));
            OnPropertyChanged(nameof(MasterTruePeakRightDbTp));
            OnPropertyChanged(nameof(MasterTruePeakMaxDbTp));
            OnPropertyChanged(nameof(MasterMomentaryLufs));
            OnPropertyChanged(nameof(MasterShortTermLufs));
            OnPropertyChanged(nameof(MasterIntegratedLufs));
            OnPropertyChanged(nameof(MasterLoudnessRangeLu));
            OnPropertyChanged(nameof(MasterLoudnessText));
            OnPropertyChanged(nameof(MasterTruePeakWarning));
            OnPropertyChanged(nameof(MeterTapLabel));

            RefreshPlayheadTime();
        }

        /// <summary>
        /// Cheap playhead time (and tempo editors while playing). Used alone on the browser demo while
        /// playback freezes the shared <see cref="IPlaybackClock"/> meter fan-out.
        /// </summary>
        public void RefreshPlayheadTime()
        {
            var now = Environment.TickCount64;
            var interval = UiPerfProfile.IsConstrained ? 250 : 100;
            if (now - _lastTimeRefreshMs < interval) return;
            _lastTimeRefreshMs = now;
            OnPropertyChanged(nameof(PlayheadTime));

            // While playing, Tempo / Time-signature automation moves these underlying values on the audio
            // thread — re-read them so the editors animate (the same way the inspector faders do).
            if (IsPlaying)
            {
                OnPropertyChanged(nameof(Bpm));
                OnPropertyChanged(nameof(TotalTime));
                OnPropertyChanged(nameof(TimeSigNumerator));
            }

            if (_link.IsEnabled)
            {
                _link.RefreshSessionState();
                OnPropertyChanged(nameof(LinkPeerText));
                OnPropertyChanged(nameof(LinkPhaseText));
                if (IsPlaying)
                {
                    OnPropertyChanged(nameof(LinkSessionBeatText));
                    var drift = Math.Abs(_transport.PlayheadBeats - _link.SessionBeat);
                    if (drift > 0.05)
                        _transport.NotifyPlayhead(_link.SessionBeat);
                }
            }
        }

        private async void AddMarkerAtPlayhead()
        {
            await _timeline.AddMarkerAtPlayheadInteractiveAsync();
            GoToNextMarkerCommand.RaiseCanExecuteChanged();
            GoToPreviousMarkerCommand.RaiseCanExecuteChanged();
        }

        private void GoToNextMarker()
        {
            var markers = _project.Current.Markers.OrderBy(m => m.Beat).ToList();
            if (markers.Count == 0) return;
            var beat = _transport.PlayheadBeats;
            var next = markers.FirstOrDefault(m => m.Beat > beat + 1e-6) ?? markers[0];
            _timeline.GoToMarker(next);
        }

        private void GoToPreviousMarker()
        {
            var markers = _project.Current.Markers.OrderBy(m => m.Beat).ToList();
            if (markers.Count == 0) return;
            var beat = _transport.PlayheadBeats;
            var prev = markers.LastOrDefault(m => m.Beat < beat - 1e-6) ?? markers[^1];
            _timeline.GoToMarker(prev);
        }

        private int BeatsPerBar => Math.Max(1, _project.Current.TimeSignature.Numerator);

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) seconds = 0;
            var minutes = (int)(seconds / 60);
            var secs = (int)(seconds % 60);
            var millis = (int)((seconds - Math.Floor(seconds)) * 1000);
            return $"{minutes}:{secs:00}.{millis:000}";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            const long gb = 1024L * 1024 * 1024;
            if (bytes >= gb) return $"{bytes / (double)gb:0.#} GB";
            return $"{bytes / (1024.0 * 1024):0} MB";
        }

        private void OnMetricsUpdated() =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(CpuText));
                OnPropertyChanged(nameof(AudioLoadText));
                OnPropertyChanged(nameof(RamText));
                OnPropertyChanged(nameof(HeapText));
            });

        private void OnTransportStateChanged(TransportState state)
        {
            if (_link.IsEnabled)
            {
                if (state == TransportState.Playing)
                {
                    var beat = _transport.StartBeat;
                    if (_link.Quantum > 0)
                    {
                        // Snap the start marker to the shared downbeat when joining a Link session.
                        var q = _link.Quantum;
                        beat = Math.Floor(beat / q) * q + _link.Phase;
                    }
                    _link.StartAtBeat(beat);
                }
                else _link.Stop();
            }

            OnStateChanged();
        }

        private void OnLinkSyncChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(LinkPeerText));
                OnPropertyChanged(nameof(LinkPhaseText));
                if (!_link.IsEnabled || _syncingLinkTempo) return;

                var remote = _link.Tempo;
                if (remote <= 0) return;
                if (Math.Abs(_project.Current.Tempo.BeatsPerMinute - remote) < 0.01) return;

                _syncingLinkTempo = true;
                try
                {
                    _transport.Tempo = new Tempo(remote);
                    _project.Current.Tempo = new Tempo(remote);
                    OnPropertyChanged(nameof(Bpm));
                    OnPropertyChanged(nameof(TotalTime));
                    OnPropertyChanged(nameof(PlayheadTime));
                }
                finally
                {
                    _syncingLinkTempo = false;
                }
            });
        }

        private void PushTempoToLink()
        {
            if (!_link.IsEnabled || _syncingLinkTempo) return;
            _syncingLinkTempo = true;
            try { _link.Tempo = _project.Current.Tempo.BeatsPerMinute; }
            finally { _syncingLinkTempo = false; }
        }

        private void OnStateChanged()
        {
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanRecord));
            OnPropertyChanged(nameof(PlayheadTime));
        }

        private void OnRecordingStateChanged()
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanRecord));
        }

        private void OnTempoChanged()
        {
            OnPropertyChanged(nameof(Bpm));
            OnPropertyChanged(nameof(TotalTime));
            OnPropertyChanged(nameof(PlayheadTime));
        }
    }
}
