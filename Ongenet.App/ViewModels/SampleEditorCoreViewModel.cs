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
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>
/// Shared waveform editor: trim, selection, cut/copy/paste, spectral overlay, audition, and destructive
/// sample edits. Used by the bottom-panel Sample Inspector and the standalone Audio Editor window.
/// </summary>
public class SampleEditorCoreViewModel : ViewModelBase
{
    private readonly ITransportService _transport;
    private readonly IEventAggregator _events;
    private readonly IProjectService _project;
    private readonly IHistoryService _history;
    private readonly IAuditionPlayer _audition;

    private Clip? _clip;
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
    private bool _spectralViewEnabled;
    private float[] _spectralMagnitudes = Array.Empty<float>();
    private int _spectralRevision;
    private bool _isPlaying;
    private double _playheadSeconds = -1;
    private double _auditionOffsetSeconds;
    private int _waveformBuildGeneration;
    private double _moveOriginalStart = -1;
    private double _moveOriginalEnd = -1;

    public SampleEditorCoreViewModel(
        ITransportService transport,
        IEventAggregator events,
        IProjectService project,
        IHistoryService history,
        IAuditionPlayer audition,
        IPlaybackClock clock)
    {
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
        NormalizeCommand = new RelayCommand(NormalizeSample, () => HasSample);
        FadeInSelectionCommand = new RelayCommand(() => ApplySelectionFade(inFade: true), () => HasSelection);
        FadeOutSelectionCommand = new RelayCommand(() => ApplySelectionFade(inFade: false), () => HasSelection);
        StretchSelectionCommand = new RelayCommand(() => _ = StretchSelectionAsync(), () => HasSelection);

        _transport.TempoChanged += _ => RaiseEditorProperties();
        _project.ProjectChanged += () => RaiseEditorProperties();
        _events.Subscribe<ClipChangedEvent>(e =>
        {
            if (ReferenceEquals(e.Clip, Clip)) RaiseEditorProperties();
        });

        _audition.Finished += () =>
        {
            _isPlaying = false;
            _playheadSeconds = -1;
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(PlayButtonText));
            OnPropertyChanged(nameof(PlayheadSeconds));
        };

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
    public RelayCommand NormalizeCommand { get; }
    public RelayCommand FadeInSelectionCommand { get; }
    public RelayCommand FadeOutSelectionCommand { get; }
    public RelayCommand StretchSelectionCommand { get; }

    public Clip? Clip
    {
        get => _clip;
        private set
        {
            if (ReferenceEquals(_clip, value)) return;
            _audition.Stop();
            _isPlaying = false;
            _playheadSeconds = -1;
            _clip = value;
            ClearSelection();
            ResetEditorBounds();
            ResetZoom();
            RebuildSpectral();
            RaiseEditorProperties();
        }
    }

    public void BindClip(Clip? clip)
    {
        if (clip is not null && !clip.IsAudio) clip = null;
        Clip = clip;
    }

    public bool HasSample => Clip is not null;
    public string SampleName => Clip?.Name ?? "No sample";

    public bool IsPlaying => _isPlaying;
    public string PlayButtonText => _isPlaying ? "Stop" : "Play";
    public double PlayheadSeconds => _playheadSeconds;

    public bool SnapEnabled
    {
        get => _snapEnabled;
        set => SetField(ref _snapEnabled, value);
    }

    public bool SpectralViewEnabled
    {
        get => _spectralViewEnabled;
        set
        {
            if (SetField(ref _spectralViewEnabled, value))
            {
                if (value) RebuildSpectral();
                OnPropertyChanged(nameof(SpectralMagnitudes));
                OnPropertyChanged(nameof(SpectralRevision));
            }
        }
    }

    public IReadOnlyList<float> SpectralMagnitudes => _spectralMagnitudes;
    public int SpectralRevision => _spectralRevision;

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
                var playable = PlayableDuration(clip);
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
            if (!HasSelection) return L("Status_NoSelection");
            var a = Math.Min(_selectionStartSeconds, _selectionEndSeconds);
            var b = Math.Max(_selectionStartSeconds, _selectionEndSeconds);
            return string.Create(CultureInfo.InvariantCulture, $"{a:0.###} s – {b:0.###} s");
        }
    }

    public void StopAudition()
    {
        _audition.Stop();
        _isPlaying = false;
        _playheadSeconds = -1;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(PlayheadSeconds));
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

    public void PlayFromSeconds(double seconds)
    {
        if (Clip?.Samples is not { } buffer || buffer.FrameCount <= 0) return;
        var duration = buffer.FrameCount / (double)buffer.SampleRate;
        seconds = Math.Clamp(seconds, 0, duration);
        _auditionOffsetSeconds = 0;
        _audition.Play(buffer, seconds);
        _isPlaying = true;
        _playheadSeconds = seconds;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(PlayheadSeconds));
    }

    public void HandleCutKey() { if (HasSelection) Cut(); }
    public void HandleCopyKey() { if (HasSelection) Copy(); }
    public void HandlePasteKey() { if (_clipboard is not null && Clip is not null) Paste(); }
    public void HandleDeleteKey() { if (HasSelection) DeleteSelection(); }

    public void ResetZoom() => ZoomScale = 1.0;

    protected void AfterBufferEdit(Clip editedClip)
    {
        foreach (var clip in ClipSharingOps.EnumerateClips(_project.Current))
        {
            if (!ReferenceEquals(clip.Samples, editedClip.Samples)) continue;
            if (clip.StretchToTempo) RefitClip(clip);
            else
            {
                var duration = PlayableDuration(clip);
                if (duration > 0) clip.LengthBeats = duration * _transport.Tempo.BeatsPerMinute / 60.0;
            }

            _events.Publish(new ClipChangedEvent(clip));
        }

        ResetEditorBounds();
        RaiseEditorProperties();
    }

    protected void ApplyBufferChange(AudioSampleBuffer oldBuffer, AudioSampleBuffer newBuffer)
    {
        var clips = ClipSharingOps.EnumerateClips(_project.Current);
        SampleEditOps.ReplaceSharedBufferSamples(clips, oldBuffer, newBuffer);
        _waveRevision++;
        RebuildSpectral();
        OnPropertyChanged(nameof(Waveform));
        OnPropertyChanged(nameof(WaveRevision));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(SourceInfo));

        var generation = ++_waveformBuildGeneration;
        _ = RebuildWaveformAsync(newBuffer, generation);
    }

    protected void PublishClip(Clip clip)
    {
        _events.Publish(new ClipChangedEvent(clip));
        RaiseEditorProperties();
    }

    protected void RaiseEditorProperties()
    {
        RebuildSpectral();
        OnPropertyChanged(nameof(HasSample));
        OnPropertyChanged(nameof(SampleName));
        OnPropertyChanged(nameof(SourceInfo));
        OnPropertyChanged(nameof(SharedInstanceCount));
        OnPropertyChanged(nameof(IsSharedSample));
        OnPropertyChanged(nameof(SharedWarning));
        OnPropertyChanged(nameof(Waveform));
        OnPropertyChanged(nameof(WaveRevision));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(GridSecondsPerBeat));
        OnPropertyChanged(nameof(GridBeatsPerBar));
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
        OnPropertyChanged(nameof(SpectralViewEnabled));
        OnPropertyChanged(nameof(SpectralMagnitudes));
        OnPropertyChanged(nameof(SpectralRevision));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(PlayheadSeconds));
        PasteCommand.RaiseCanExecuteChanged();
        SwapChannelsCommand.RaiseCanExecuteChanged();
        ReverseSelectionCommand.RaiseCanExecuteChanged();
        PlayStopCommand.RaiseCanExecuteChanged();
        NormalizeCommand.RaiseCanExecuteChanged();
        FadeInSelectionCommand.RaiseCanExecuteChanged();
        FadeOutSelectionCommand.RaiseCanExecuteChanged();
        StretchSelectionCommand.RaiseCanExecuteChanged();
    }

    private void TogglePlay()
    {
        if (_audition.IsPlaying)
        {
            StopAudition();
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

    private void NormalizeSample()
    {
        if (Clip is not { Samples: { } buffer } clip) return;
        _history.Capture("Normalize sample");
        SampleEditorService.Normalize(buffer);
        AfterBufferEdit(clip);
    }

    private void ApplySelectionFade(bool inFade)
    {
        if (Clip is not { Samples: { } buffer } clip || !HasSelection) return;
        if (!TryGetSelectionFrameRange(buffer.SampleRate, out var start, out var count)) return;
        _history.Capture(inFade ? "Fade in selection" : "Fade out selection");
        if (inFade) SampleEditorService.ApplyFadeIn(buffer, (int)start, (int)(start + count));
        else SampleEditorService.ApplyFadeOut(buffer, (int)start, (int)(start + count));
        AfterBufferEdit(clip);
    }

    private async Task StretchSelectionAsync()
    {
        if (!HasSelection || Clip is not { Samples: { } buffer } clip) return;
        if (!TryGetSelectionFrameRange(buffer.SampleRate, out var start, out var count)) return;

        var segment = SampleEditOps.CopyRange(buffer, start, count);
        if (segment.FrameCount <= 0) return;

        _history.Capture("Stretch selection");
        var stretched = await Task.Run(() => AudioPitchOps.PitchShift(
            new AudioSampleBuffer(segment.Samples, segment.Channels, segment.SampleRate), 0));
        if (!ReferenceEquals(clip, Clip)) return;

        var deleted = SampleEditOps.DeleteRange(buffer, start, count);
        var inserted = SampleEditOps.InsertRange(deleted, start,
            new AudioSegment(stretched.Samples, stretched.Channels, stretched.SampleRate));
        ApplyBufferChange(buffer, inserted);
        AfterBufferEdit(clip);
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

    private void ClearSelection()
    {
        _selectionStartSeconds = -1;
        _selectionEndSeconds = -1;
        OnPropertyChanged(nameof(SelectionStartSeconds));
        OnPropertyChanged(nameof(SelectionEndSeconds));
        OnEditorSelectionChanged();
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

    protected ITransportService Transport => _transport;
    protected IHistoryService History => _history;
    protected IProjectService Project => _project;

    protected void CaptureHistory(string label) => _history.Capture(label);

    protected static double PlayableDuration(Clip clip)
    {
        if (clip.Samples is not { } s || s.SampleRate <= 0) return 0;
        var full = s.FrameCount / (double)s.SampleRate;
        return clip.SourceLengthSeconds ?? Math.Max(0.0, full - clip.SourceOffsetSeconds);
    }

    private static double PlayableDurationSeconds(Clip clip) => PlayableDuration(clip);

    protected void RefitClip(Clip clip)
    {
        var duration = PlayableDuration(clip);
        if (duration <= 0 || clip.SourceTempo is not { } source || source <= 0) return;
        var beats = TempoSync.MusicalBeats(duration, source, _transport.Tempo.BeatsPerMinute);
        if (beats > 0) clip.LengthBeats = beats;
    }

    private async Task RebuildWaveformAsync(AudioSampleBuffer buffer, int generation)
    {
        var waveform = await Task.Run(() => AudioWaveform.Build(buffer));
        if (generation != _waveformBuildGeneration) return;
        if (!ReferenceEquals(Clip?.Samples, buffer)) return;

        SampleEditOps.AssignSharedWaveform(ClipSharingOps.EnumerateClips(_project.Current), buffer, waveform);
        _waveRevision++;
        RebuildSpectral();
        OnPropertyChanged(nameof(Waveform));
        OnPropertyChanged(nameof(WaveRevision));
    }

    private void RebuildSpectral()
    {
        if (!_spectralViewEnabled || Clip?.Samples is not { } buffer)
            _spectralMagnitudes = Array.Empty<float>();
        else
            _spectralMagnitudes = SpectralAnalyzer.ComputeMagnitudes(buffer);

        _spectralRevision++;
    }
}
