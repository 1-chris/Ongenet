using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.VideoTimeline;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Program monitor — preview, live sync, pop-out. Editing lives in bottom timeline + resources.</summary>
public sealed class VideoTrackViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IHistoryService _history;
    private readonly ITempoMapService _tempoMap;
    private readonly VideoTriggerEngine _triggers;
    private readonly IPlaybackModeService _playback;
    private readonly IVideoSelectionService _videoSelection;
    private readonly VideoTimelineViewModel _timeline;
    private readonly IVideoWaveformCacheService _waveformCache;
    private readonly IVideoAudioScopeService _audioScope;
    private readonly LiveVideoDecoder _liveDecoder = new();
    private Bitmap? _frame;
    private string _status = string.Empty;
    private double _lastFrameTime = double.NaN;
    private double _lastTickBeat;
    private bool _useLivePreview = true;
    private VideoPreviewWindowHost? _popOut;
    private int _previewTick;
    private int _waveformRevision;

    public VideoTrackViewModel(IProjectService project, ITransportService transport, IPlaybackClock clock,
        IHistoryService history, ITempoMapService tempoMap, VideoTriggerEngine triggers,
        IPlaybackModeService playback, IVideoSelectionService videoSelection,
        VideoTimelineViewModel timeline, IVideoWaveformCacheService waveformCache,
        IVideoAudioScopeService audioScope)
    {
        _project = project;
        _transport = transport;
        _history = history;
        _tempoMap = tempoMap;
        _triggers = triggers;
        _playback = playback;
        _videoSelection = videoSelection;
        _timeline = timeline;
        _waveformCache = waveformCache;
        _audioScope = audioScope;

        BrowseCommand = new RelayCommand(() => _ = BrowseAsync(), () => IsProjectVideoEnabled);
        EnableProjectVideoCommand = new RelayCommand(EnableProjectVideo);
        PopOutCommand = new RelayCommand(PopOut, () => IsProjectVideoEnabled && (Frame is not null || Layers.Count > 0));
        AddLayerCommand = new RelayCommand(
            () => _timeline.AddLayerCommand.Execute(null),
            () => _timeline.AddLayerCommand.CanExecute(null));

        _project.ProjectChanged += Rebuild;
        _timeline.LanesChanged += OnTimelineLanesChanged;
        _videoSelection.SelectionChanged += OnVideoSelectionChanged;
        _transport.StartBeatChanged += OnTransportScrub;
        _transport.StateChanged += _ => OnTransportScrub();
        _playback.ActiveClipsChanged += OnSessionClipsChanged;
        clock.Tick += OnTick;
        Rebuild();
    }

    public ObservableCollection<VideoLayer> Layers { get; } = new();

    public bool HasVideoLayers => Layers.Any(l => l.HasVideoItem);

    public bool IsFfmpegAvailable => FfmpegVideoFrameExtractor.IsAvailable;
    public bool IsLivePreviewAvailable => LiveVideoDecoder.IsAvailable;
    public bool IsProjectVideoEnabled => _project.Current.VideoEnabled;

    public int CanvasWidth => _project.Current.VideoCanvasWidth;
    public int CanvasHeight => _project.Current.VideoCanvasHeight;
    public string CanvasSizeLabel => $"{CanvasWidth} × {CanvasHeight}";

    public bool HasSyncVideo => SyncLayer is not null
        && SyncLayer.Items.FirstOrDefault(i => i.Kind == VideoElementKind.Video) is { SourcePath: { } path }
        && !string.IsNullOrWhiteSpace(path)
        && File.Exists(path);

    public bool ShowGettingStarted => IsProjectVideoEnabled && Layers.Count == 0;

    public ObservableCollection<VideoResolutionPreset> ResolutionPresets { get; } = new()
    {
        new("1920 × 1080", 1920, 1080),
        new("1280 × 720", 1280, 720),
        new("1080 × 1080", 1080, 1080),
        new("1080 × 1920", 1080, 1920),
        new("Custom", 0, 0, true)
    };

    private VideoResolutionPreset? _selectedResolutionPreset;

    public VideoResolutionPreset? SelectedResolutionPreset
    {
        get => _selectedResolutionPreset ?? MatchCurrentPreset();
        set
        {
            if (value is null || value.IsCustom) { SetField(ref _selectedResolutionPreset, value); return; }
            if (!SetField(ref _selectedResolutionPreset, value)) return;
            _history.Capture("Set video canvas resolution");
            _project.Current.VideoCanvasWidth = value.Width;
            _project.Current.VideoCanvasHeight = value.Height;
            NotifyCanvasChanged();
        }
    }

    public bool IsCustomResolution => SelectedResolutionPreset?.IsCustom == true;

    public int CustomCanvasWidth
    {
        get => CanvasWidth;
        set
        {
            var clamped = Math.Clamp(value, 320, 4096);
            if (CanvasWidth == clamped) return;
            _history.Capture("Set video canvas width");
            _project.Current.VideoCanvasWidth = clamped;
            _selectedResolutionPreset = ResolutionPresets.First(p => p.IsCustom);
            NotifyCanvasChanged();
        }
    }

    public int CustomCanvasHeight
    {
        get => CanvasHeight;
        set
        {
            var clamped = Math.Clamp(value, 320, 4096);
            if (CanvasHeight == clamped) return;
            _history.Capture("Set video canvas height");
            _project.Current.VideoCanvasHeight = clamped;
            _selectedResolutionPreset = ResolutionPresets.First(p => p.IsCustom);
            NotifyCanvasChanged();
        }
    }

    public bool UseLivePreview
    {
        get => _useLivePreview;
        set
        {
            if (!SetField(ref _useLivePreview, value)) return;
            _lastFrameTime = double.NaN;
            if (!value) _liveDecoder.Close();
            RefreshFrame(force: true);
        }
    }

    /// <summary>Layer used for transport-synced video decode.</summary>
    public VideoLayer? SyncLayer => _videoSelection.SelectedLayer is { HasVideoItem: true, Muted: false } selected
        ? selected
        : Layers.FirstOrDefault(l => l.HasVideoItem && !l.Muted);

    public VideoLayer? SelectedLayer
    {
        get => _videoSelection.SelectedLayer;
        set => _videoSelection.SelectedLayer = value;
    }

    public VideoLayerItem? SelectedLayerItem => _videoSelection.SelectedLayerItem;

    public void SelectItem(VideoLayer layer, VideoLayerItem item)
    {
        _videoSelection.SelectedTrigger = null;
        _videoSelection.SelectedVisibilityRegion = null;
        _videoSelection.SelectedLayer = layer;
        _videoSelection.SelectedLayerItem = item;
    }

    public bool IsItemSelected(VideoLayer layer, VideoLayerItem item) =>
        ReferenceEquals(_videoSelection.SelectedLayer, layer)
        && ReferenceEquals(_videoSelection.SelectedLayerItem, item);

    public bool IsMuted
    {
        get => SyncLayer?.Muted ?? false;
        set
        {
            if (SyncLayer is null || SyncLayer.Muted == value) return;
            _history.Capture("Toggle video mute");
            SyncLayer.Muted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SyncLayer));
            RefreshFrame(force: true);
        }
    }

    public Bitmap? Frame
    {
        get => _frame;
        private set
        {
            if (SetField(ref _frame, value))
                PopOutCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public double SyncTimeSeconds => ComputeSyncTimeSeconds();

    /// <summary>Increments during playback so the composition canvas repaints overlay visibility.</summary>
    public int PreviewTick => _previewTick;

    public double PlayheadBeats => _transport.PlayheadBeats;

    public int TotalProjectBeats => Math.Max(1, _project.Current.BarCount * Math.Max(1, _project.Current.TimeSignature.Numerator));

    public int WaveformRevision => _waveformRevision;

    public IVideoAudioScopeService AudioScope => _audioScope;

    public AudioWaveform? GetWaveformForLayer(VideoLayer layer)
    {
        if (!layer.IsWaveformLayer || layer.AudioSourceTrackId is not { } id) return null;
        return _waveformCache.TryGet(id);
    }

    public bool IsWaveformLayerSelected(VideoLayer layer) =>
        ReferenceEquals(_videoSelection.SelectedLayer, layer) && layer.IsWaveformLayer;

    public void SelectWaveformLayer(VideoLayer layer)
    {
        _videoSelection.SelectedTrigger = null;
        _videoSelection.SelectedVisibilityRegion = null;
        _videoSelection.SelectedLayer = layer;
        _videoSelection.SelectedLayerItem = null;
    }

    public void SetWaveformBounds(VideoLayer layer, double x, double y, double width, double height)
    {
        _history.Capture("Move waveform bounds");
        layer.WaveformX = Math.Clamp(x, 0, 1);
        layer.WaveformY = Math.Clamp(y, 0, 1);
        layer.WaveformWidth = Math.Clamp(width, 0.05, 1);
        layer.WaveformHeight = Math.Clamp(height, 0.03, 1);
        OnPropertyChanged(nameof(Layers));
    }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand EnableProjectVideoCommand { get; }
    public RelayCommand PopOutCommand { get; }
    public RelayCommand AddLayerCommand { get; }

    public Func<Task<string?>>? PickVideoPathAsync { get; set; }

    public double GetLayerOpacity(Guid layerId) => _triggers.Runtime.GetOpacity(layerId);

    public void MoveElement(Guid itemId, double x, double y)
    {
        if (FindItem(itemId) is not ({ } layer, { } item)) return;
        _history.Capture("Move video item");
        item.X = Math.Clamp(x, 0, 1);
        item.Y = Math.Clamp(y, 0, 1);
        OnPropertyChanged(nameof(Layers));
    }

    public void ResizeElement(Guid itemId, double width, double height)
    {
        if (FindItem(itemId) is not ({ } layer, { } item)) return;
        _history.Capture("Resize video item");
        item.Width = Math.Clamp(width, 0.02, 1);
        item.Height = Math.Clamp(height, 0.02, 1);
        OnPropertyChanged(nameof(Layers));
    }

    private (VideoLayer Layer, VideoLayerItem Item)? FindItem(Guid itemId)
    {
        foreach (var layer in Layers)
        {
            var item = layer.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not null) return (layer, item);
        }

        return null;
    }

    public void OnMidiNote(int note, bool on)
    {
        if (!IsProjectVideoEnabled) return;
        _triggers.OnMidiNote(_project.Current, note, on);
    }

    private void OnVideoSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedLayer));
        OnPropertyChanged(nameof(SyncLayer));
        OnPropertyChanged(nameof(IsMuted));
        BrowseCommand.RaiseCanExecuteChanged();
        _lastFrameTime = double.NaN;
        RefreshFrame(force: true);
    }

    private void EnableProjectVideo()
    {
        _history.Capture("Enable project video");
        _project.Current.VideoEnabled = true;
        _triggers.Reset(_project.Current);
        Rebuild();
    }

    private void Rebuild()
    {
        Layers.Clear();
        foreach (var layer in _project.Current.VideoLayers.OrderBy(l => l.ZOrder))
            Layers.Add(layer);

        if (_videoSelection.SelectedLayer is null)
            _videoSelection.SelectedLayer = Layers.FirstOrDefault();

        OnPropertyChanged(nameof(IsProjectVideoEnabled));
        OnPropertyChanged(nameof(IsFfmpegAvailable));
        OnPropertyChanged(nameof(IsLivePreviewAvailable));
        OnPropertyChanged(nameof(SelectedLayer));
        OnPropertyChanged(nameof(SyncLayer));
        OnPropertyChanged(nameof(ShowGettingStarted));
        OnPropertyChanged(nameof(HasSyncVideo));
        OnPropertyChanged(nameof(HasVideoLayers));
        NotifyCanvasChanged();
        BrowseCommand.RaiseCanExecuteChanged();
        PopOutCommand.RaiseCanExecuteChanged();
        AddLayerCommand.RaiseCanExecuteChanged();

        if (IsProjectVideoEnabled)
            _triggers.Reset(_project.Current);
        else
            Frame = null;

        EnsureWaveformCaches();
        SyncVisualiserRequests();
    }

    private void SyncVisualiserRequests()
    {
        foreach (var layer in Layers.Where(l => l.IsWaveformLayer && l.AudioSourceTrackId is not null))
        {
            if (GetLayerOpacity(layer.Id) > 0.01)
                _audioScope.Request(layer.AudioSourceTrackId!.Value);
        }
    }

    private void OnTimelineLanesChanged()
    {
        var projectLayers = _project.Current.VideoLayers.OrderBy(l => l.ZOrder).ToList();
        Layers.Clear();
        foreach (var layer in projectLayers)
            Layers.Add(layer);

        _triggers.Seek(_project.Current, _transport.PlayheadBeats);
        OnPropertyChanged(nameof(Layers));
        OnPropertyChanged(nameof(SelectedLayer));
        OnPropertyChanged(nameof(SyncLayer));
        OnPropertyChanged(nameof(HasVideoLayers));
        OnPropertyChanged(nameof(HasSyncVideo));
        OnPropertyChanged(nameof(ShowGettingStarted));
        PopOutCommand.RaiseCanExecuteChanged();
        _lastFrameTime = double.NaN;
        EnsureWaveformCaches();
        SyncVisualiserRequests();
        RefreshFrame(force: true);
    }

    private void EnsureWaveformCaches()
    {
        foreach (var layer in Layers.Where(l => l.IsWaveformLayer && l.AudioSourceTrackId is not null))
            _ = BuildWaveformAsync(layer.AudioSourceTrackId!.Value);
    }

    private async Task BuildWaveformAsync(Guid trackId)
    {
        if (_waveformCache.TryGet(trackId) is not null) return;
        try
        {
            await Task.Run(() => _waveformCache.GetOrBuild(
                _project.Current, trackId, _transport.Tempo.BeatsPerMinute));
            _waveformRevision++;
            OnPropertyChanged(nameof(WaveformRevision));
            OnPropertyChanged(nameof(PreviewTick));
            _lastFrameTime = double.NaN;
            RefreshFrame(force: true);
        }
        catch
        {
            // Waveform build can fail when the source track has no renderable content yet.
        }
    }

    private async Task BrowseAsync()
    {
        if (!IsProjectVideoEnabled || PickVideoPathAsync is null) return;

        var path = await PickVideoPathAsync();
        if (string.IsNullOrWhiteSpace(path)) return;

        _history.Capture("Add video layer");
        var layer = VideoTimelineViewModel.CreateLayerFromPath(path);
        layer.ZOrder = _project.Current.VideoLayers.Count;
        _project.Current.VideoLayers.Add(layer);
        Layers.Add(layer);
        _videoSelection.SelectedLayer = layer;
        _videoSelection.SelectedLayerItem = layer.Items.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedLayer));
        OnPropertyChanged(nameof(SyncLayer));
        OnPropertyChanged(nameof(HasVideoLayers));
        _lastFrameTime = double.NaN;
        RefreshFrame(force: true);
    }

    private void PopOut()
    {
        _popOut ??= new VideoPreviewWindowHost(this);
        _popOut.ShowOrActivate();
    }

    private void OnSessionClipsChanged()
    {
        if (!IsProjectVideoEnabled) return;
        foreach (var id in _playback.ActiveSessionClipIds)
            _triggers.OnSessionClipEvent(_project.Current, id, VideoTriggerMoment.ClipStart);
    }

    private void OnTransportScrub()
    {
        _triggers.Seek(_project.Current, _transport.PlayheadBeats);
        SyncVisualiserRequests();
        _lastFrameTime = double.NaN;
        RefreshFrame(force: true);
    }

    private DateTime _lastTickUtc = DateTime.UtcNow;

    private void OnTick()
    {
        if (!IsProjectVideoEnabled) return;
        var now = DateTime.UtcNow;
        var delta = (now - _lastTickUtc).TotalSeconds;
        _lastTickUtc = now;
        var beat = _transport.PlayheadBeats;
        _triggers.Tick(_project.Current, _lastTickBeat, beat, delta);
        _lastTickBeat = beat;
        if (_transport.State == TransportState.Playing)
        {
            _previewTick++;
            OnPropertyChanged(nameof(PreviewTick));
            OnPropertyChanged(nameof(PlayheadBeats));
        }

        SyncVisualiserRequests();
        RefreshFrame(force: false);
        _popOut?.UpdateFrame();
    }

    private double ComputeSyncTimeSeconds()
    {
        if (SyncLayer is null) return 0;
        var seconds = _tempoMap.BeatsToSeconds(_project.Current, _transport.PlayheadBeats);
        var raw = SyncLayer.OffsetSeconds + seconds;
        var inPt = SyncLayer.InPointSeconds;
        var outPt = SyncLayer.OutPointSeconds;
        if (outPt > inPt && raw > outPt) raw = outPt;
        return Math.Max(inPt, raw);
    }

    private void RefreshFrame(bool force)
    {
        if (!IsProjectVideoEnabled)
        {
            Frame = null;
            Status = string.Empty;
            return;
        }

        var syncLayer = SyncLayer;
        var videoItem = syncLayer?.Items.FirstOrDefault(i => i.Kind == VideoElementKind.Video);
        var videoPath = videoItem?.SourcePath;

        if (syncLayer is null || syncLayer.Muted || string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            Frame = null;
            Status = L("VideoTrack_Overlay_only_mode");
            OnPropertyChanged(nameof(SyncTimeSeconds));
            OnPropertyChanged(nameof(HasSyncVideo));
            return;
        }

        var t = SyncTimeSeconds;

        if (!IsFfmpegAvailable)
        {
            Frame = null;
            Status = string.Format(L("VideoTrack_ffmpeg_not_installed"), Path.GetFileName(videoPath), t);
            OnPropertyChanged(nameof(SyncTimeSeconds));
            return;
        }

        if (!force && Math.Abs(t - _lastFrameTime) < 1.0 / Math.Max(syncLayer.Fps, 1))
            return;

        _lastFrameTime = t;

        if (UseLivePreview && IsLivePreviewAvailable && _transport.State == TransportState.Playing)
        {
            _liveDecoder.Seek(videoPath, Math.Max(0, t));
            var rgb = _liveDecoder.ReadFrame();
            if (rgb is not null && _liveDecoder.Width > 0 && _liveDecoder.Height > 0)
            {
                Frame = BitmapFromRgb(rgb, _liveDecoder.Width, _liveDecoder.Height);
                Status = string.Format(L("VideoTrack_Live_sync"), t);
                OnPropertyChanged(nameof(SyncTimeSeconds));
                return;
            }
        }

        var png = FfmpegVideoFrameExtractor.ExtractFramePng(videoPath, Math.Max(0, t));
        if (png is null)
        {
            Status = L("VideoTrack_Frame_extract_failed");
            return;
        }

        using var ms = new MemoryStream(png);
        Frame = new Bitmap(ms);
        Status = _transport.State == TransportState.Playing
            ? string.Format(L("VideoTrack_Synced_at"), t)
            : string.Format(L("VideoTrack_Scrub_preview_at"), t);
        OnPropertyChanged(nameof(SyncTimeSeconds));
        OnPropertyChanged(nameof(HasSyncVideo));
    }

    private VideoResolutionPreset? MatchCurrentPreset()
    {
        var w = CanvasWidth;
        var h = CanvasHeight;
        return ResolutionPresets.FirstOrDefault(p => !p.IsCustom && p.Width == w && p.Height == h)
               ?? ResolutionPresets.First(p => p.IsCustom);
    }

    private void NotifyCanvasChanged()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(CanvasSizeLabel));
        OnPropertyChanged(nameof(CustomCanvasWidth));
        OnPropertyChanged(nameof(CustomCanvasHeight));
        OnPropertyChanged(nameof(SelectedResolutionPreset));
        OnPropertyChanged(nameof(IsCustomResolution));
    }

    private static Bitmap BitmapFromRgb(byte[] rgb, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            bgra[i * 4] = rgb[i * 3 + 2];
            bgra[i * 4 + 1] = rgb[i * 3 + 1];
            bgra[i * 4 + 2] = rgb[i * 3];
            bgra[i * 4 + 3] = 255;
        }

        var wb = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888);
        using var fb = wb.Lock();
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, fb.Address, Math.Min(bgra.Length, fb.RowBytes * height));
        return wb;
    }
}

public sealed record VideoResolutionPreset(string Label, int Width, int Height, bool IsCustom = false);

public sealed record ClipSyncOption(string TrackName, string ClipName, Guid ClipId, double StartBeat, double EndBeat)
{
    public string Label => $"{TrackName} / {ClipName}";
}

public sealed class VideoTriggerRow(VideoTrigger trigger, string label)
{
    public VideoTrigger Trigger { get; } = trigger;
    public string Label { get; } = label;
}
