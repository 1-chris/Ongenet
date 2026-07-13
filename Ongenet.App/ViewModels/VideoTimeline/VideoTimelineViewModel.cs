using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.Panels;
using Ongenet.App.ViewModels.Timeline;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.VideoTimeline;

/// <summary>Bottom-panel NLE timeline: layers, trigger markers, inspector.</summary>
public sealed class VideoTimelineViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly IHistoryService _history;
    private readonly ITempoMapService _tempoMap;
    private readonly IVideoSelectionService _selection;
    private readonly ITimelineLayoutService _layout;
    private readonly ITransportSeekService _seek;
    private readonly ITransportService _transport;
    private readonly IVideoWaveformCacheService _waveformCache;
    private ClipSyncOption? _selectedSyncClip;
    private bool _syncingSelection;
    private bool _visibilityRegionEditing;

    public VideoTimelineViewModel(IProjectService project, IHistoryService history,
        ITempoMapService tempoMap, IVideoSelectionService selection,
        ITimelineLayoutService layout, ITransportSeekService seek, ITransportService transport,
        IVideoWaveformCacheService waveformCache)
    {
        _project = project;
        _history = history;
        _tempoMap = tempoMap;
        _selection = selection;
        _layout = layout;
        _seek = seek;
        _transport = transport;
        _waveformCache = waveformCache;

        AddLayerCommand = new RelayCommand(AddLayer, () => IsProjectVideoEnabled);
        RemoveLayerCommand = new RelayCommand(RemoveLayer, () => SelectedLayer is not null);
        AddLayerItemCommand = new RelayCommand(AddLayerItem, CanAddLayerItem);
        RemoveLayerItemCommand = new RelayCommand(RemoveLayerItem, () => SelectedLayerItem is not null);
        AddTriggerCommand = new RelayCommand(AddTrigger, () => IsProjectVideoEnabled && _project.Current.VideoLayers.Count > 0);
        RemoveTriggerCommand = new RelayCommand(RemoveTrigger, () => SelectedTrigger is not null);
        BrowseLayerItemCommand = new RelayCommand(() => _ = BrowseLayerMediaAsync(), CanBrowseLayerMedia);
        SyncToClipCommand = new RelayCommand(SyncToSelectedClip, () => SelectedLayer is not null && SelectedSyncClip is not null);
        SeekToClipCommand = new RelayCommand<ClipSyncOption>(c => { if (c is not null) _seek.SeekToBeat(c.StartBeat); });
        MoveLayerUpCommand = new RelayCommand(() => MoveLayer(-1), CanMoveLayerUp);
        MoveLayerDownCommand = new RelayCommand(() => MoveLayer(1), CanMoveLayerDown);
        AddVisibilityRegionCommand = new RelayCommand(AddVisibilityRegion, () => SelectedLayer is not null);
        RemoveVisibilityRegionCommand = new RelayCommand(RemoveVisibilityRegion, () => SelectedVisibilityRegion is not null);

        _project.ProjectChanged += Rebuild;
        _selection.SelectionChanged += OnExternalSelectionChanged;
        _layout.Metrics.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineMetrics.PixelsPerBeat) or nameof(TimelineMetrics.TotalWidth))
                RebuildLanes();
        };
        _transport.StartBeatChanged += () => OnPropertyChanged(nameof(PlayheadBeats));
        _transport.StateChanged += _ => OnPropertyChanged(nameof(IsPlaying));
        Rebuild();
    }

    public TimelineMetrics Metrics => _layout.Metrics;
    public ObservableCollection<BarTickViewModel> Bars { get; } = new();
    public ObservableCollection<VideoOverlayLaneViewModel> OverlayLanes { get; } = new();
    public ObservableCollection<ClipSyncOption> ArrangementClips { get; } = new();
    public ObservableCollection<VideoLayerItem> SelectedLayerItems => SelectedLayer is null
        ? new ObservableCollection<VideoLayerItem>()
        : new ObservableCollection<VideoLayerItem>(SelectedLayer.Items);

    public ObservableCollection<EnumOption<VideoElementKind>> ElementKindOptions { get; } = BuildElementKindOptions();
    public ObservableCollection<EnumOption<VideoLayerContentKind>> LayerContentKindOptions { get; } = BuildLayerContentKindOptions();
    public ObservableCollection<EnumOption<VideoWaveformStyle>> WaveformStyleOptions { get; } = BuildWaveformStyleOptions();
    public ObservableCollection<EnumOption<VideoVisualiserColorMode>> VisualiserColorModeOptions { get; } = BuildVisualiserColorModeOptions();
    public ObservableCollection<VideoAudioSourceOption> AudioSourceTrackOptions { get; } = new();
    public ObservableCollection<WaveformColorPreset> WaveformColorPresets { get; } = BuildWaveformColorPresets();
    public ObservableCollection<EnumOption<VideoTriggerMoment>> TriggerMomentOptions { get; } = BuildTriggerMomentOptions();
    public ObservableCollection<EnumOption<VideoTriggerAction>> TriggerActionOptions { get; } = BuildTriggerActionOptions();
    public ObservableCollection<VideoLayer> TargetLayerOptions => new(_project.Current.VideoLayers);

    public bool IsProjectVideoEnabled => _project.Current.VideoEnabled;
    public bool HasInspectorSelection => SelectedLayer is not null
        || SelectedTrigger is not null || SelectedVisibilityRegion is not null;
    public bool ShowInspectorPlaceholder => IsProjectVideoEnabled && !HasInspectorSelection;
    public double PlayheadBeats => _transport.PlayheadBeats;
    public bool IsPlaying => _transport.State == TransportState.Playing;
    public double BeatsPerSecond => _transport.Tempo.BeatsPerMinute / 60.0;
    public int BeatsPerBar => Math.Max(1, _project.Current.TimeSignature.Numerator);

    public VideoLayer? SelectedLayer
    {
        get => _selection.SelectedLayer;
        set
        {
            if (_selection.SelectedLayer == value) return;
            _syncingSelection = true;
            _selection.SelectedLayer = value;
            if (value is not null)
            {
                _selection.SelectedTrigger = null;
                _selection.SelectedVisibilityRegion = null;
                if (_selection.SelectedLayerItem is null || !value.Items.Contains(_selection.SelectedLayerItem))
                    _selection.SelectedLayerItem = value.Items.FirstOrDefault();
            }

            _syncingSelection = false;
            NotifyInspector();
            RebuildLanes();
        }
    }

    public VideoLayerItem? SelectedLayerItem
    {
        get => _selection.SelectedLayerItem;
        set
        {
            if (_selection.SelectedLayerItem == value) return;
            _syncingSelection = true;
            _selection.SelectedLayerItem = value;
            if (value is not null)
            {
                var layer = _project.Current.VideoLayers.FirstOrDefault(l => l.Items.Contains(value));
                if (layer is not null)
                    _selection.SelectedLayer = layer;
                _selection.SelectedTrigger = null;
                _selection.SelectedVisibilityRegion = null;
            }

            _syncingSelection = false;
            NotifyInspector();
        }
    }

    public VideoTrigger? SelectedTrigger
    {
        get => _selection.SelectedTrigger;
        set
        {
            if (_selection.SelectedTrigger == value) return;
            _syncingSelection = true;
            _selection.SelectedTrigger = value;
            _syncingSelection = false;
            NotifyInspector();
            RebuildLanes();
        }
    }

    public VideoVisibilityRegion? SelectedVisibilityRegion
    {
        get => _selection.SelectedVisibilityRegion;
        set
        {
            if (_selection.SelectedVisibilityRegion == value) return;
            _syncingSelection = true;
            _selection.SelectedVisibilityRegion = value;
            if (value is not null)
            {
                _selection.SelectedLayer = _project.Current.VideoLayers.FirstOrDefault(l => l.Id == value.LayerId);
                _selection.SelectedTrigger = null;
            }

            _syncingSelection = false;
            NotifyInspector();
            RebuildLanes();
        }
    }

    public string SelectedLayerName
    {
        get => SelectedLayer?.Name ?? "";
        set
        {
            if (SelectedLayer is null || SelectedLayer.Name == value) return;
            _history.Capture("Rename video layer");
            SelectedLayer.Name = value;
            RebuildLanes();
            OnPropertyChanged();
        }
    }

    public double RegionStartBeat
    {
        get => SelectedVisibilityRegion?.StartBeat ?? 0;
        set
        {
            if (SelectedVisibilityRegion is null || Math.Abs(SelectedVisibilityRegion.StartBeat - value) < 1e-6) return;
            _history.Capture("Edit visibility region start");
            SelectedVisibilityRegion.StartBeat = Math.Min(value, SelectedVisibilityRegion.EndBeat - 0.25);
            RebuildLanes();
            OnPropertyChanged();
            OnPropertyChanged(nameof(RegionEndBeat));
        }
    }

    public double RegionEndBeat
    {
        get => SelectedVisibilityRegion?.EndBeat ?? 0;
        set
        {
            if (SelectedVisibilityRegion is null || Math.Abs(SelectedVisibilityRegion.EndBeat - value) < 1e-6) return;
            _history.Capture("Edit visibility region end");
            SelectedVisibilityRegion.EndBeat = Math.Max(value, SelectedVisibilityRegion.StartBeat + 0.25);
            RebuildLanes();
            OnPropertyChanged();
            OnPropertyChanged(nameof(RegionStartBeat));
        }
    }

    public ClipSyncOption? SelectedSyncClip { get => _selectedSyncClip; set => SetField(ref _selectedSyncClip, value); }

    public double LayerOffsetSeconds
    {
        get => SelectedLayer?.OffsetSeconds ?? 0;
        set
        {
            if (SelectedLayer is null || Math.Abs(SelectedLayer.OffsetSeconds - value) < 1e-6) return;
            _history.Capture("Edit video offset");
            SelectedLayer.OffsetSeconds = value;
            RebuildLanes();
            OnPropertyChanged();
        }
    }

    public double LayerInPointSeconds
    {
        get => SelectedLayer?.InPointSeconds ?? 0;
        set
        {
            if (SelectedLayer is null || Math.Abs(SelectedLayer.InPointSeconds - value) < 1e-6) return;
            _history.Capture("Edit video in point");
            SelectedLayer.InPointSeconds = Math.Max(0, value);
            RebuildLanes();
            OnPropertyChanged();
        }
    }

    public double LayerOutPointSeconds
    {
        get => SelectedLayer?.OutPointSeconds ?? 0;
        set
        {
            if (SelectedLayer is null || Math.Abs(SelectedLayer.OutPointSeconds - value) < 1e-6) return;
            _history.Capture("Edit video out point");
            SelectedLayer.OutPointSeconds = Math.Max(0, value);
            RebuildLanes();
            OnPropertyChanged();
        }
    }

    public double LayerFps
    {
        get => SelectedLayer?.Fps ?? 24;
        set
        {
            if (SelectedLayer is null || Math.Abs(SelectedLayer.Fps - value) < 1e-6) return;
            _history.Capture("Edit video FPS");
            SelectedLayer.Fps = Math.Max(1, value);
            OnPropertyChanged();
        }
    }

    public EnumOption<VideoElementKind>? SelectedLayerItemKind
    {
        get => SelectedLayerItem is null ? null : ElementKindOptions.FirstOrDefault(o => o.Value == SelectedLayerItem.Kind);
        set
        {
            if (SelectedLayerItem is null || value is null || SelectedLayerItem.Kind == value.Value) return;
            _history.Capture("Change overlay item type");
            SelectedLayerItem.Kind = value.Value;
            OnPropertyChanged();
        }
    }

    public EnumOption<VideoTriggerMoment>? SelectedTriggerMoment
    {
        get => SelectedTrigger is null ? null : TriggerMomentOptions.FirstOrDefault(o => o.Value == SelectedTrigger.Moment);
        set
        {
            if (SelectedTrigger is null || value is null || SelectedTrigger.Moment == value.Value) return;
            _history.Capture("Change trigger moment");
            SelectedTrigger.Moment = value.Value;
            RebuildLanes();
            OnPropertyChanged();
        }
    }

    public EnumOption<VideoTriggerAction>? SelectedTriggerAction
    {
        get => SelectedTrigger is null ? null : TriggerActionOptions.FirstOrDefault(o => o.Value == SelectedTrigger.Action);
        set
        {
            if (SelectedTrigger is null || value is null || SelectedTrigger.Action == value.Value) return;
            _history.Capture("Change trigger action");
            SelectedTrigger.Action = value.Value;
            RebuildLanes();
            OnPropertyChanged();
        }
    }

    public ClipSyncOption? SelectedTriggerClip
    {
        get => SelectedTrigger?.ClipId is { } id ? ArrangementClips.FirstOrDefault(c => c.ClipId == id) : null;
        set
        {
            if (SelectedTrigger is null || value is null || SelectedTrigger.ClipId == value.ClipId) return;
            _history.Capture("Change trigger clip");
            SelectedTrigger.ClipId = value.ClipId;
            RebuildLanes();
            OnPropertyChanged();
        }
    }

    public VideoLayer? SelectedTriggerTarget
    {
        get => SelectedTrigger?.TargetLayerId is { } id
            ? _project.Current.VideoLayers.FirstOrDefault(l => l.Id == id)
            : null;
        set
        {
            if (SelectedTrigger is null || value is null || SelectedTrigger.TargetLayerId == value.Id) return;
            _history.Capture("Change trigger target");
            SelectedTrigger.TargetLayerId = value.Id;
            RebuildLanes();
            OnPropertyChanged();
        }
    }

    public bool ShowLayerSyncInspector => SelectedLayer?.HasVideoItem == true;
    public bool ShowLayerMediaInspector => SelectedLayer is not null && LayerContentKind == VideoLayerContentKind.Media;
    public bool ShowLayerWaveformInspector => SelectedLayer is not null && LayerContentKind == VideoLayerContentKind.Waveform;

    public bool ShowVisualiserGradientColor => SelectedLayer?.VisualiserColorMode == VideoVisualiserColorMode.Gradient;

    public bool ShowVisualiserSpectrumSettings => SelectedLayer?.WaveformStyle == VideoWaveformStyle.Spectrum;

    public bool ShowVisualiserLineThickness => SelectedLayer?.WaveformStyle is VideoWaveformStyle.Spectrum
        or VideoWaveformStyle.Mirrored;

    public EnumOption<VideoLayerContentKind>? SelectedLayerContentKind
    {
        get => LayerContentKindOptions.FirstOrDefault(o => o.Value == LayerContentKind);
        set
        {
            if (SelectedLayer is null || value is null || LayerContentKind == value.Value) return;
            ApplyLayerContentKind(value.Value);
        }
    }

    public VideoLayerContentKind LayerContentKind => SelectedLayer?.ContentKind ?? VideoLayerContentKind.Empty;

    public VideoAudioSourceOption? SelectedAudioSourceTrack
    {
        get
        {
            var id = SelectedLayer?.AudioSourceTrackId;
            return AudioSourceTrackOptions.FirstOrDefault(o => o.Id == id)
                   ?? AudioSourceTrackOptions.FirstOrDefault();
        }
        set
        {
            if (SelectedLayer is null || value?.Id == SelectedLayer.AudioSourceTrackId) return;
            _history.Capture("Set waveform source track");
            SelectedLayer.AudioSourceTrackId = value?.Id;
            _waveformCache.Invalidate();
            RebuildLanes();
            OnPropertyChanged(nameof(SelectedAudioSourceTrack));
            OnPropertyChanged(nameof(LayerContentKind));
            OnPropertyChanged(nameof(ShowLayerWaveformInspector));
        }
    }

    public EnumOption<VideoWaveformStyle>? SelectedWaveformStyle
    {
        get => SelectedLayer is null ? null : WaveformStyleOptions.FirstOrDefault(o => o.Value == SelectedLayer.WaveformStyle);
        set
        {
            if (SelectedLayer is null || value is null || SelectedLayer.WaveformStyle == value.Value) return;
            _history.Capture("Set waveform style");
            SelectedLayer.WaveformStyle = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowVisualiserSpectrumSettings));
            OnPropertyChanged(nameof(ShowVisualiserLineThickness));
        }
    }

    public EnumOption<VideoVisualiserColorMode>? SelectedVisualiserColorMode
    {
        get => SelectedLayer is null
            ? null
            : VisualiserColorModeOptions.FirstOrDefault(o => o.Value == SelectedLayer.VisualiserColorMode);
        set
        {
            if (SelectedLayer is null || value is null || SelectedLayer.VisualiserColorMode == value.Value) return;
            _history.Capture("Set visualiser color mode");
            SelectedLayer.VisualiserColorMode = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowVisualiserGradientColor));
        }
    }

    public WaveformColorPreset? SelectedVisualiserSecondaryColor
    {
        get
        {
            if (SelectedLayer is null) return null;
            return WaveformColorPresets.FirstOrDefault(p => p.Argb == SelectedLayer.VisualiserColorSecondaryArgb)
                   ?? WaveformColorPresets.FirstOrDefault();
        }
        set
        {
            if (SelectedLayer is null || value is null || SelectedLayer.VisualiserColorSecondaryArgb == value.Argb) return;
            _history.Capture("Set visualiser gradient color");
            SelectedLayer.VisualiserColorSecondaryArgb = value.Argb;
            OnPropertyChanged();
        }
    }

    public double SpectrumMinHz
    {
        get => SelectedLayer?.SpectrumMinHz ?? 20;
        set => SetVisualiserDouble(v => SelectedLayer!.SpectrumMinHz = Math.Clamp(v, 20, 20000), value,
            nameof(SpectrumMinHz), min: 20, max: 20000);
    }

    public double SpectrumMaxHz
    {
        get => SelectedLayer?.SpectrumMaxHz ?? 16000;
        set => SetVisualiserDouble(v => SelectedLayer!.SpectrumMaxHz = Math.Clamp(v, 40, 22000), value,
            nameof(SpectrumMaxHz), min: 40, max: 22000);
    }

    public double SpectrumLineThickness
    {
        get => SelectedLayer?.SpectrumLineThickness ?? 2;
        set => SetVisualiserDouble(v => SelectedLayer!.SpectrumLineThickness = Math.Clamp(v, 0.5, 12), value,
            nameof(SpectrumLineThickness), min: 0.5, max: 12);
    }

    public WaveformColorPreset? SelectedWaveformColor
    {
        get
        {
            if (SelectedLayer is null) return null;
            return WaveformColorPresets.FirstOrDefault(p => p.Argb == SelectedLayer.WaveformColorArgb)
                   ?? WaveformColorPresets.Last();
        }
        set
        {
            if (SelectedLayer is null || value is null || SelectedLayer.WaveformColorArgb == value.Argb) return;
            _history.Capture("Set waveform color");
            SelectedLayer.WaveformColorArgb = value.Argb;
            OnPropertyChanged();
        }
    }

    public bool WaveformFollowPlayhead
    {
        get => SelectedLayer?.WaveformFollowPlayhead ?? true;
        set
        {
            if (SelectedLayer is null || SelectedLayer.WaveformFollowPlayhead == value) return;
            _history.Capture("Toggle waveform follow playhead");
            SelectedLayer.WaveformFollowPlayhead = value;
            OnPropertyChanged();
        }
    }

    public double WaveformX
    {
        get => SelectedLayer?.WaveformX ?? 0.1;
        set => SetWaveformBound(v => SelectedLayer!.WaveformX = v, value, nameof(WaveformX));
    }

    public double WaveformY
    {
        get => SelectedLayer?.WaveformY ?? 0.7;
        set => SetWaveformBound(v => SelectedLayer!.WaveformY = v, value, nameof(WaveformY));
    }

    public double WaveformWidth
    {
        get => SelectedLayer?.WaveformWidth ?? 0.8;
        set => SetWaveformBound(v => SelectedLayer!.WaveformWidth = Math.Clamp(v, 0.05, 1), value, nameof(WaveformWidth));
    }

    public double WaveformHeight
    {
        get => SelectedLayer?.WaveformHeight ?? 0.12;
        set => SetWaveformBound(v => SelectedLayer!.WaveformHeight = Math.Clamp(v, 0.03, 1), value, nameof(WaveformHeight));
    }

    public RelayCommand AddLayerCommand { get; }
    public RelayCommand RemoveLayerCommand { get; }
    public RelayCommand AddLayerItemCommand { get; }
    public RelayCommand RemoveLayerItemCommand { get; }
    public RelayCommand AddTriggerCommand { get; }
    public RelayCommand RemoveTriggerCommand { get; }
    public RelayCommand BrowseLayerItemCommand { get; }
    public RelayCommand SyncToClipCommand { get; }
    public RelayCommand<ClipSyncOption> SeekToClipCommand { get; }
    public RelayCommand MoveLayerUpCommand { get; }
    public RelayCommand MoveLayerDownCommand { get; }
    public RelayCommand AddVisibilityRegionCommand { get; }
    public RelayCommand RemoveVisibilityRegionCommand { get; }

    public Func<Task<string?>>? PickImagePathAsync { get; set; }

    public event Action? LanesChanged;

    public void SeekToBeat(double beat, bool snap = false) => _seek.SeekToBeat(beat, snap);

    public void SelectOverlay(VideoLayer layer)
    {
        SelectedLayer = layer;
        SelectedTrigger = null;
        SelectedVisibilityRegion = null;
    }

    public void SelectMarker(VideoTrigger trigger)
    {
        SelectedTrigger = trigger;
        SelectedLayer = _project.Current.VideoLayers.FirstOrDefault(l => l.Id == trigger.TargetLayerId);
        SelectedVisibilityRegion = null;
    }

    public void SelectVisibilityRegion(VideoVisibilityRegion region) => SelectedVisibilityRegion = region;

    public void AdjustLayerInPoint(double deltaSeconds)
    {
        if (SelectedLayer is null || deltaSeconds == 0) return;
        _history.Capture("Edit video in point");
        SelectedLayer.InPointSeconds = Math.Max(0, SelectedLayer.InPointSeconds + deltaSeconds);
        RebuildLanes();
    }

    public void AdjustLayerOutPoint(double deltaSeconds)
    {
        if (SelectedLayer is null || deltaSeconds == 0) return;
        _history.Capture("Edit video out point");
        SelectedLayer.OutPointSeconds = Math.Max(0, SelectedLayer.OutPointSeconds + deltaSeconds);
        RebuildLanes();
    }

    public void ReassignTriggerClip(VideoTrigger trigger, Guid clipId)
    {
        _history.Capture("Move trigger to clip");
        trigger.ClipId = clipId;
        if (_selection.SelectedTrigger?.Id == trigger.Id)
            OnPropertyChanged(nameof(SelectedTriggerClip));
        RebuildLanes();
    }

    public void MoveVisibilityRegion(VideoVisibilityRegion region, double deltaBeats)
    {
        if (deltaBeats == 0) return;
        var span = region.EndBeat - region.StartBeat;
        SetVisibilityRegionSpan(region, region.StartBeat + deltaBeats, region.StartBeat + deltaBeats + span);
    }

    public void TrimVisibilityRegionStart(VideoVisibilityRegion region, double deltaBeats)
    {
        if (deltaBeats == 0) return;
        SetVisibilityRegionSpan(region, region.StartBeat + deltaBeats, region.EndBeat);
    }

    public void TrimVisibilityRegionEnd(VideoVisibilityRegion region, double deltaBeats)
    {
        if (deltaBeats == 0) return;
        SetVisibilityRegionSpan(region, region.StartBeat, region.EndBeat + deltaBeats);
    }

    public void BeginVisibilityRegionEdit()
    {
        if (_visibilityRegionEditing) return;
        _history.Capture("Edit visibility region");
        _visibilityRegionEditing = true;
    }

    public void EndVisibilityRegionEdit()
    {
        if (!_visibilityRegionEditing) return;
        _visibilityRegionEditing = false;
        OnPropertyChanged(nameof(RegionStartBeat));
        OnPropertyChanged(nameof(RegionEndBeat));
    }

    public void SetVisibilityRegionSpan(VideoVisibilityRegion region, double startBeat, double endBeat)
    {
        region.StartBeat = Math.Max(0, startBeat);
        region.EndBeat = Math.Max(region.StartBeat + 0.25, endBeat);
        NotifyVisibilityRegionGeometryChanged(region);
    }

    public void NotifyVisibilityRegionGeometryChanged(VideoVisibilityRegion region)
    {
        foreach (var lane in OverlayLanes)
        {
            foreach (var block in lane.VisibilityBlocks)
            {
                if (block.Region.Id != region.Id) continue;
                block.RefreshFromRegion();
                return;
            }
        }
    }

    public void CreateVisibilityRegion(VideoLayer layer, double startBeat, double endBeat)
    {
        _history.Capture("Add visibility region");
        var region = new VideoVisibilityRegion
        {
            LayerId = layer.Id,
            StartBeat = Math.Min(startBeat, endBeat),
            EndBeat = Math.Max(startBeat, endBeat)
        };
        if (region.EndBeat - region.StartBeat < 0.25)
            region.EndBeat = region.StartBeat + 4;
        _project.Current.VideoVisibilityRegions.Add(region);
        SelectedVisibilityRegion = region;
        RebuildLanes();
    }

    public void DuplicateVisibilityRegion(VideoVisibilityRegion region)
    {
        _history.Capture("Duplicate visibility region");
        var span = Math.Max(0.25, region.EndBeat - region.StartBeat);
        var copy = new VideoVisibilityRegion
        {
            LayerId = region.LayerId,
            StartBeat = region.EndBeat,
            EndBeat = region.EndBeat + span
        };
        _project.Current.VideoVisibilityRegions.Add(copy);
        SelectedVisibilityRegion = copy;
        RebuildLanes();
    }

    public void DeleteVisibilityRegion(VideoVisibilityRegion region)
    {
        if (!_project.Current.VideoVisibilityRegions.Contains(region)) return;
        _history.Capture("Remove visibility region");
        _project.Current.VideoVisibilityRegions.Remove(region);
        SelectedVisibilityRegion = _project.Current.VideoVisibilityRegions
            .FirstOrDefault(r => r.LayerId == region.LayerId);
        RebuildLanes();
    }

    public void ReorderLayer(VideoLayer layer, int targetIndex)
    {
        var ordered = _project.Current.VideoLayers.OrderBy(l => l.ZOrder).ToList();
        var currentIndex = ordered.FindIndex(l => l.Id == layer.Id);
        if (currentIndex < 0 || currentIndex == targetIndex) return;
        _history.Capture("Reorder video layer");
        ordered.RemoveAt(currentIndex);
        targetIndex = Math.Clamp(targetIndex, 0, ordered.Count);
        ordered.Insert(targetIndex, layer);
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].ZOrder = i;
        RebuildLanes();
    }

    public static VideoElementKind DetectKind(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".gif" => VideoElementKind.AnimatedGif,
            ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or ".m4v" => VideoElementKind.Video,
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tif" or ".tiff" => VideoElementKind.Image,
            _ => VideoElementKind.Image
        };
    }

    public static VideoLayer CreateLayerFromPath(string path)
    {
        var kind = DetectKind(path);
        var name = string.IsNullOrWhiteSpace(path) ? "Layer" : Path.GetFileName(path);
        var layer = new VideoLayer { Name = name };
        var item = VideoLayer.CreateDefaultItem();
        item.Kind = kind;
        item.SourcePath = path;
        if (kind == VideoElementKind.Video)
        {
            item.X = 0;
            item.Y = 0;
            item.Width = 1;
            item.Height = 1;
        }

        layer.Items.Add(item);
        return layer;
    }

    private bool CanMoveLayerUp() => SelectedLayer is not null
        && _project.Current.VideoLayers.OrderBy(l => l.ZOrder).FirstOrDefault()?.Id != SelectedLayer.Id;

    private bool CanMoveLayerDown() => SelectedLayer is not null
        && _project.Current.VideoLayers.OrderBy(l => l.ZOrder).LastOrDefault()?.Id != SelectedLayer.Id;

    private void MoveLayer(int delta)
    {
        if (SelectedLayer is null) return;
        var ordered = _project.Current.VideoLayers.OrderBy(l => l.ZOrder).ToList();
        var index = ordered.FindIndex(l => l.Id == SelectedLayer.Id);
        if (index < 0) return;
        var target = index + delta;
        if (target < 0 || target >= ordered.Count) return;
        ReorderLayer(SelectedLayer, target);
        MoveLayerUpCommand.RaiseCanExecuteChanged();
        MoveLayerDownCommand.RaiseCanExecuteChanged();
    }

    private void Rebuild()
    {
        RebuildAudioSourceTrackOptions();
        _waveformCache.Invalidate();
        ArrangementClips.Clear();
        foreach (var track in _project.Current.Tracks)
        {
            foreach (var clip in track.Clips)
                ArrangementClips.Add(new ClipSyncOption(track.Name, clip.Name, clip.Id, clip.StartBeat, clip.EndBeat));
        }

        var bars = _project.Current.BarCount;
        var beatsPerBar = BeatsPerBar;
        Metrics.BeatsPerBar = beatsPerBar;
        Metrics.TotalBeats = bars * beatsPerBar;

        Bars.Clear();
        for (var bar = 0; bar < bars; bar++)
            Bars.Add(new BarTickViewModel(bar + 1, Metrics));

        OnPropertyChanged(nameof(IsProjectVideoEnabled));
        OnPropertyChanged(nameof(HasInspectorSelection));
        OnPropertyChanged(nameof(ShowInspectorPlaceholder));
        OnPropertyChanged(nameof(TargetLayerOptions));
        AddLayerCommand.RaiseCanExecuteChanged();
        AddTriggerCommand.RaiseCanExecuteChanged();
        MoveLayerUpCommand.RaiseCanExecuteChanged();
        MoveLayerDownCommand.RaiseCanExecuteChanged();
        RebuildLanes();
    }

    private void RebuildLanes()
    {
        OverlayLanes.Clear();

        foreach (var layer in _project.Current.VideoLayers.OrderBy(l => l.ZOrder))
        {
            var markers = new ObservableCollection<VideoTriggerMarkerViewModel>();
            foreach (var tr in _project.Current.VideoTriggers.Where(t => t.TargetLayerId == layer.Id))
            {
                var beat = VideoTimelineHelper.TriggerBeat(tr, _project.Current);
                var label = VideoTimelineHelper.TriggerLabel(tr, _project.Current, ArrangementClips.ToList(),
                    _project.Current.VideoLayers, L("VideoTrack_Any_clip"), L("VideoTrack_Unknown_layer"),
                    TriggerMomentOptions, TriggerActionOptions);
                markers.Add(new VideoTriggerMarkerViewModel(tr, layer, beat, label, Metrics,
                    _selection.SelectedTrigger?.Id == tr.Id));
            }

            var blocks = new ObservableCollection<VideoVisibilityBlockViewModel>();
            foreach (var region in _project.Current.VideoVisibilityRegions.Where(r => r.LayerId == layer.Id))
            {
                blocks.Add(new VideoVisibilityBlockViewModel(region, Metrics,
                    _selection.SelectedVisibilityRegion?.Id == region.Id));
            }

            OverlayLanes.Add(new VideoOverlayLaneViewModel(layer, Metrics, markers, blocks,
                _selection.SelectedLayer?.Id == layer.Id));
        }

        RemoveLayerCommand.RaiseCanExecuteChanged();
        RemoveLayerItemCommand.RaiseCanExecuteChanged();
        RemoveTriggerCommand.RaiseCanExecuteChanged();
        BrowseLayerItemCommand.RaiseCanExecuteChanged();
        SyncToClipCommand.RaiseCanExecuteChanged();
        AddVisibilityRegionCommand.RaiseCanExecuteChanged();
        RemoveVisibilityRegionCommand.RaiseCanExecuteChanged();
        MoveLayerUpCommand.RaiseCanExecuteChanged();
        MoveLayerDownCommand.RaiseCanExecuteChanged();
        LanesChanged?.Invoke();
    }

    private void OnExternalSelectionChanged()
    {
        if (_syncingSelection) return;
        NotifyInspector();
        RebuildLanes();
    }

    private void NotifyInspector()
    {
        OnPropertyChanged(nameof(SelectedLayer));
        OnPropertyChanged(nameof(SelectedLayerItem));
        OnPropertyChanged(nameof(SelectedLayerItems));
        OnPropertyChanged(nameof(SelectedLayerName));
        OnPropertyChanged(nameof(SelectedTrigger));
        OnPropertyChanged(nameof(SelectedVisibilityRegion));
        OnPropertyChanged(nameof(RegionStartBeat));
        OnPropertyChanged(nameof(RegionEndBeat));
        OnPropertyChanged(nameof(LayerOffsetSeconds));
        OnPropertyChanged(nameof(LayerInPointSeconds));
        OnPropertyChanged(nameof(LayerOutPointSeconds));
        OnPropertyChanged(nameof(LayerFps));
        OnPropertyChanged(nameof(SelectedLayerItemKind));
        OnPropertyChanged(nameof(SelectedTriggerMoment));
        OnPropertyChanged(nameof(SelectedTriggerAction));
        OnPropertyChanged(nameof(SelectedTriggerClip));
        OnPropertyChanged(nameof(SelectedTriggerTarget));
        OnPropertyChanged(nameof(HasInspectorSelection));
        OnPropertyChanged(nameof(ShowInspectorPlaceholder));
        OnPropertyChanged(nameof(ShowLayerSyncInspector));
        OnPropertyChanged(nameof(ShowLayerMediaInspector));
        OnPropertyChanged(nameof(ShowLayerWaveformInspector));
        OnPropertyChanged(nameof(LayerContentKind));
        OnPropertyChanged(nameof(SelectedLayerContentKind));
        OnPropertyChanged(nameof(SelectedAudioSourceTrack));
        OnPropertyChanged(nameof(SelectedWaveformStyle));
        OnPropertyChanged(nameof(SelectedWaveformColor));
        OnPropertyChanged(nameof(WaveformFollowPlayhead));
        OnPropertyChanged(nameof(WaveformX));
        OnPropertyChanged(nameof(WaveformY));
        OnPropertyChanged(nameof(WaveformWidth));
        OnPropertyChanged(nameof(WaveformHeight));
        OnPropertyChanged(nameof(SelectedVisualiserColorMode));
        OnPropertyChanged(nameof(SelectedVisualiserSecondaryColor));
        OnPropertyChanged(nameof(ShowVisualiserGradientColor));
        OnPropertyChanged(nameof(ShowVisualiserSpectrumSettings));
        OnPropertyChanged(nameof(ShowVisualiserLineThickness));
        OnPropertyChanged(nameof(SpectrumMinHz));
        OnPropertyChanged(nameof(SpectrumMaxHz));
        OnPropertyChanged(nameof(SpectrumLineThickness));
        RemoveLayerCommand.RaiseCanExecuteChanged();
        RemoveLayerItemCommand.RaiseCanExecuteChanged();
        RemoveTriggerCommand.RaiseCanExecuteChanged();
        BrowseLayerItemCommand.RaiseCanExecuteChanged();
        AddLayerItemCommand.RaiseCanExecuteChanged();
        AddVisibilityRegionCommand.RaiseCanExecuteChanged();
        RemoveVisibilityRegionCommand.RaiseCanExecuteChanged();
        MoveLayerUpCommand.RaiseCanExecuteChanged();
        MoveLayerDownCommand.RaiseCanExecuteChanged();
    }

    private void AddLayer()
    {
        _history.Capture("Add video layer");
        var index = _project.Current.VideoLayers.Count;
        var layer = new VideoLayer
        {
            Name = $"Layer {index + 1}",
            ZOrder = index
        };
        _project.Current.VideoLayers.Add(layer);
        SelectedLayer = layer;
        SelectedLayerItem = null;
        OnPropertyChanged(nameof(TargetLayerOptions));
        AddTriggerCommand.RaiseCanExecuteChanged();
        RebuildLanes();
    }

    private bool CanAddLayerItem() =>
        SelectedLayer is not null && LayerContentKind != VideoLayerContentKind.Waveform;

    private bool CanBrowseLayerMedia() =>
        SelectedLayer is not null && LayerContentKind != VideoLayerContentKind.Waveform;

    private async Task BrowseLayerMediaAsync()
    {
        if (SelectedLayer is null || PickImagePathAsync is null) return;
        if (SelectedLayerItem is null)
        {
            _history.Capture("Add overlay item");
            var item = VideoLayer.CreateDefaultItem();
            SelectedLayer.Items.Add(item);
            SelectedLayerItem = item;
            RebuildLanes();
        }

        await BrowseLayerItemAsync();
    }

    private void ApplyLayerContentKind(VideoLayerContentKind kind)
    {
        if (SelectedLayer is null) return;
        _history.Capture("Change layer content kind");
        switch (kind)
        {
            case VideoLayerContentKind.Waveform:
                SelectedLayer.Items.Clear();
                SelectedLayerItem = null;
                if (SelectedLayer.AudioSourceTrackId is null)
                    SelectedLayer.AudioSourceTrackId = AudioSourceTrackOptions.FirstOrDefault(o => o.Id is not null)?.Id;
                ApplyDefaultWaveformLayout(SelectedLayer);
                break;
            case VideoLayerContentKind.Media:
                SelectedLayer.AudioSourceTrackId = null;
                if (SelectedLayer.Items.Count == 0)
                {
                    var item = VideoLayer.CreateDefaultItem();
                    SelectedLayer.Items.Add(item);
                    SelectedLayerItem = item;
                }
                break;
            default:
                SelectedLayer.AudioSourceTrackId = null;
                SelectedLayer.Items.Clear();
                SelectedLayerItem = null;
                break;
        }

        _waveformCache.Invalidate();
        RebuildLanes();
        NotifyInspector();
    }

    private void ApplyDefaultWaveformLayout(VideoLayer layer)
    {
        var wfIndex = _project.Current.VideoLayers.Count(l => l.IsWaveformLayer);
        layer.WaveformX = 0.1;
        layer.WaveformWidth = 0.8;
        layer.WaveformHeight = 0.12;
        layer.WaveformY = Math.Clamp(0.82 - wfIndex * 0.14, 0.05, 0.82);
    }

    private void SetWaveformBound(Action<double> setter, double value, string propertyName)
    {
        if (SelectedLayer is null) return;
        var clamped = Math.Clamp(value, 0, 1);
        setter(clamped);
        OnPropertyChanged(propertyName);
    }

    private void RebuildAudioSourceTrackOptions()
    {
        AudioSourceTrackOptions.Clear();
        AudioSourceTrackOptions.Add(new VideoAudioSourceOption(null, L("VideoTrack_Waveform_none")));
        foreach (var track in _project.Current.Tracks.Where(t => t.Kind != TrackKind.Master))
        {
            var suffix = track.Kind switch
            {
                TrackKind.Group => L("VideoTrack_Waveform_group"),
                TrackKind.Return => L("VideoTrack_Waveform_return"),
                _ => L("VideoTrack_Waveform_track")
            };
            AudioSourceTrackOptions.Add(new VideoAudioSourceOption(track.Id, $"{track.Name} ({suffix})"));
        }
    }

    private void RemoveLayer()
    {
        if (SelectedLayer is null) return;
        _history.Capture("Remove video layer");
        var id = SelectedLayer.Id;
        _project.Current.VideoLayers.Remove(SelectedLayer);
        foreach (var tr in _project.Current.VideoTriggers.Where(t => t.TargetLayerId == id).ToList())
            _project.Current.VideoTriggers.Remove(tr);
        foreach (var region in _project.Current.VideoVisibilityRegions.Where(r => r.LayerId == id).ToList())
            _project.Current.VideoVisibilityRegions.Remove(region);
        SelectedLayer = _project.Current.VideoLayers.FirstOrDefault();
        SelectedTrigger = _project.Current.VideoTriggers.FirstOrDefault();
        OnPropertyChanged(nameof(TargetLayerOptions));
        AddTriggerCommand.RaiseCanExecuteChanged();
        RebuildLanes();
    }

    private void AddLayerItem()
    {
        if (SelectedLayer is null) return;
        _history.Capture("Add overlay item");
        var item = VideoLayer.CreateDefaultItem();
        SelectedLayer.Items.Add(item);
        SelectedLayerItem = item;
        RebuildLanes();
    }

    private void RemoveLayerItem()
    {
        if (SelectedLayer is null || SelectedLayerItem is null) return;
        _history.Capture("Remove overlay item");
        SelectedLayer.Items.Remove(SelectedLayerItem);
        SelectedLayerItem = SelectedLayer.Items.FirstOrDefault();
        RebuildLanes();
    }

    private void AddVisibilityRegion()
    {
        if (SelectedLayer is null) return;
        var start = _transport.PlayheadBeats;
        CreateVisibilityRegion(SelectedLayer, start, start + 4);
    }

    private void RemoveVisibilityRegion()
    {
        if (SelectedVisibilityRegion is null) return;
        _history.Capture("Remove visibility region");
        _project.Current.VideoVisibilityRegions.Remove(SelectedVisibilityRegion);
        SelectedVisibilityRegion = _project.Current.VideoVisibilityRegions.FirstOrDefault();
        RebuildLanes();
    }

    private void AddTrigger()
    {
        _history.Capture("Add video trigger");
        var tr = new VideoTrigger
        {
            TargetLayerId = SelectedLayer?.Id ?? _project.Current.VideoLayers[0].Id,
            ClipId = SelectedSyncClip?.ClipId ?? ArrangementClips.FirstOrDefault()?.ClipId
        };
        _project.Current.VideoTriggers.Add(tr);
        SelectedTrigger = tr;
        RebuildLanes();
    }

    private void RemoveTrigger()
    {
        if (SelectedTrigger is null) return;
        _history.Capture("Remove video trigger");
        _project.Current.VideoTriggers.Remove(SelectedTrigger);
        SelectedTrigger = _project.Current.VideoTriggers.FirstOrDefault();
        RebuildLanes();
    }

    private void SyncToSelectedClip()
    {
        if (SelectedLayer is null || SelectedSyncClip is null) return;
        _history.Capture("Sync video to clip");
        var clip = SelectedSyncClip;
        SelectedLayer.SyncClipId = clip.ClipId;
        SelectedLayer.OffsetSeconds = -_tempoMap.BeatsToSeconds(_project.Current, clip.StartBeat);
        SelectedLayer.InPointSeconds = 0;
        var clipDuration = _tempoMap.BeatsToSeconds(_project.Current, clip.EndBeat)
                           - _tempoMap.BeatsToSeconds(_project.Current, clip.StartBeat);
        SelectedLayer.OutPointSeconds = clipDuration;
        RebuildLanes();
    }

    private async Task BrowseLayerItemAsync()
    {
        if (SelectedLayerItem is null || PickImagePathAsync is null) return;
        var path = await PickImagePathAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _history.Capture("Set overlay item file");
            SelectedLayerItem.SourcePath = path;
            SelectedLayerItem.Kind = DetectKind(path);
            RebuildLanes();
        }
    }

    private static ObservableCollection<EnumOption<VideoLayerContentKind>> BuildLayerContentKindOptions() => new()
    {
        new(VideoLayerContentKind.Empty, L("VideoTrack_Content_empty")),
        new(VideoLayerContentKind.Media, L("VideoTrack_Content_media")),
        new(VideoLayerContentKind.Waveform, L("VideoTrack_Content_visualiser"))
    };

    private void SetVisualiserDouble(Action<double> setter, double value, string propertyName, double min, double max)
    {
        if (SelectedLayer is null) return;
        var clamped = Math.Clamp(value, min, max);
        setter(clamped);
        OnPropertyChanged(propertyName);
    }

    private static ObservableCollection<EnumOption<VideoVisualiserColorMode>> BuildVisualiserColorModeOptions() => new()
    {
        new(VideoVisualiserColorMode.Solid, L("VideoTrack_Visualiser_color_solid")),
        new(VideoVisualiserColorMode.Gradient, L("VideoTrack_Visualiser_color_gradient"))
    };

    private static ObservableCollection<EnumOption<VideoWaveformStyle>> BuildWaveformStyleOptions() => new()
    {
        new(VideoWaveformStyle.Mirrored, L("VideoTrack_Visualiser_style_waveform")),
        new(VideoWaveformStyle.Bars, L("VideoTrack_Visualiser_style_volume_bars")),
        new(VideoWaveformStyle.Spectrum, L("VideoTrack_Visualiser_style_spectrum"))
    };

    private static ObservableCollection<WaveformColorPreset> BuildWaveformColorPresets() => new()
    {
        new(L("VideoTrack_Waveform_color_red"), 0xFFE64553),
        new(L("VideoTrack_Waveform_color_blue"), 0xFF457EE6),
        new(L("VideoTrack_Waveform_color_green"), 0xFF40A02B),
        new(L("VideoTrack_Waveform_color_teal"), 0xFF179299),
        new(L("VideoTrack_Waveform_color_peach"), 0xFFFFB080),
        new(L("VideoTrack_Waveform_color_mauve"), 0xFFBB9AF7)
    };

    private static ObservableCollection<EnumOption<VideoElementKind>> BuildElementKindOptions() => new()
    {
        new(VideoElementKind.Image, L("VideoTrack_Kind_Image")),
        new(VideoElementKind.AnimatedGif, L("VideoTrack_Kind_AnimatedGif")),
        new(VideoElementKind.Video, L("VideoTrack_Kind_Video"))
    };

    private static ObservableCollection<EnumOption<VideoTriggerMoment>> BuildTriggerMomentOptions() => new()
    {
        new(VideoTriggerMoment.ClipStart, L("VideoTrack_Moment_ClipStart")),
        new(VideoTriggerMoment.ClipEnd, L("VideoTrack_Moment_ClipEnd")),
        new(VideoTriggerMoment.NoteOn, L("VideoTrack_Moment_NoteOn")),
        new(VideoTriggerMoment.NoteOff, L("VideoTrack_Moment_NoteOff")),
    };

    private static ObservableCollection<EnumOption<VideoTriggerAction>> BuildTriggerActionOptions() => new()
    {
        new(VideoTriggerAction.Show, L("VideoTrack_Action_Show")),
        new(VideoTriggerAction.Hide, L("VideoTrack_Action_Hide")),
        new(VideoTriggerAction.Toggle, L("VideoTrack_Action_Toggle")),
        new(VideoTriggerAction.FadeIn, L("VideoTrack_Action_FadeIn")),
        new(VideoTriggerAction.FadeOut, L("VideoTrack_Action_FadeOut")),
    };
}
