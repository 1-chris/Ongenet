using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.Timeline;

namespace Ongenet.App.ViewModels;

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
    private ExportKind _kind = ExportKind.Master;
    private SurroundFormat _surround = SurroundFormat.Stereo;
    private int _bitDepth = 16;
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
        IVideoRenderQueueService renderQueue)
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

        ExportCommand = new RelayCommand(() => _ = ExportAsync(), () => CanExport);
        SeparateSelectedClipCommand = new RelayCommand(
            () => _ = SeparateSelectedClipAsync(),
            () => CanSeparateSelectedClip);
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

    public ExportKind[] ExportKinds { get; } = Enum.GetValues<ExportKind>();
    public SurroundFormat[] SurroundFormats { get; } = Enum.GetValues<SurroundFormat>();
    public ExportAudioFormat[] AudioFormats { get; } = Enum.GetValues<ExportAudioFormat>();
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
            OnPropertyChanged(nameof(CanExport));
        }
    }

    public int BitDepth
    {
        get => _bitDepth;
        set => SetField(ref _bitDepth, value);
    }

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

    public ObservableCollection<ExportStemTrackViewModel> StemTracks { get; } = new();

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetField(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanExport));
                ExportCommand.RaiseCanExecuteChanged();
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
            Status = "Done.";
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
