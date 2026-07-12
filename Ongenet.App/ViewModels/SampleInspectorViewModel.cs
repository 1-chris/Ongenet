using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Ongenet.App.Services;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>
/// Bottom-panel inspector for audio sample clips: tempo/stretch/warp controls plus the shared
/// waveform editor from <see cref="SampleEditorCoreViewModel"/>.
/// </summary>
public sealed class SampleInspectorViewModel : SampleEditorCoreViewModel
{
    private readonly ISelectionService _selection;
    private readonly OfflineRenderer _renderer;
    private readonly IAudioEngine _engine;

    private bool _isDetectingKey;
    private bool _isDetectingTempo;
    private int _targetKeyRootIndex;
    private bool _targetKeyIsMinor;
    private List<TargetKeyOption> _targetKeyOptions = new();
    private TargetKeyOption? _selectedTargetKey;
    private bool _isChangingKey;
    private double _keyChangeProgress;

    public SampleInspectorViewModel(
        ISelectionService selection,
        ITransportService transport,
        IEventAggregator events,
        IProjectService project,
        IHistoryService history,
        IAuditionPlayer audition,
        IPlaybackClock clock,
        OfflineRenderer renderer,
        IAudioEngine engine)
        : base(transport, events, project, history, audition, clock)
    {
        _selection = selection;
        _renderer = renderer;
        _engine = engine;

        DetectKeyCommand = new RelayCommand(() => _ = DetectKeyAsync(), () => HasSample && !_isDetectingKey);
        RedetectTempoCommand = new RelayCommand(() => _ = RedetectTempoAsync(), () => HasSample && !_isDetectingTempo);
        ChangeKeyCommand = new RelayCommand(() => _ = ChangeSampleKeyAsync(), CanChangeSampleKey);
        FlattenWarpCommand = new RelayCommand(() => _ = FlattenWarpAsync(),
            () => HasSample && (Clip?.WarpMarkers.Count > 0 || Clip?.StretchToTempo == true));

        _selection.SelectionChanged += OnSelectionChanged;
        OnSelectionChanged();
    }

    public RelayCommand DetectKeyCommand { get; }
    public RelayCommand RedetectTempoCommand { get; }
    public RelayCommand ChangeKeyCommand { get; }
    public RelayCommand FlattenWarpCommand { get; }

    public Array WarpModes => Enum.GetValues<WarpMode>();

    public WarpMode WarpMode
    {
        get => Clip?.WarpMode ?? WarpMode.Beats;
        set
        {
            if (Clip is not { } clip || clip.WarpMode == value) return;
            CaptureHistory("Change warp mode");
            clip.WarpMode = value;
            PublishClip(clip);
            FlattenWarpCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasWarpMarkers => Clip is { WarpMarkers.Count: > 0 };

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

    public bool IsChangingKey => _isChangingKey;
    public double KeyChangeProgress => _keyChangeProgress;

    public double NaturalBpm
    {
        get => Clip?.SourceTempo ?? 0;
        set
        {
            if (Clip is not { } clip) return;
            clip.SourceTempo = value > 0 ? value : null;
            if (clip.StretchToTempo) RefitClip(clip);
            PublishClip(clip);
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
                if (clip.SourceTempo is not { } t || t <= 0) clip.SourceTempo = Transport.Tempo.BeatsPerMinute;
                clip.StretchToTempo = true;
                RefitClip(clip);
            }
            else
            {
                clip.StretchToTempo = false;
                var duration = PlayableDuration(clip);
                if (duration > 0) clip.LengthBeats = duration * Transport.Tempo.BeatsPerMinute / 60.0;
            }

            PublishClip(clip);
        }
    }

    public bool PitchCorrected
    {
        get => Clip?.PitchCorrected ?? false;
        set
        {
            if (Clip is not { } clip) return;
            clip.PitchCorrected = value;
            PublishClip(clip);
        }
    }

    public bool HasTempo => Clip is { SourceTempo: > 0 };

    public string KeyInfo
    {
        get
        {
            if (_isDetectingKey) return L("Status_Detecting");
            return Clip?.SourceKey is { Length: > 0 } key ? key : L("Status_EmDash");
        }
    }

    public bool CanChangeKey => CanChangeSampleKey();

    public string StretchInfo
    {
        get
        {
            if (Clip is not { } clip) return string.Empty;
            if (!clip.StretchToTempo) return L("Status_NativeNotStretched");
            var duration = PlayableDuration(clip);
            var factor = TempoSync.Stretch(duration, Transport.Tempo.BeatsPerMinute, clip.LengthBeats);
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

    private void OnSelectionChanged()
    {
        StopAudition();
        BindClip(_selection.SelectedClip is { IsAudio: true } clip ? clip : null);
        if (Clip?.SourceKey is { Length: > 0 } key && MusicalKeyFormat.TryParse(key, out var root, out var minor))
        {
            _targetKeyRootIndex = root;
            _targetKeyIsMinor = minor;
        }

        RaiseInspectorProperties();
    }

    private void RaiseInspectorProperties()
    {
        RebuildTargetKeyOptions();
        OnPropertyChanged(nameof(NaturalBpm));
        OnPropertyChanged(nameof(StretchEnabled));
        OnPropertyChanged(nameof(PitchCorrected));
        OnPropertyChanged(nameof(WarpMode));
        OnPropertyChanged(nameof(WarpModes));
        OnPropertyChanged(nameof(HasWarpMarkers));
        OnPropertyChanged(nameof(HasTempo));
        OnPropertyChanged(nameof(StretchInfo));
        OnPropertyChanged(nameof(LengthInfo));
        OnPropertyChanged(nameof(KeyInfo));
        OnPropertyChanged(nameof(CanChangeKey));
        OnPropertyChanged(nameof(KeyChangeHint));
        OnPropertyChanged(nameof(IsChangingKey));
        OnPropertyChanged(nameof(KeyChangeProgress));
        OnPropertyChanged(nameof(TargetKeyOptions));
        OnPropertyChanged(nameof(SelectedTargetKey));
        DetectKeyCommand.RaiseCanExecuteChanged();
        RedetectTempoCommand.RaiseCanExecuteChanged();
        ChangeKeyCommand.RaiseCanExecuteChanged();
        FlattenWarpCommand.RaiseCanExecuteChanged();
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

        CaptureHistory("Detect sample key");
        clip.SourceKey = key.Length > 0 ? key : null;
        if (key.Length > 0 && MusicalKeyFormat.TryParse(key, out var root, out var minor))
        {
            _targetKeyRootIndex = root;
            _targetKeyIsMinor = minor;
        }
        RebuildTargetKeyOptions();
        PublishClip(clip);

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
            CaptureHistory("Re-detect tempo");
            NaturalBpm = detected.Value;
        }

        _isDetectingTempo = false;
        RedetectTempoCommand.RaiseCanExecuteChanged();
    }

    private async Task FlattenWarpAsync()
    {
        if (Clip is not { Samples: not null } clip) return;
        var owner = Project.Current.Tracks.FirstOrDefault(t => t.Clips.Contains(clip));
        if (owner is null) return;

        CaptureHistory("Flatten warp");
        var project = Project.Current;
        var bpm = Transport.Tempo.BeatsPerMinute;
        var scope = ClipRenderScope.ForClip(project, owner, clip);

        var baked = await Task.Run(() =>
            _renderer.RenderScopeToBuffer(project, _engine.Format, bpm, scope));
        if (!ReferenceEquals(clip, Clip)) return;

        clip.WarpMarkers.Clear();
        clip.StretchToTempo = false;
        clip.PitchCorrected = false;
        clip.SourceOffsetSeconds = 0;
        clip.SourceLengthSeconds = null;
        clip.LengthBeats = baked.FrameCount * 60.0 / (bpm * baked.SampleRate);
        clip.Samples = baked;
        clip.Waveform = await Task.Run(() => AudioWaveform.Build(baked));

        PublishClip(clip);
        RaiseEditorProperties();
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

        CaptureHistory("Change sample key");

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

            if (!ReferenceEquals(clip, Clip)) return;

            ApplyBufferChange(buffer, shifted);
            clip.SourceKey = targetKey;
            PublishClip(clip);
            RebuildTargetKeyOptions();
            AfterBufferEdit(clip);
        }
        else
        {
            clip.SourceKey = targetKey;
            PublishClip(clip);
            RebuildTargetKeyOptions();
            OnPropertyChanged(nameof(KeyInfo));
            OnPropertyChanged(nameof(CanChangeKey));
            ChangeKeyCommand.RaiseCanExecuteChanged();
        }
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
}
