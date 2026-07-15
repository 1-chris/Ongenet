using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.Timeline;

namespace Ongenet.App.ViewModels;

public sealed record DeliveryPlatformOption(string? Name, string Label, double Lufs, double DbTp);
public sealed record DitherModeOption(DitherMode Mode, string Label);
public sealed record AlbumLoudnessRow(
    string TrackName, string IntegratedLufs, string Lra, string TruePeak, string OffsetDb);

/// <summary>View model for the export dialog: kind, bit depth, region, and stem selection.</summary>
public sealed class ExportViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IAudioEngine _engine;
    private readonly ExportService _export;
    private readonly StemSeparationService _stemSeparation;
    private readonly ISelectionService _selection;
    private readonly TimelineViewModel _timeline;
    private readonly IVideoWaveformCacheService _waveformCache;
    private readonly IVideoRenderQueueService _renderQueue;
    private readonly IMasteringDeliveryTarget _deliveryTarget;
    private readonly IPlaybackClock _clock;
    private bool _previewActive;
    private bool _syncingDeliveryTarget;
    private ExportKind _kind = ExportKind.Master;
    private SurroundFormat _surround = SurroundFormat.Stereo;
    private int _bitDepth = 24;
    private bool _includeMasterFx = true;
    private bool _bypassMasterFx;
    private bool _applyDither;
    private DitherMode _ditherMode = DitherMode.Tpdf;
    private DitherModeOption _selectedDitherMode = null!;
    private bool _matchAlbumLoudness;
    private bool _exportComparisonPair;
    private bool _analyzeLoudness = true;
    private bool _normalizeLoudness = true;
    private string? _deliveryPlatform = "Spotify";
    private DeliveryPlatformOption _selectedDeliveryPlatform = null!;
    private double _targetIntegratedLufs = -14;
    private double _targetTruePeakDbTp = -1;
    private int _targetSampleRate;
    private string _loudnessReportText = string.Empty;
    private string _reportIntegratedLufs = string.Empty;
    private string _reportLra = string.Empty;
    private string _reportTruePeak = string.Empty;
    private bool _reportWithinTarget;
    private bool _showStructuredLoudnessReport;
    private bool _isAnalyzingAlbumLoudness;
    private double _regionStartBeat;
    private double _regionEndBeat = 16;
    private bool _isExporting;
    private double _progress;
    private string _status = string.Empty;
    private bool _exportTimelineXml;
    private bool _muxWithVideo;
    private bool _composeVideo;
    private Ongenet.Core.Models.Media.VideoLayer? _selectedVideoLayer;
    private bool _exportAdmBwf;
    private bool _exportAafOmf;
    private bool _isVideoExportMode;
    private bool _exportInBackground;
    private ArrangementMarker? _selectedMarker;

    public ExportViewModel(IProjectService project, ITransportService transport, IAudioEngine engine,
        ExportService export, StemSeparationService stemSeparation, ISelectionService selection,
        TimelineViewModel timeline, IVideoWaveformCacheService waveformCache,
        IVideoRenderQueueService renderQueue, IMasteringDeliveryTarget deliveryTarget,
        IPlaybackClock clock)
    {
        _project = project;
        _transport = transport;
        _engine = engine;
        _export = export;
        _stemSeparation = stemSeparation;
        _selection = selection;
        _timeline = timeline;
        _waveformCache = waveformCache;
        _renderQueue = renderQueue;
        _deliveryTarget = deliveryTarget;
        _clock = clock;
        _renderQueue.JobsChanged += () => OnPropertyChanged(nameof(RenderQueueStatus));

        _regionStartBeat = transport.IsLoopActive ? transport.LoopStart : 0;
        _regionEndBeat = transport.IsLoopActive ? transport.LoopEnd
            : project.Current.BarCount * Math.Max(1, project.Current.TimeSignature.Numerator);

        foreach (var track in _project.Current.Tracks.Where(t => !t.IsBus))
            StemTracks.Add(new ExportStemTrackViewModel(track, true));

        foreach (var marker in _project.Current.Markers.OrderBy(m => m.Beat))
            Markers.Add(marker);

        foreach (var layer in _project.Current.VideoLayers.Where(l => l.HasVideoItem))
            VideoLayers.Add(layer);

        DeliveryPlatforms = DeliveryPlatformPresets.All
            .Select(p => new DeliveryPlatformOption(p.Name,
                DeliveryPlatformPresets.FormatLabel(p.Name, p.Lufs, p.DbTp), p.Lufs, p.DbTp))
            .Append(new DeliveryPlatformOption("Custom", "Custom", -14, -1))
            .ToArray();
        DitherModes =
        [
            new DitherModeOption(DitherMode.Tpdf, "TPDF"),
            new DitherModeOption(DitherMode.NoiseShaped, "Noise-shaped")
        ];
        _selectedDitherMode = DitherModes[0];
        SyncFromDeliveryTarget();

        ExportCommand = new RelayCommand(() => _ = ExportAsync(), () => CanExport);
        SeparateSelectedClipCommand = new RelayCommand(
            () => _ = SeparateSelectedClipAsync(),
            () => CanSeparateSelectedClip);
        AnalyzeAlbumLoudnessCommand = new RelayCommand(
            () => _ = AnalyzeAlbumLoudnessAsync(),
            () => CanAnalyzeAlbumLoudness);
    }

    public void BeginPreview()
    {
        SyncFromDeliveryTarget();
        if (_previewActive) return;
        _previewActive = true;
        _clock.Tick += OnPreviewTick;
    }

    public void EndPreview()
    {
        if (!_previewActive) return;
        _previewActive = false;
        _clock.Tick -= OnPreviewTick;
    }

    private void OnPreviewTick()
    {
        OnPropertyChanged(nameof(NormalizePreviewText));
        OnPropertyChanged(nameof(RelimitWarningText));
        OnPropertyChanged(nameof(ShowRelimitWarning));
    }

    private void SyncFromDeliveryTarget()
    {
        _syncingDeliveryTarget = true;
        try
        {
            _deliveryPlatform = _deliveryTarget.PlatformName;
            _targetIntegratedLufs = _deliveryTarget.TargetIntegratedLufs;
            _targetTruePeakDbTp = _deliveryTarget.TargetTruePeakDbTp;
            _selectedDeliveryPlatform = DeliveryPlatforms.FirstOrDefault(p =>
                string.Equals(p.Name, _deliveryTarget.PlatformName, StringComparison.OrdinalIgnoreCase))
                ?? DeliveryPlatforms[^1];
            OnPropertyChanged(nameof(SelectedDeliveryPlatform));
            OnPropertyChanged(nameof(DeliveryPlatform));
            OnPropertyChanged(nameof(TargetIntegratedLufs));
            OnPropertyChanged(nameof(TargetTruePeakDbTp));
            OnPropertyChanged(nameof(IsCustomDeliveryPlatform));
            OnPreviewTick();
        }
        finally
        {
            _syncingDeliveryTarget = false;
        }
    }

    /// <summary>Presets the dialog for composited MP4 export from the title bar Export video menu.</summary>
    public void ApplyVideoExportPreset()
    {
        IsVideoExportMode = true;
        Kind = ExportKind.Master;
        ExportTimelineXml = false;
        ExportAdmBwf = false;
        ExportAafOmf = false;
        MuxWithVideo = false;
        ComposeVideo = true;
    }

    /// <summary>Resets video-only mode when opening the standard audio export dialog.</summary>
    public void ApplyAudioExportPreset() => IsVideoExportMode = false;

    public bool IsVideoExportMode
    {
        get => _isVideoExportMode;
        private set
        {
            if (!SetField(ref _isVideoExportMode, value)) return;
            OnPropertyChanged(nameof(ShowAudioExportOptions));
            OnPropertyChanged(nameof(ShowVideoCompositorPanel));
            OnPropertyChanged(nameof(ShowIncludeMasterFx));
            OnPropertyChanged(nameof(ShowDeliveryHint));
            OnPropertyChanged(nameof(ShowAdmExportOption));
        }
    }

    public bool ShowAudioExportOptions => !IsVideoExportMode;
    public bool ShowVideoCompositorPanel => IsVideoExportMode || ShowVideoExport;

    public string StemSeparationBackend => _stemSeparation.IsDemucsAvailable
        ? "demucs available — choose quality below"
        : _stemSeparation.IsFfmpegAvailable
            ? "built-in heuristic (install demucs for higher quality)"
            : "built-in heuristic";

    public string DemucsInstallHint => StemSeparationService.DemucsInstallHint;

    public bool IsDemucsAvailable => _stemSeparation.IsDemucsAvailable;

    public bool ShowDemucsHint => !IsDemucsAvailable;

    public StemSeparationQuality[] StemQualities { get; } = Enum.GetValues<StemSeparationQuality>();

    private StemSeparationQuality _stemQuality = StemSeparationQuality.Fast;

    public StemSeparationQuality StemQuality
    {
        get => _stemQuality;
        set => SetField(ref _stemQuality, value);
    }

    private bool _isSeparatingStems;

    public bool IsSeparatingStems
    {
        get => _isSeparatingStems;
        private set
        {
            if (SetField(ref _isSeparatingStems, value))
            {
                OnPropertyChanged(nameof(CanSeparateSelectedClip));
                SeparateSelectedClipCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double StemSeparationProgress
    {
        get => _stemSeparationProgress;
        private set => SetField(ref _stemSeparationProgress, value);
    }

    private double _stemSeparationProgress;

    public bool CanSeparateSelectedClip => !IsSeparatingStems && _selection.SelectedClip is { IsAudio: true, Samples: not null }
        && _selection.SelectedClip.Samples!.FrameCount > 0;

    public RelayCommand SeparateSelectedClipCommand { get; }
    public RelayCommand AnalyzeAlbumLoudnessCommand { get; }

    public ExportKind[] ExportKinds { get; } = Enum.GetValues<ExportKind>();
    public SurroundFormat[] SurroundFormats { get; } = Enum.GetValues<SurroundFormat>();
    public ExportAudioFormat[] AudioFormats { get; } = Enum.GetValues<ExportAudioFormat>();
    public IReadOnlyList<DitherModeOption> DitherModes { get; }
    public int[] BitDepths { get; } = { 16, 24, 32 };

    public ObservableCollection<ArrangementMarker> Markers { get; } = new();
    public ObservableCollection<Ongenet.Core.Models.Media.VideoLayer> VideoLayers { get; } = new();

    public bool HasMarkers => Markers.Count > 0;
    public bool HasMuxVideoLayers => VideoLayers.Count > 0;
    public bool CanComposeVideo => _project.Current.VideoLayers.Count > 0;
    public bool ShowVideoExport => _project.Current.VideoLayers.Count > 0;
    public string VideoExportFpsLabel => $"{_project.Current.VideoExportFps:0} fps";
    public string VideoCanvasSizeLabel =>
        $"{_project.Current.VideoCanvasWidth} × {_project.Current.VideoCanvasHeight}";
    public string RenderQueueStatus
    {
        get
        {
            var job = _renderQueue.Jobs.LastOrDefault();
            return job is null ? string.Empty : $"{job.Status} ({job.Progress * 100:0}%)";
        }
    }

    public bool ExportInBackground
    {
        get => _exportInBackground;
        set => SetField(ref _exportInBackground, value);
    }

    public bool ShowBackgroundExportOption => IsVideoExportMode && ComposeVideo;

    public bool MuxWithVideo
    {
        get => _muxWithVideo;
        set
        {
            if (!SetField(ref _muxWithVideo, value)) return;
            OnPropertyChanged(nameof(SuggestedFileExtension));
        }
    }

    public bool ComposeVideo
    {
        get => _composeVideo;
        set
        {
            if (!SetField(ref _composeVideo, value)) return;
            OnPropertyChanged(nameof(SuggestedFileExtension));
            OnPropertyChanged(nameof(ShowBackgroundExportOption));
        }
    }

    public Ongenet.Core.Models.Media.VideoLayer? SelectedVideoLayer
    {
        get => _selectedVideoLayer;
        set => SetField(ref _selectedVideoLayer, value);
    }

    public bool ExportTimelineXml
    {
        get => _exportTimelineXml;
        set
        {
            if (!SetField(ref _exportTimelineXml, value)) return;
            OnPropertyChanged(nameof(SuggestedFileExtension));
        }
    }

    /// <summary>Obsolete property name — use <see cref="ExportTimelineXml"/>.</summary>
    [Obsolete("Use ExportTimelineXml — export is custom timeline XML, not binary AAF.")]
    public bool ExportAaf
    {
        get => ExportTimelineXml;
        set => ExportTimelineXml = value;
    }

    public bool ExportAdmBwf
    {
        get => _exportAdmBwf;
        set
        {
            if (!SetField(ref _exportAdmBwf, value)) return;
            OnPropertyChanged(nameof(SuggestedFileExtension));
        }
    }

    public bool ExportAafOmf
    {
        get => _exportAafOmf;
        set
        {
            if (!SetField(ref _exportAafOmf, value)) return;
            OnPropertyChanged(nameof(SuggestedFileExtension));
        }
    }

    public bool ShowAdmOption => Surround != SurroundFormat.Stereo;
    public bool ShowAdmExportOption => ShowAudioExportOptions && ShowAdmOption;

    public ArrangementMarker? SelectedMarker
    {
        get => _selectedMarker;
        set
        {
            if (!SetField(ref _selectedMarker, value) || value is null) return;
            Kind = ExportKind.Region;
            RegionStartBeat = value.Beat;
            var ordered = _project.Current.Markers.OrderBy(m => m.Beat).ToList();
            var idx = ordered.FindIndex(m => m.Id == value.Id);
            RegionEndBeat = idx >= 0 && idx + 1 < ordered.Count
                ? ordered[idx + 1].Beat
                : _project.Current.BarCount * Math.Max(1, _project.Current.TimeSignature.Numerator);
        }
    }

    public string SuggestedFileExtension => ExportTimelineXml ? TimelineXmlExporter.DefaultExtension
        : ExportAdmBwf ? AdmBwfExporter.DefaultExtension
        : ExportAafOmf ? AafOmffExporter.AafExtension
        : MuxWithVideo || ComposeVideo ? "mp4"
        : AudioFormat.GetExtension();

    private ExportAudioFormat _audioFormat = ExportAudioFormat.Wav;

    public ExportKind Kind
    {
        get => _kind;
        set
        {
            if (!SetField(ref _kind, value)) return;
            OnPropertyChanged(nameof(IsRegionVisible));
            OnPropertyChanged(nameof(IsStemsVisible));
            OnPropertyChanged(nameof(ShowStemsAlbumSection));
            OnPropertyChanged(nameof(ShowIncludeMasterFx));
            OnPropertyChanged(nameof(ShowBypassMasterFx));
            OnPropertyChanged(nameof(ShowDeliveryHint));
            OnPropertyChanged(nameof(ShowDitherOption));
            OnPropertyChanged(nameof(ShowDitherWarning));
            OnPropertyChanged(nameof(ShowLoudnessOptions));
            OnPropertyChanged(nameof(ShowMatchAlbumLoudness));
            OnPropertyChanged(nameof(ShowComparisonPair));
            OnPropertyChanged(nameof(MasterFxChainPreview));
            OnPropertyChanged(nameof(ShowMasterFxPreview));
            OnPropertyChanged(nameof(ShowAlbumLoudnessPreview));
            OnPropertyChanged(nameof(CanAnalyzeAlbumLoudness));
            OnPropertyChanged(nameof(CanExport));
            AnalyzeAlbumLoudnessCommand.RaiseCanExecuteChanged();
        }
    }

    public int BitDepth
    {
        get => _bitDepth;
        set
        {
            if (!SetField(ref _bitDepth, value)) return;
            OnPropertyChanged(nameof(ShowDitherOption));
            OnPropertyChanged(nameof(ShowDitherWarning));
            OnPropertyChanged(nameof(DitherAuditionPreviewText));
        }
    }

    /// <summary>When exporting stems, bake Master-track insert FX into each stem (default on).</summary>
    public bool IncludeMasterFx
    {
        get => _includeMasterFx;
        set
        {
            if (!SetField(ref _includeMasterFx, value)) return;
            OnPropertyChanged(nameof(ShowMasterFxPreview));
            OnPropertyChanged(nameof(MasterFxChainPreview));
        }
    }

    public bool BypassMasterFx
    {
        get => _bypassMasterFx;
        set => SetField(ref _bypassMasterFx, value);
    }

    public bool ApplyDither
    {
        get => _applyDither;
        set
        {
            if (!SetField(ref _applyDither, value)) return;
            OnPropertyChanged(nameof(ShowDitherWarning));
            OnPropertyChanged(nameof(DitherAuditionPreviewText));
        }
    }

    public DitherMode DitherMode
    {
        get => _ditherMode;
        set => SetField(ref _ditherMode, value);
    }

    public DitherModeOption SelectedDitherMode
    {
        get => _selectedDitherMode;
        set
        {
            if (value is null || !SetField(ref _selectedDitherMode, value)) return;
            DitherMode = value.Mode;
        }
    }

    public bool MatchAlbumLoudness
    {
        get => _matchAlbumLoudness;
        set
        {
            if (!SetField(ref _matchAlbumLoudness, value)) return;
            OnPropertyChanged(nameof(ShowAlbumLoudnessPreview));
            if (value && CanAnalyzeAlbumLoudness)
                _ = AnalyzeAlbumLoudnessAsync();
        }
    }

    public bool ExportComparisonPair
    {
        get => _exportComparisonPair;
        set => SetField(ref _exportComparisonPair, value);
    }

    public bool AnalyzeLoudness
    {
        get => _analyzeLoudness;
        set
        {
            if (!SetField(ref _analyzeLoudness, value)) return;
            OnPropertyChanged(nameof(MetadataPreviewText));
        }
    }

    public bool NormalizeLoudness
    {
        get => _normalizeLoudness;
        set
        {
            if (!SetField(ref _normalizeLoudness, value)) return;
            OnPropertyChanged(nameof(NormalizePreviewText));
        }
    }

    public string? DeliveryPlatform
    {
        get => _deliveryPlatform;
        set
        {
            if (!SetField(ref _deliveryPlatform, value)) return;
            OnPropertyChanged(nameof(IsCustomDeliveryPlatform));
        }
    }

    public IReadOnlyList<DeliveryPlatformOption> DeliveryPlatforms { get; }
    public DeliveryPlatformOption SelectedDeliveryPlatform
    {
        get => _selectedDeliveryPlatform;
        set
        {
            if (value is null || !SetField(ref _selectedDeliveryPlatform, value)) return;
            DeliveryPlatform = value.Name;
            if (!IsCustomDeliveryPlatform)
            {
                TargetIntegratedLufs = value.Lufs;
                TargetTruePeakDbTp = value.DbTp;
            }
            if (!_syncingDeliveryTarget)
                _deliveryTarget.ApplyPlatform(value.Name);
            OnPropertyChanged(nameof(IsCustomDeliveryPlatform));
            OnPropertyChanged(nameof(NormalizePreviewText));
        }
    }
    public bool IsCustomDeliveryPlatform =>
        string.IsNullOrWhiteSpace(DeliveryPlatform) ||
        string.Equals(DeliveryPlatform, "Custom", StringComparison.OrdinalIgnoreCase);
    public double TargetIntegratedLufs
    {
        get => _targetIntegratedLufs;
        set
        {
            if (!SetField(ref _targetIntegratedLufs, value)) return;
            if (!_syncingDeliveryTarget && IsCustomDeliveryPlatform)
                _deliveryTarget.TargetIntegratedLufs = value;
            OnPropertyChanged(nameof(NormalizePreviewText));
            OnPropertyChanged(nameof(RelimitWarningText));
            OnPropertyChanged(nameof(ShowRelimitWarning));
        }
    }
    public double TargetTruePeakDbTp
    {
        get => _targetTruePeakDbTp;
        set
        {
            if (!SetField(ref _targetTruePeakDbTp, value)) return;
            if (!_syncingDeliveryTarget && IsCustomDeliveryPlatform)
                _deliveryTarget.TargetTruePeakDbTp = value;
            OnPropertyChanged(nameof(RelimitWarningText));
            OnPropertyChanged(nameof(ShowRelimitWarning));
            OnPropertyChanged(nameof(NormalizePreviewText));
        }
    }
    public int[] TargetSampleRates { get; } = { 0, 44100, 48000 };
    public int TargetSampleRate
    {
        get => _targetSampleRate;
        set => SetField(ref _targetSampleRate, value);
    }
    public string NormalizePreviewText
    {
        get
        {
            var current = _engine.MasterIntegratedLufs;
            if (double.IsNegativeInfinity(current))
                return "Play the project to estimate normalization gain.";
            var gain = TargetIntegratedLufs - current;
            var estimatedTp = _engine.MasterTruePeakMaxDbTp + gain;
            var relimit = estimatedTp > TargetTruePeakDbTp + 0.05
                ? " · will re-limit at delivery ceiling"
                : string.Empty;
            return $"Estimated normalization gain: {gain:+0.0;-0.0;0.0} dB · post TP ≈ {estimatedTp:0.0} dBTP{relimit}";
        }
    }
    public bool ShowRelimitWarning
    {
        get
        {
            var current = _engine.MasterIntegratedLufs;
            if (double.IsNegativeInfinity(current)) return false;
            var estimatedGain = TargetIntegratedLufs - current;
            var estimatedTp = _engine.MasterTruePeakMaxDbTp + estimatedGain;
            return estimatedGain > 0.05 && estimatedTp > TargetTruePeakDbTp + 0.05;
        }
    }
    public string RelimitWarningText => ShowRelimitWarning
        ? "Normalization gain may exceed the true-peak ceiling; the export will be re-limited."
        : string.Empty;
    public string LoudnessReportText
    {
        get => _loudnessReportText;
        private set => SetField(ref _loudnessReportText, value);
    }
    public string ReportIntegratedLufs
    {
        get => _reportIntegratedLufs;
        private set => SetField(ref _reportIntegratedLufs, value);
    }
    public string ReportLra
    {
        get => _reportLra;
        private set => SetField(ref _reportLra, value);
    }
    public string ReportTruePeak
    {
        get => _reportTruePeak;
        private set => SetField(ref _reportTruePeak, value);
    }
    public bool ReportWithinTarget
    {
        get => _reportWithinTarget;
        private set
        {
            if (!SetField(ref _reportWithinTarget, value)) return;
            OnPropertyChanged(nameof(ReportTargetStatus));
        }
    }
    public string ReportTargetStatus => ReportWithinTarget ? "Within target" : "Outside target";
    public bool ShowStructuredLoudnessReport
    {
        get => _showStructuredLoudnessReport;
        private set => SetField(ref _showStructuredLoudnessReport, value);
    }
    public string DitherAuditionPreviewText => ApplyDither
        ? $"Dither adds a very low-level noise floor before {BitDepth}-bit quantization; TPDF is neutral, noise-shaped moves energy upward."
        : string.Empty;
    public string MetadataPreviewText => AnalyzeLoudness
        ? "Metadata: WAV writes RIFF loudness information; encoded formats include ReplayGain and R128 track-gain tags. JSON and text reports are written beside the deliverable."
        : "Enable loudness analysis to write RIFF/R128 metadata and report sidecars.";

    public bool ShowIncludeMasterFx => ShowAudioExportOptions && Kind == ExportKind.Stems;
    public bool ShowBypassMasterFx => ShowAudioExportOptions && Kind is ExportKind.Master or ExportKind.Region;
    public bool ShowDitherOption => ShowAudioExportOptions && BitDepth == 16;
    public bool ShowDitherWarning => ShowDitherOption && !ApplyDither;
    public bool ShowLoudnessOptions => ShowAudioExportOptions;
    public bool ShowMatchAlbumLoudness =>
        ShowAudioExportOptions && Kind is ExportKind.Stems or ExportKind.Batch;
    public bool ShowComparisonPair => ShowBypassMasterFx;
    public bool ShowMasterFxPreview => ShowIncludeMasterFx && IncludeMasterFx;

    public string MasterFxChainPreview
    {
        get
        {
            var master = _project.Current.Master;
            if (master is null || master.Effects.Count == 0) return "";
            return string.Join(" → ", master.Effects.Select(e => e.Name));
        }
    }

    /// <summary>Hint for streaming-safe Peak Limiter ceiling when bouncing a WAV master.</summary>
    public bool ShowDeliveryHint =>
        ShowAudioExportOptions && Kind == ExportKind.Master && AudioFormat == ExportAudioFormat.Wav;

    public SurroundFormat Surround
    {
        get => _surround;
        set
        {
            if (!SetField(ref _surround, value)) return;
            OnPropertyChanged(nameof(ShowAdmOption));
        }
    }

    public ExportAudioFormat AudioFormat
    {
        get => _audioFormat;
        set
        {
            if (!SetField(ref _audioFormat, value)) return;
            OnPropertyChanged(nameof(SuggestedFileExtension));
            OnPropertyChanged(nameof(ShowDeliveryHint));
        }
    }

    public double RegionStartBeat
    {
        get => _regionStartBeat;
        set => SetField(ref _regionStartBeat, value);
    }

    public double RegionEndBeat
    {
        get => _regionEndBeat;
        set => SetField(ref _regionEndBeat, value);
    }

    public bool IsRegionVisible => Kind == ExportKind.Region;
    public bool IsStemsVisible => Kind == ExportKind.Stems;
    public bool ShowStemsAlbumSection => Kind is ExportKind.Stems or ExportKind.Batch;

    public ObservableCollection<ExportStemTrackViewModel> StemTracks { get; } = new();
    public ObservableCollection<AlbumLoudnessRow> AlbumLoudnessRows { get; } = new();
    public bool ShowAlbumLoudnessPreview => ShowMatchAlbumLoudness &&
        (MatchAlbumLoudness || AlbumLoudnessRows.Count > 0);
    public bool CanAnalyzeAlbumLoudness =>
        !IsExporting && !IsAnalyzingAlbumLoudness && Kind is ExportKind.Stems or ExportKind.Batch;
    public bool IsAnalyzingAlbumLoudness
    {
        get => _isAnalyzingAlbumLoudness;
        private set
        {
            if (!SetField(ref _isAnalyzingAlbumLoudness, value)) return;
            OnPropertyChanged(nameof(CanAnalyzeAlbumLoudness));
            AnalyzeAlbumLoudnessCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetField(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanExport));
                ExportCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanAnalyzeAlbumLoudness));
                AnalyzeAlbumLoudnessCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanExport => !IsExporting;

    public double Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public RelayCommand ExportCommand { get; }

    /// <summary>Invoked by the dialog after the user picks an output path or folder.</summary>
    public async Task ExportToPathAsync(string path)
    {
        if (IsExporting || string.IsNullOrWhiteSpace(path)) return;
        IsExporting = true;
        Progress = 0;
        Status = "Exporting…";
        LoudnessReportText = string.Empty;
        ShowStructuredLoudnessReport = false;
        try
        {
            if (ExportTimelineXml)
            {
                var xmlPath = path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? path
                    : path + TimelineXmlExporter.DefaultExtension;
                var exportBpm = _transport.Tempo.BeatsPerMinute;
                await Task.Run(() => TimelineXmlExporter.Export(_project.Current, xmlPath, exportBpm));
                Status = "Timeline XML export done.";
                Progress = 1;
                return;
            }

            if (ExportAafOmf)
            {
                var exportBpm = _transport.Tempo.BeatsPerMinute;
                await Task.Run(() => AafOmffExporter.ExportAaf(_project.Current, path, exportBpm));
                Status = "AAF/OMF handoff export done.";
                Progress = 1;
                return;
            }

            if (ExportAdmBwf)
            {
                var admOptions = new ExportOptions
                {
                    Kind = Kind,
                    RegionStartBeat = RegionStartBeat,
                    RegionEndBeat = RegionEndBeat,
                    Surround = Surround
                };
                var exportBpm = _transport.Tempo.BeatsPerMinute;
                await Task.Run(() => _export.ExportAdmBwf(_project.Current, _engine.Format, exportBpm, path,
                    admOptions, new Progress<double>(p => Progress = p)));
                Status = "ADM BWF export done.";
                Progress = 1;
                return;
            }

            var options = new ExportOptions
            {
                Kind = Kind,
                BitDepth = BitDepth,
                Surround = Surround,
                AudioFormat = AudioFormat,
                RegionStartBeat = RegionStartBeat,
                RegionEndBeat = RegionEndBeat,
                TrackIds = Kind == ExportKind.Stems
                    ? StemTracks.Where(t => t.IsSelected).Select(t => t.TrackId).ToList()
                    : null,
                IncludeMasterFx = IncludeMasterFx,
                BypassMasterFx = BypassMasterFx,
                ApplyDither = ApplyDither,
                DitherMode = DitherMode,
                MatchAlbumLoudness = MatchAlbumLoudness,
                ExportComparisonPair = ExportComparisonPair,
                AnalyzeLoudness = AnalyzeLoudness,
                NormalizeLoudness = NormalizeLoudness,
                DeliveryPlatform = DeliveryPlatform,
                TargetIntegratedLufs = TargetIntegratedLufs,
                TargetTruePeakDbTp = TargetTruePeakDbTp,
                TargetSampleRate = TargetSampleRate,
                MuxWithVideo = MuxWithVideo,
                VideoTrackId = SelectedVideoLayer?.Id ?? VideoLayers.FirstOrDefault()?.Id,
                ComposeVideo = ComposeVideo
            };

            var progress = new Progress<double>(p => Progress = p);
            var format = _engine.Format;
            var bpm = _transport.Tempo.BeatsPerMinute;

            if (ExportInBackground && ComposeVideo)
            {
                var start = Kind == ExportKind.Region ? RegionStartBeat : 0;
                var end = Kind == ExportKind.Region ? RegionEndBeat
                    : _project.Current.BarCount * Math.Max(1, _project.Current.TimeSignature.Numerator);
                _renderQueue.Enqueue(path, start, end);
                Status = "Queued for background export.";
                Progress = 0;
                OnPropertyChanged(nameof(RenderQueueStatus));
                return;
            }

            await Task.Run(() => _export.Export(_project.Current, format, bpm, path, options, progress,
                _waveformCache));
            Status = options.LoudnessReport is { } lr
                ? $"Done. {lr.Summary}"
                : "Done.";
            if (options.LoudnessReport is { } report)
            {
                LoudnessReportText = report.Summary;
                SetStructuredLoudnessReport(report);
            }
            Progress = 1;
        }
        catch (Exception ex)
        {
            Status = $"Failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private Task ExportAsync() => Task.CompletedTask;

    private void SetStructuredLoudnessReport(LoudnessReport report)
    {
        ReportIntegratedLufs = float.IsNegativeInfinity(report.IntegratedLufs)
            ? "−∞ LUFS"
            : $"{report.IntegratedLufs:0.0} LUFS";
        ReportLra = float.IsNaN(report.LoudnessRangeLu) ? "n/a" : $"{report.LoudnessRangeLu:0.0} LU";
        ReportTruePeak = $"{report.TruePeakDbTp:0.00} dBTP";
        ReportWithinTarget = report.WithinTarget;
        ShowStructuredLoudnessReport = true;
    }

    private async Task AnalyzeAlbumLoudnessAsync()
    {
        if (!CanAnalyzeAlbumLoudness) return;
        var selectedIds = Kind == ExportKind.Stems
            ? StemTracks.Where(t => t.IsSelected).Select(t => t.TrackId).ToArray()
            : null;
        if (selectedIds is { Length: 0 })
        {
            Status = "Select at least one stem to analyze.";
            return;
        }

        IsAnalyzingAlbumLoudness = true;
        AlbumLoudnessRows.Clear();
        OnPropertyChanged(nameof(ShowAlbumLoudnessPreview));
        Status = "Analyzing album loudness…";
        try
        {
            var format = _engine.Format;
            var bpm = _transport.Tempo.BeatsPerMinute;
            var progress = new Progress<double>(p => Progress = p);
            var rows = await Task.Run(() => _export.AnalyzeStemLoudness(
                _project.Current, format, bpm, selectedIds, IncludeMasterFx, Surround,
                TargetSampleRate, TargetIntegratedLufs, TargetTruePeakDbTp, progress));
            foreach (var row in rows)
            {
                var report = row.Report;
                AlbumLoudnessRows.Add(new AlbumLoudnessRow(
                    row.TrackName,
                    float.IsNegativeInfinity(report.IntegratedLufs) ? "−∞" : report.IntegratedLufs.ToString("0.0"),
                    float.IsNaN(report.LoudnessRangeLu) ? "n/a" : report.LoudnessRangeLu.ToString("0.0"),
                    report.TruePeakDbTp.ToString("0.00"),
                    row.OffsetDb.ToString("+0.0;-0.0;0.0")));
            }
            OnPropertyChanged(nameof(ShowAlbumLoudnessPreview));
            Status = $"Album analysis complete ({rows.Count} track{(rows.Count == 1 ? "" : "s")}).";
            Progress = 1;
        }
        catch (Exception ex)
        {
            Status = $"Album analysis failed: {ex.Message}";
        }
        finally
        {
            IsAnalyzingAlbumLoudness = false;
        }
    }

    private async Task SeparateSelectedClipAsync()
    {
        if (_selection.SelectedClip is not { IsAudio: true } clip) return;
        var lane = _timeline.Lanes.OfType<TrackLaneViewModel>()
            .FirstOrDefault(l => l.Model.Clips.Contains(clip));
        var clipVm = lane?.Clips.FirstOrDefault(c => ReferenceEquals(c.Model, clip));
        if (clipVm is null) return;
        IsSeparatingStems = true;
        StemSeparationProgress = 0;
        Status = "Separating stems…";
        try
        {
            await _timeline.SeparateStemsAsync(clipVm, StemQuality,
                new Progress<double>(p => StemSeparationProgress = p));
            Status = "Stem separation done.";
            StemSeparationProgress = 1;
        }
        catch (Exception ex)
        {
            Status = $"Stem separation failed: {ex.Message}";
        }
        finally
        {
            IsSeparatingStems = false;
        }
    }
}

public sealed class ExportStemTrackViewModel : ViewModelBase
{
    public ExportStemTrackViewModel(Track track, bool selected)
    {
        TrackId = track.Id;
        Name = track.Name;
        _isSelected = selected;
    }

    public Guid TrackId { get; }
    public string Name { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
