using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Ongenet.App.Services;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Music;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// Bottom-panel inspector for audio sample clips: tempo/stretch controls plus an interactive waveform
    /// editor with destructive trim, selection, and cut/copy/paste. Edits apply to the shared PCM buffer
    /// when multiple clips reference the same source.
    /// </summary>
    public class SampleInspectorViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly ITransportService _transport;
        private readonly IEventAggregator _events;
        private readonly IProjectService _project;
        private readonly IHistoryService _history;
        private readonly IAuditionPlayer _audition;

        private AudioSegment? _clipboard;
        private double _trimStartSeconds;
        private double _trimEndSeconds;
        private double _selectionStartSeconds = -1;
        private double _selectionEndSeconds = -1;
        private int _waveRevision;
        private double _hoverSeconds = -1;
        private double _viewportWidth;
        private double _zoomScale = 1.0;
        private double _selectionGainDb;
        private double _selectionPan;
        private bool _selectionEditCaptured;
        private bool _snapEnabled = true;
        private bool _isPlaying;
        private bool _isDetectingKey;
        private bool _isDetectingTempo;
        private double _playheadSeconds = -1;
        private double _auditionOffsetSeconds;
        private int _waveformBuildGeneration;
        private int _targetKeyRootIndex;
        private bool _targetKeyIsMinor;
        private List<TargetKeyOption> _targetKeyOptions = new();
        private TargetKeyOption? _selectedTargetKey;
        private bool _isChangingKey;
        private double _keyChangeProgress;

        public SampleInspectorViewModel(ISelectionService selection, ITransportService transport,
            IEventAggregator events, IProjectService project, IHistoryService history,
            IAuditionPlayer audition, IPlaybackClock clock)
        {
            _selection = selection;
            _transport = transport;
            _events = events;
            _project = project;
            _history = history;
            _audition = audition;

            CutCommand = new RelayCommand(Cut, () => HasSelection);
            CopyCommand = new RelayCommand(Copy, () => HasSelection);
            PasteCommand = new RelayCommand(Paste, () => _clipboard is not null && Clip is not null);
            DeleteCommand = new RelayCommand(DeleteSelection, () => HasSelection);
            SwapChannelsCommand = new RelayCommand(SwapChannels, () => HasSelection && CanSwapChannels);
            ReverseSelectionCommand = new RelayCommand(ReverseSelection, () => HasSelection);
            PlayStopCommand = new RelayCommand(TogglePlay, () => HasSample);
            DetectKeyCommand = new RelayCommand(() => _ = DetectKeyAsync(), () => HasSample && !_isDetectingKey);
            RedetectTempoCommand = new RelayCommand(() => _ = RedetectTempoAsync(), () => HasSample && !_isDetectingTempo);
            ChangeKeyCommand = new RelayCommand(() => _ = ChangeSampleKeyAsync(), CanChangeSampleKey);

            _selection.SelectionChanged += OnSelectionChanged;
            _transport.TempoChanged += _ => RaiseAll();
            _project.ProjectChanged += () => RaiseAll();
            _events.Subscribe<ClipChangedEvent>(e =>
            {
                if (ReferenceEquals(e.Clip, Clip)) RaiseAll();
            });

            _audition.Finished += () =>
            {
                _isPlaying = false;
                _playheadSeconds = -1;
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(PlayButtonText));
                OnPropertyChanged(nameof(PlayheadSeconds));
            };

            // Reflect audition state and playhead on the UI heartbeat.
            clock.Tick += () =>
            {
                var playing = _audition.IsPlaying;
                if (playing != _isPlaying)
                {
                    _isPlaying = playing;
                    OnPropertyChanged(nameof(IsPlaying));
                    OnPropertyChanged(nameof(PlayButtonText));
                    if (!playing) _playheadSeconds = -1;
                }

                if (playing)
                {
                    var pos = _auditionOffsetSeconds + _audition.PositionSeconds;
                    if (Math.Abs(pos - _playheadSeconds) > 0.001)
                    {
                        _playheadSeconds = pos;
                        OnPropertyChanged(nameof(PlayheadSeconds));
                    }
                }
                else if (_playheadSeconds >= 0)
                {
                    _playheadSeconds = -1;
                    OnPropertyChanged(nameof(PlayheadSeconds));
                }
            };
        }

        public RelayCommand CutCommand { get; }
        public RelayCommand CopyCommand { get; }
        public RelayCommand PasteCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand SwapChannelsCommand { get; }
        public RelayCommand ReverseSelectionCommand { get; }
        public RelayCommand PlayStopCommand { get; }
        public RelayCommand DetectKeyCommand { get; }
        public RelayCommand RedetectTempoCommand { get; }
        public RelayCommand ChangeKeyCommand { get; }

        public bool IsPlaying => _isPlaying;

        public string PlayButtonText => _isPlaying ? "Stop" : "Play";

        /// <summary>Audition playhead in full-sample seconds, or −1 when not playing.</summary>
        public double PlayheadSeconds => _playheadSeconds;

        public IReadOnlyList<TargetKeyOption> TargetKeyOptions => _targetKeyOptions;

        public TargetKeyOption? SelectedTargetKey
        {
            get
            {
                if (_selectedTargetKey is { RootIndex: var r, IsMinor: var m } &&
                    r == _targetKeyRootIndex && m == _targetKeyIsMinor)
                    return _selectedTargetKey;
                return FindTargetKeyOption(_targetKeyRootIndex, _targetKeyIsMinor);
            }
            set
            {
                if (value is null) return;
                if (_targetKeyRootIndex == value.RootIndex && _targetKeyIsMinor == value.IsMinor) return;
                _targetKeyRootIndex = value.RootIndex;
                _targetKeyIsMinor = value.IsMinor;
                _selectedTargetKey = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanChangeKey));
                ChangeKeyCommand.RaiseCanExecuteChanged();
            }
        }

        public string KeyChangeHint =>
            "Transposes by semitones only — major/minor character is unchanged. ★ marks related keys and low shifts that tend to sound smoother. Re-detect reads the audio, not the label.";

        /// <summary>True while a background pitch-shift for a key change is running.</summary>
        public bool IsChangingKey => _isChangingKey;

        /// <summary>Progress (0..1) of the running key change.</summary>
        public double KeyChangeProgress => _keyChangeProgress;

        public bool SnapEnabled
        {
            get => _snapEnabled;
            set => SetField(ref _snapEnabled, value);
        }

        private Clip? Clip => _selection.SelectedClip is { IsAudio: true } clip ? clip : null;

        public bool HasSample => Clip is not null;

        public string SampleName => Clip?.Name ?? "No sample";

        public double NaturalBpm
        {
            get => Clip?.SourceTempo ?? 0;
            set
            {
                if (Clip is not { } clip) return;
                clip.SourceTempo = value > 0 ? value : null;
                if (clip.StretchToTempo) Refit(clip);
                Publish(clip);
            }
        }

        public bool StretchEnabled
        {
            get => Clip?.StretchToTempo ?? false;
            set
            {
                if (Clip is not { } clip) return;
                if (value)
                {
                    if (clip.SourceTempo is not { } t || t <= 0) clip.SourceTempo = _transport.Tempo.BeatsPerMinute;
                    clip.StretchToTempo = true;
                    Refit(clip);
                }
                else
                {
                    clip.StretchToTempo = false;
                    var duration = PlayableDurationSeconds(clip);
                    if (duration > 0) clip.LengthBeats = duration * _transport.Tempo.BeatsPerMinute / 60.0;
                }

                Publish(clip);
            }
        }

        public bool PitchCorrected
        {
            get => Clip?.PitchCorrected ?? false;
            set
            {
                if (Clip is not { } clip) return;
                clip.PitchCorrected = value;
                Publish(clip);
            }
        }

        public bool HasTempo => Clip is { SourceTempo: > 0 };

        public string KeyInfo
        {
            get
            {
                if (_isDetectingKey) return "Detecting…";
                return Clip?.SourceKey is { Length: > 0 } key ? key : "—";
            }
        }

        public bool CanChangeKey => CanChangeSampleKey();

        public string StretchInfo
        {
            get
            {
                if (Clip is not { } clip) return string.Empty;
                if (!clip.StretchToTempo) return "Native (not stretched)";
                var duration = PlayableDurationSeconds(clip);
                var factor = TempoSync.Stretch(duration, _transport.Tempo.BeatsPerMinute, clip.LengthBeats);
                var pct = factor * 100.0;
                var dir = factor > 1.0001 ? " — faster" : factor < 0.9999 ? " — slower" : "";
                return string.Create(CultureInfo.InvariantCulture, $"{factor:0.###}× ({pct:0}% speed){dir}");
            }
        }

        public string LengthInfo
        {
            get
            {
                if (Clip is not { } clip) return string.Empty;
                return string.Create(CultureInfo.InvariantCulture, $"{clip.LengthBeats:0.##} beats");
            }
        }

        public string SourceInfo
        {
            get
            {
                if (Clip?.Samples is not { } s) return string.Empty;
                var seconds = s.FrameCount / (double)s.SampleRate;
                var channels = s.Channels == 1 ? "mono" : s.Channels == 2 ? "stereo" : $"{s.Channels} ch";
                return string.Create(CultureInfo.InvariantCulture,
                    $"{seconds:0.00} s · {s.SampleRate / 1000.0:0.0} kHz · {channels}");
            }
        }

        public int SharedInstanceCount => Clip is { } c ? ClipSharingOps.SharedInstanceCount(_project.Current, c) : 0;

        public bool IsSharedSample => SharedInstanceCount > 1;

        public string SharedWarning => IsSharedSample
            ? $"Used by {SharedInstanceCount} clips — edits affect all instances"
            : string.Empty;

        public AudioWaveform? Waveform => Clip?.Waveform;

        public int WaveRevision => _waveRevision;

        public double DurationSeconds => Clip?.Samples is { } s && s.SampleRate > 0
            ? s.FrameCount / (double)s.SampleRate
            : 0;

        public double GridSecondsPerBeat
        {
            get
            {
                if (Clip is not { } clip || DurationSeconds <= 0) return 0;
                if (clip.StretchToTempo && clip.LengthBeats > 0)
                {
                    var playable = PlayableDurationSeconds(clip);
                    return playable > 0 ? playable / clip.LengthBeats : 0;
                }

                if (clip.SourceTempo is { } tempo && tempo > 0) return 60.0 / tempo;
                var projectBpm = _transport.Tempo.BeatsPerMinute;
                return projectBpm > 0 ? 60.0 / projectBpm : 0;
            }
        }

        public int GridBeatsPerBar
        {
            get
            {
                var num = _project.Current.TimeSignature.Numerator;
                return num < 1 ? 4 : num;
            }
        }

        public bool GridHasMusicalGrid => GridSecondsPerBeat > 0 && DurationSeconds > 0;

        public double ViewportWidth
        {
            get => _viewportWidth;
            set
            {
                if (SetField(ref _viewportWidth, value))
                    OnPropertyChanged(nameof(ContentWidth));
            }
        }

        public double ZoomScale
        {
            get => _zoomScale;
            set
            {
                var clamped = value < 1.0 ? 1.0 : value > 64.0 ? 64.0 : value;
                if (SetField(ref _zoomScale, clamped))
                    OnPropertyChanged(nameof(ContentWidth));
            }
        }

        public double ContentWidth => Math.Max(_viewportWidth, _viewportWidth * _zoomScale);

        public double HoverSeconds
        {
            get => _hoverSeconds;
            set
            {
                if (SetField(ref _hoverSeconds, value))
                    OnPropertyChanged(nameof(HoverInfo));
            }
        }

        public string HoverInfo
        {
            get
            {
                if (_hoverSeconds < 0) return string.Empty;
                var spb = GridSecondsPerBeat;
                if (spb <= 0)
                    return string.Create(CultureInfo.InvariantCulture, $"Hover: {_hoverSeconds:0.###} s");

                var beat = _hoverSeconds / spb;
                var bar = (int)Math.Floor(beat / GridBeatsPerBar) + 1;
                return string.Create(CultureInfo.InvariantCulture,
                    $"Hover: Bar {bar} · {_hoverSeconds:0.###} s");
            }
        }

        public bool HasSelection => _selectionStartSeconds >= 0 && _selectionEndSeconds >= 0 &&
                                    Math.Abs(_selectionEndSeconds - _selectionStartSeconds) > 1e-6;

        public bool CanEditSelectionPan => Clip?.Samples?.Channels == 2;

        public bool CanSwapChannels => CanEditSelectionPan;

        public double SelectionGainDb
        {
            get => _selectionGainDb;
            set
            {
                if (SetField(ref _selectionGainDb, value))
                    OnPropertyChanged(nameof(SelectionGainText));
            }
        }

        public double SelectionPan
        {
            get => _selectionPan;
            set
            {
                if (SetField(ref _selectionPan, value))
                    OnPropertyChanged(nameof(SelectionPanText));
            }
        }

        public string SelectionGainText => string.Create(CultureInfo.InvariantCulture, $"{_selectionGainDb:0.#} dB");

        public string SelectionPanText => string.Create(CultureInfo.InvariantCulture, $"{_selectionPan:0.##}");

        public double TrimStartSeconds
        {
            get => _trimStartSeconds;
            set => SetField(ref _trimStartSeconds, Math.Max(0, value));
        }

        public double TrimEndSeconds
        {
            get => _trimEndSeconds;
            set => SetField(ref _trimEndSeconds, value);
        }

        public double HighlightStartSeconds => Clip?.SourceOffsetSeconds ?? 0;

        public double HighlightEndSeconds
        {
            get
            {
                if (Clip is not { Samples: { } s } clip) return DurationSeconds;
                var full = s.FrameCount / (double)s.SampleRate;
                return clip.SourceLengthSeconds is { } len ? clip.SourceOffsetSeconds + len : full;
            }
        }

        public double SelectionStartSeconds
        {
            get => _selectionStartSeconds;
            set => SetField(ref _selectionStartSeconds, value);
        }

        public double SelectionEndSeconds
        {
            get => _selectionEndSeconds;
            set => SetField(ref _selectionEndSeconds, value);
        }

        public string SelectionInfo
        {
            get
            {
                if (!HasSelection) return "No selection";
                var a = Math.Min(_selectionStartSeconds, _selectionEndSeconds);
                var b = Math.Max(_selectionStartSeconds, _selectionEndSeconds);
                return string.Create(CultureInfo.InvariantCulture, $"{a:0.###} s – {b:0.###} s");
            }
        }

        public void OnTrimCommitted()
        {
            if (Clip is not { Samples: { } oldBuffer } clip) return;
            var sampleRate = oldBuffer.SampleRate;
            var startFrame = (long)Math.Round(_trimStartSeconds * sampleRate);
            var endFrame = (long)Math.Round(_trimEndSeconds * sampleRate);
            if (endFrame <= startFrame) return;

            _history.Capture("Trim sample");
            var trimmed = SampleEditOps.Trim(oldBuffer, startFrame, endFrame);
            ApplyBufferChange(oldBuffer, trimmed);
            _trimStartSeconds = 0;
            _trimEndSeconds = trimmed.FrameCount / (double)trimmed.SampleRate;
            ClearSelection();
            AfterBufferEdit(clip);
        }

        private double _moveOriginalStart = -1;
        private double _moveOriginalEnd = -1;

        public void OnMoveStarted()
        {
            if (!HasSelection) return;
            _moveOriginalStart = Math.Min(_selectionStartSeconds, _selectionEndSeconds);
            _moveOriginalEnd = Math.Max(_selectionStartSeconds, _selectionEndSeconds);
        }

        public void OnMoveCommitted()
        {
            if (Clip is not { Samples: { } buffer } clip || _moveOriginalStart < 0) return;

            var sampleRate = buffer.SampleRate;
            var newStart = Math.Min(_selectionStartSeconds, _selectionEndSeconds);
            var newEnd = Math.Max(_selectionStartSeconds, _selectionEndSeconds);
            var frameCount = (long)Math.Round((_moveOriginalEnd - _moveOriginalStart) * sampleRate);
            var fromFrame = (long)Math.Round(_moveOriginalStart * sampleRate);
            var toFrame = (long)Math.Round(newStart * sampleRate);
            if (frameCount <= 0 || fromFrame == toFrame) { _moveOriginalStart = -1; return; }

            _history.Capture("Move sample");
            var moved = SampleEditOps.MoveRange(buffer, fromFrame, frameCount, toFrame);
            ApplyBufferChange(buffer, moved);
            _moveOriginalStart = -1;
            AfterBufferEdit(clip);
        }

        public void OnEditorSelectionChanged()
        {
            ResetSelectionEditKnobs();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanEditSelectionPan));
            OnPropertyChanged(nameof(CanSwapChannels));
            OnPropertyChanged(nameof(SelectionInfo));
            CutCommand.RaiseCanExecuteChanged();
            CopyCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
            SwapChannelsCommand.RaiseCanExecuteChanged();
            ReverseSelectionCommand.RaiseCanExecuteChanged();
        }

        public void BeginSelectionEdit(string label)
        {
            if (_selectionEditCaptured) return;
            _history.Capture(label);
            _selectionEditCaptured = true;
        }

        public bool ApplySelectionGain()
        {
            if (!HasSelection || Math.Abs(_selectionGainDb) < 1e-6) return false;
            if (Clip is not { Samples: { } buffer } clip) return false;
            if (!TryGetSelectionFrameRange(buffer.SampleRate, out var start, out var count)) return false;

            if (!_selectionEditCaptured) _history.Capture("Adjust selection gain");
            var gain = AudioMath.Db2Lin(_selectionGainDb);
            var edited = SampleEditOps.ApplyGainRange(buffer, start, count, gain);
            ApplyBufferChange(buffer, edited);
            AfterBufferEdit(clip);
            _selectionEditCaptured = false;
            return true;
        }

        public bool ApplySelectionPan()
        {
            if (!HasSelection || !CanEditSelectionPan || Math.Abs(_selectionPan) < 1e-6) return false;
            if (Clip is not { Samples: { } buffer } clip) return false;
            if (!TryGetSelectionFrameRange(buffer.SampleRate, out var start, out var count)) return false;

            if (!_selectionEditCaptured) _history.Capture("Adjust selection pan");
            var edited = SampleEditOps.ApplyPanRange(buffer, start, count, _selectionPan);
            ApplyBufferChange(buffer, edited);
            AfterBufferEdit(clip);
            _selectionEditCaptured = false;
            return true;
        }

        private void SwapChannels()
        {
            if (!HasSelection || !CanSwapChannels) return;
            if (Clip is not { Samples: { } buffer } clip) return;
            if (!TryGetSelectionFrameRange(buffer.SampleRate, out var start, out var count)) return;

            _history.Capture("Swap channels");
            var edited = SampleEditOps.SwapChannelsRange(buffer, start, count);
            ApplyBufferChange(buffer, edited);
            AfterBufferEdit(clip);
        }

        private void ReverseSelection()
        {
            if (!HasSelection) return;
            if (Clip is not { Samples: { } buffer } clip) return;
            if (!TryGetSelectionFrameRange(buffer.SampleRate, out var start, out var count)) return;

            _history.Capture("Reverse selection");
            var edited = SampleEditOps.ReverseRange(buffer, start, count);
            ApplyBufferChange(buffer, edited);
            AfterBufferEdit(clip);
        }

        /// <summary>
        /// Auditions the sample through the output device without engaging the transport. Plays the current
        /// selection when one exists, otherwise the whole sample buffer. Toggles to stop while sounding.
        /// </summary>
        private void TogglePlay()
        {
            if (_audition.IsPlaying)
            {
                _audition.Stop();
                _playheadSeconds = -1;
                OnPropertyChanged(nameof(PlayheadSeconds));
                return;
            }

            if (Clip?.Samples is not { } buffer || buffer.FrameCount <= 0) return;

            if (HasSelection && TryGetSelectionFrameRange(buffer.SampleRate, out var start, out var count))
            {
                var segment = SampleEditOps.CopyRange(buffer, start, count);
                if (segment.FrameCount <= 0) return;
                _auditionOffsetSeconds = start / (double)buffer.SampleRate;
                _audition.Play(new AudioSampleBuffer(segment.Samples, segment.Channels, segment.SampleRate));
            }
            else
            {
                _auditionOffsetSeconds = 0;
                _audition.Play(buffer);
            }

            _isPlaying = true;
            _playheadSeconds = _auditionOffsetSeconds;
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(PlayButtonText));
            OnPropertyChanged(nameof(PlayheadSeconds));
        }

        /// <summary>Starts audition from <paramref name="seconds"/> to the end of the sample buffer.</summary>
        public void PlayFromSeconds(double seconds)
        {
            if (Clip?.Samples is not { } buffer || buffer.FrameCount <= 0) return;

            var duration = buffer.FrameCount / (double)buffer.SampleRate;
            seconds = Math.Clamp(seconds, 0, duration);

            // Full-buffer audition: PositionSeconds is already absolute in the sample.
            _auditionOffsetSeconds = 0;
            _audition.Play(buffer, seconds);

            _isPlaying = true;
            _playheadSeconds = seconds;
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(PlayButtonText));
            OnPropertyChanged(nameof(PlayheadSeconds));
        }

        private async Task DetectKeyAsync()
        {
            if (Clip is not { Samples: { } buffer } clip) return;
            _isDetectingKey = true;
            OnPropertyChanged(nameof(KeyInfo));
            DetectKeyCommand.RaiseCanExecuteChanged();
            ChangeKeyCommand.RaiseCanExecuteChanged();

            var key = await Task.Run(() => MusicalKeyDetector.Detect(buffer));

            if (!ReferenceEquals(clip, Clip)) return;

            _history.Capture("Detect sample key");
            clip.SourceKey = key.Length > 0 ? key : null;
            if (key.Length > 0 && MusicalKeyFormat.TryParse(key, out var root, out var minor))
            {
                _targetKeyRootIndex = root;
                _targetKeyIsMinor = minor;
            }
            RebuildTargetKeyOptions();
            Publish(clip);

            _isDetectingKey = false;
            OnPropertyChanged(nameof(KeyInfo));
            OnPropertyChanged(nameof(CanChangeKey));
            DetectKeyCommand.RaiseCanExecuteChanged();
            ChangeKeyCommand.RaiseCanExecuteChanged();
        }

        private async Task RedetectTempoAsync()
        {
            if (Clip is not { Samples: { } buffer } clip) return;
            _isDetectingTempo = true;
            RedetectTempoCommand.RaiseCanExecuteChanged();

            var path = clip.AudioFilePath;
            var detected = await Task.Run(() =>
            {
                if (!string.IsNullOrEmpty(path))
                {
                    var tagged = TempoDetector.FromPath(path);
                    if (tagged is > 0) return tagged;
                }

                var hint = clip.SourceTempo is > 0 ? clip.SourceTempo : null;
                return TempoDetector.Estimate(buffer, hint);
            });

            if (!ReferenceEquals(clip, Clip)) return;

            if (detected is > 0)
            {
                _history.Capture("Re-detect tempo");
                NaturalBpm = detected.Value;
            }

            _isDetectingTempo = false;
            RedetectTempoCommand.RaiseCanExecuteChanged();
        }

        private bool CanChangeSampleKey()
        {
            if (_isChangingKey) return false;
            if (!HasSample || Clip?.SourceKey is not { Length: > 0 } key) return false;
            if (!MusicalKeyFormat.TryParse(key, out _, out _)) return false;
            var target = MusicalKeyFormat.Format(_targetKeyRootIndex, _targetKeyIsMinor);
            return !string.Equals(key, target, StringComparison.OrdinalIgnoreCase);
        }

        private async Task ChangeSampleKeyAsync()
        {
            if (_isChangingKey) return;
            if (Clip is not { SourceKey: { Length: > 0 } fromKey, Samples: { } buffer } clip) return;
            if (!MusicalKeyFormat.TryParse(fromKey, out var fromRoot, out _)) return;

            var targetKey = MusicalKeyFormat.Format(_targetKeyRootIndex, _targetKeyIsMinor);
            var semitones = MusicalKeyFormat.ShortestSemitoneDelta(fromRoot, _targetKeyRootIndex);

            _history.Capture("Change sample key");

            if (semitones != 0)
            {
                _isChangingKey = true;
                _keyChangeProgress = 0;
                OnPropertyChanged(nameof(IsChangingKey));
                OnPropertyChanged(nameof(KeyChangeProgress));
                OnPropertyChanged(nameof(CanChangeKey));
                ChangeKeyCommand.RaiseCanExecuteChanged();

                var progress = new Progress<double>(p =>
                {
                    _keyChangeProgress = p;
                    OnPropertyChanged(nameof(KeyChangeProgress));
                });

                AudioSampleBuffer shifted;
                try
                {
                    shifted = await Task.Run(() => AudioPitchOps.PitchShift(buffer, semitones, progress));
                }
                finally
                {
                    _isChangingKey = false;
                    OnPropertyChanged(nameof(IsChangingKey));
                    OnPropertyChanged(nameof(CanChangeKey));
                    ChangeKeyCommand.RaiseCanExecuteChanged();
                }

                // The selection may have changed to a different clip while we were shifting.
                if (!ReferenceEquals(clip, Clip)) return;

                ApplyBufferChange(buffer, shifted);
                clip.SourceKey = targetKey;
                Publish(clip);
                RebuildTargetKeyOptions();
                AfterBufferEdit(clip);
            }
            else
            {
                clip.SourceKey = targetKey;
                Publish(clip);
                RebuildTargetKeyOptions();
                OnPropertyChanged(nameof(KeyInfo));
                OnPropertyChanged(nameof(CanChangeKey));
                ChangeKeyCommand.RaiseCanExecuteChanged();
            }
        }

        private bool TryGetSelectionFrameRange(int sampleRate, out long startFrame, out long frameCount)
        {
            startFrame = 0;
            frameCount = 0;
            if (!HasSelection) return false;
            var a = Math.Min(_selectionStartSeconds, _selectionEndSeconds);
            var b = Math.Max(_selectionStartSeconds, _selectionEndSeconds);
            startFrame = (long)Math.Round(a * sampleRate);
            frameCount = (long)Math.Round((b - a) * sampleRate);
            return frameCount > 0;
        }

        private void ResetSelectionEditKnobs()
        {
            _selectionGainDb = 0;
            _selectionPan = 0;
            _selectionEditCaptured = false;
            OnPropertyChanged(nameof(SelectionGainDb));
            OnPropertyChanged(nameof(SelectionPan));
            OnPropertyChanged(nameof(SelectionGainText));
            OnPropertyChanged(nameof(SelectionPanText));
        }

        public void HandleCutKey() { if (HasSelection) Cut(); }
        public void HandleCopyKey() { if (HasSelection) Copy(); }
        public void HandlePasteKey() { if (_clipboard is not null && Clip is not null) Paste(); }
        public void HandleDeleteKey() { if (HasSelection) DeleteSelection(); }

        public void ResetZoom()
        {
            ZoomScale = 1.0;
        }

        private void Cut()
        {
            Copy();
            DeleteSelection();
        }

        private void Copy()
        {
            if (Clip?.Samples is not { } buffer || !HasSelection) return;
            var sampleRate = buffer.SampleRate;
            var a = Math.Min(_selectionStartSeconds, _selectionEndSeconds);
            var b = Math.Max(_selectionStartSeconds, _selectionEndSeconds);
            var startFrame = (long)Math.Round(a * sampleRate);
            var frameCount = (long)Math.Round((b - a) * sampleRate);
            if (frameCount <= 0) return;
            _clipboard = SampleEditOps.CopyRange(buffer, startFrame, frameCount);
            PasteCommand.RaiseCanExecuteChanged();
        }

        private void Paste()
        {
            if (Clip is not { Samples: { } buffer } clip || _clipboard is null) return;

            var sampleRate = buffer.SampleRate;
            var atSeconds = HasSelection
                ? Math.Min(_selectionStartSeconds, _selectionEndSeconds)
                : _trimStartSeconds;
            var atFrame = (long)Math.Round(atSeconds * sampleRate);
            _history.Capture("Paste sample");
            var pasted = SampleEditOps.InsertRange(buffer, atFrame, _clipboard);
            ApplyBufferChange(buffer, pasted);
            AfterBufferEdit(clip);
        }

        private void DeleteSelection()
        {
            if (Clip is not { Samples: { } buffer } clip || !HasSelection) return;

            var sampleRate = buffer.SampleRate;
            var a = Math.Min(_selectionStartSeconds, _selectionEndSeconds);
            var b = Math.Max(_selectionStartSeconds, _selectionEndSeconds);
            var startFrame = (long)Math.Round(a * sampleRate);
            var frameCount = (long)Math.Round((b - a) * sampleRate);
            if (frameCount <= 0) return;

            _history.Capture("Delete sample");
            var edited = SampleEditOps.DeleteRange(buffer, startFrame, frameCount);
            ApplyBufferChange(buffer, edited);
            ClearSelection();
            AfterBufferEdit(clip);
        }

        private TargetKeyOption? FindTargetKeyOption(int root, bool minor)
        {
            foreach (var option in _targetKeyOptions)
            {
                if (option.RootIndex == root && option.IsMinor == minor) return option;
            }

            return null;
        }

        private void RebuildTargetKeyOptions()
        {
            var options = new List<TargetKeyOption>(24);
            var hasSource = false;
            var fromRoot = 0;
            var fromMinor = false;
            if (Clip?.SourceKey is { Length: > 0 } sourceKey)
                hasSource = MusicalKeyFormat.TryParse(sourceKey, out fromRoot, out fromMinor);

            Span<(int Root, bool IsMinor)> keys = stackalloc (int, bool)[24];
            SampleKeyCompatibility.EnumerateTargets(keys);
            foreach (var (root, minor) in keys)
            {
                var key = MusicalKeyFormat.Format(root, minor);
                SampleKeyCompatibility.Fit fit;
                string hint;
                string label;

                if (hasSource)
                {
                    fit = SampleKeyCompatibility.Classify(fromRoot, fromMinor, root, minor);
                    var semi = MusicalKeyFormat.ShortestSemitoneDelta(fromRoot, root);
                    var shift = semi == 0 ? "no semitone shift" : semi > 0 ? $"+{semi} semitones" : $"{semi} semitones";
                    var fitText = SampleKeyCompatibility.FitLabel(fit);
                    hint = fitText.Length > 0
                        ? $"{fitText}; {shift}"
                        : $"{shift}; larger shifts may sound less smooth";
                    var marker = SampleKeyCompatibility.FitMarker(fit);
                    label = marker.Length > 0 ? $"{key}  {marker} {fitText}" : key;
                }
                else
                {
                    fit = SampleKeyCompatibility.Fit.Other;
                    hint = "Detect the sample key first for fit hints.";
                    label = key;
                }

                options.Add(new TargetKeyOption
                {
                    RootIndex = root,
                    IsMinor = minor,
                    Label = label,
                    Hint = hint,
                    Fit = fit
                });
            }

            _targetKeyOptions = options;
            _selectedTargetKey = FindTargetKeyOption(_targetKeyRootIndex, _targetKeyIsMinor);
            OnPropertyChanged(nameof(TargetKeyOptions));
            OnPropertyChanged(nameof(SelectedTargetKey));
        }

        private void ApplyBufferChange(AudioSampleBuffer oldBuffer, AudioSampleBuffer newBuffer)
        {
            var clips = ClipSharingOps.EnumerateClips(_project.Current);
            SampleEditOps.ReplaceSharedBufferSamples(clips, oldBuffer, newBuffer);
            _waveRevision++;
            OnPropertyChanged(nameof(Waveform));
            OnPropertyChanged(nameof(WaveRevision));
            OnPropertyChanged(nameof(DurationSeconds));
            OnPropertyChanged(nameof(SourceInfo));

            var generation = ++_waveformBuildGeneration;
            _ = RebuildWaveformAsync(newBuffer, generation);
        }

        private async Task RebuildWaveformAsync(AudioSampleBuffer buffer, int generation)
        {
            var waveform = await Task.Run(() => AudioWaveform.Build(buffer));
            if (generation != _waveformBuildGeneration) return;
            if (!ReferenceEquals(Clip?.Samples, buffer)) return;

            SampleEditOps.AssignSharedWaveform(ClipSharingOps.EnumerateClips(_project.Current), buffer, waveform);
            _waveRevision++;
            OnPropertyChanged(nameof(Waveform));
            OnPropertyChanged(nameof(WaveRevision));
        }

        private void AfterBufferEdit(Clip editedClip)
        {
            foreach (var clip in ClipSharingOps.EnumerateClips(_project.Current))
            {
                if (!ReferenceEquals(clip.Samples, editedClip.Samples)) continue;
                if (clip.StretchToTempo) Refit(clip);
                else
                {
                    var duration = PlayableDurationSeconds(clip);
                    if (duration > 0) clip.LengthBeats = duration * _transport.Tempo.BeatsPerMinute / 60.0;
                }

                _events.Publish(new ClipChangedEvent(clip));
            }

            ResetEditorBounds();
            RaiseAll();
        }

        private void ResetEditorBounds()
        {
            if (Clip?.Samples is not { } s)
            {
                _trimStartSeconds = 0;
                _trimEndSeconds = 0;
                return;
            }

            var full = s.FrameCount / (double)s.SampleRate;
            _trimStartSeconds = Clip.SourceOffsetSeconds;
            _trimEndSeconds = Clip.SourceLengthSeconds is { } len ? Clip.SourceOffsetSeconds + len : full;
            OnPropertyChanged(nameof(TrimStartSeconds));
            OnPropertyChanged(nameof(TrimEndSeconds));
            OnPropertyChanged(nameof(HighlightStartSeconds));
            OnPropertyChanged(nameof(HighlightEndSeconds));
        }

        private void ClearSelection()
        {
            _selectionStartSeconds = -1;
            _selectionEndSeconds = -1;
            OnPropertyChanged(nameof(SelectionStartSeconds));
            OnPropertyChanged(nameof(SelectionEndSeconds));
            OnEditorSelectionChanged();
        }

        private static double PlayableDurationSeconds(Clip clip)
        {
            if (clip.Samples is not { } s || s.SampleRate <= 0) return 0;
            var full = s.FrameCount / (double)s.SampleRate;
            return clip.SourceLengthSeconds ?? Math.Max(0.0, full - clip.SourceOffsetSeconds);
        }

        private void Refit(Clip clip)
        {
            var duration = PlayableDurationSeconds(clip);
            if (duration <= 0 || clip.SourceTempo is not { } source || source <= 0) return;
            var beats = TempoSync.MusicalBeats(duration, source, _transport.Tempo.BeatsPerMinute);
            if (beats > 0) clip.LengthBeats = beats;
        }

        private void Publish(Clip clip)
        {
            _events.Publish(new ClipChangedEvent(clip));
            RaiseAll();
        }

        private void OnSelectionChanged()
        {
            _audition.Stop();
            _isPlaying = false;
            _playheadSeconds = -1;
            ClearSelection();
            ResetEditorBounds();
            ResetZoom();
            if (Clip?.SourceKey is { Length: > 0 } key && MusicalKeyFormat.TryParse(key, out var root, out var minor))
            {
                _targetKeyRootIndex = root;
                _targetKeyIsMinor = minor;
            }
            RaiseAll();
        }

        private void RaiseAll()
        {
            RebuildTargetKeyOptions();
            OnPropertyChanged(nameof(HasSample));
            OnPropertyChanged(nameof(SampleName));
            OnPropertyChanged(nameof(NaturalBpm));
            OnPropertyChanged(nameof(StretchEnabled));
            OnPropertyChanged(nameof(PitchCorrected));
            OnPropertyChanged(nameof(HasTempo));
            OnPropertyChanged(nameof(StretchInfo));
            OnPropertyChanged(nameof(LengthInfo));
            OnPropertyChanged(nameof(SourceInfo));
            OnPropertyChanged(nameof(SharedInstanceCount));
            OnPropertyChanged(nameof(IsSharedSample));
            OnPropertyChanged(nameof(SharedWarning));
            OnPropertyChanged(nameof(Waveform));
            OnPropertyChanged(nameof(WaveRevision));
            OnPropertyChanged(nameof(DurationSeconds));
            OnPropertyChanged(nameof(GridSecondsPerBeat));
            OnPropertyChanged(nameof(GridBeatsPerBar));
            OnPropertyChanged(nameof(GridHasMusicalGrid));
            OnPropertyChanged(nameof(ContentWidth));
            OnPropertyChanged(nameof(ZoomScale));
            OnPropertyChanged(nameof(HoverInfo));
            OnPropertyChanged(nameof(TrimStartSeconds));
            OnPropertyChanged(nameof(TrimEndSeconds));
            OnPropertyChanged(nameof(HighlightStartSeconds));
            OnPropertyChanged(nameof(HighlightEndSeconds));
            OnPropertyChanged(nameof(SelectionStartSeconds));
            OnPropertyChanged(nameof(SelectionEndSeconds));
            OnPropertyChanged(nameof(SelectionInfo));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanEditSelectionPan));
            OnPropertyChanged(nameof(CanSwapChannels));
            OnPropertyChanged(nameof(SnapEnabled));
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(PlayButtonText));
            OnPropertyChanged(nameof(PlayheadSeconds));
            OnPropertyChanged(nameof(KeyInfo));
            OnPropertyChanged(nameof(CanChangeKey));
            OnPropertyChanged(nameof(KeyChangeHint));
            OnPropertyChanged(nameof(IsChangingKey));
            OnPropertyChanged(nameof(KeyChangeProgress));
            OnPropertyChanged(nameof(TargetKeyOptions));
            OnPropertyChanged(nameof(SelectedTargetKey));
            PasteCommand.RaiseCanExecuteChanged();
            SwapChannelsCommand.RaiseCanExecuteChanged();
            ReverseSelectionCommand.RaiseCanExecuteChanged();
            PlayStopCommand.RaiseCanExecuteChanged();
            DetectKeyCommand.RaiseCanExecuteChanged();
            RedetectTempoCommand.RaiseCanExecuteChanged();
            ChangeKeyCommand.RaiseCanExecuteChanged();
        }
    }
}
