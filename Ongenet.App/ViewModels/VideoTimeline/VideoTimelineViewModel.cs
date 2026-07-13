using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.Panels;
using Ongenet.App.ViewModels.Timeline;
using Ongenet.App.Views.Windows;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Engine3D.Abstractions;

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
    private readonly IVideoProxyCacheService _proxyCache;
    private readonly IProjectFileService _projectFile;
    private ClipSyncOption? _selectedSyncClip;
    private bool _syncingSelection;
    private bool _visibilityRegionEditing;
    private bool _generatingProxy;
    private string _proxyCacheStatus = "";

    public VideoTimelineViewModel(IProjectService project, IHistoryService history,
        ITempoMapService tempoMap, IVideoSelectionService selection,
        ITimelineLayoutService layout, ITransportSeekService seek, ITransportService transport,
        IVideoWaveformCacheService waveformCache, IVideoProxyCacheService proxyCache,
        IProjectFileService projectFile)
    {
        _project = project;
        _history = history;
        _tempoMap = tempoMap;
        _selection = selection;
        _layout = layout;
        _seek = seek;
        _transport = transport;
        _waveformCache = waveformCache;
        _proxyCache = proxyCache;
        _projectFile = projectFile;

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
        ImportMarkersAsTriggersCommand = new RelayCommand(ImportMarkersAsTriggers, () => _project.Current.Markers.Count > 0);
        AddKeyframeAtPlayheadCommand = new RelayCommand(AddKeyframeAtPlayhead, () => SelectedLayerItem is not null);
        BrowseSubtitleSrtCommand = new RelayCommand(() => _ = BrowseSubtitleSrtAsync(), () => SelectedLayerItem?.Kind == VideoElementKind.Subtitle);
        BrowseLutCubeCommand = new RelayCommand(() => _ = BrowseLutCubeAsync(), () => ShowLayerItemEffectsInspector);
        BrowseMaskImageCommand = new RelayCommand(() => _ = BrowseMaskImageAsync(), () => ShowLayerItemEffectsInspector);
        BrowseEngine3DImageCommand = new RelayCommand(() => _ = BrowseEngine3DImageAsync(), () => ShowEngine3DTexturedCubeInspector);
        GenerateProxyCommand = new RelayCommand(() => _ = GenerateProxyAsync(), CanGenerateProxy);
        CaptureEngine3DSnapshotCommand = new RelayCommand(() => _ = CaptureEngine3DSnapshotAsync(), () => ShowEngine3DCapture);

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
    public ObservableCollection<EnumOption<VideoEngine3DEffectKind>> Engine3DEffectKindOptions { get; } = BuildEngine3DEffectKindOptions();
    public ObservableCollection<EnumOption<VideoEngine3DParticleShape>> Engine3DParticleShapeOptions { get; } = BuildEngine3DParticleShapeOptions();
    public ObservableCollection<EnumOption<VideoVisualiserColorMode>> VisualiserColorModeOptions { get; } = BuildVisualiserColorModeOptions();
    public ObservableCollection<VideoAudioSourceOption> AudioSourceTrackOptions { get; } = new();
    public ObservableCollection<WaveformColorPreset> WaveformColorPresets { get; } = BuildWaveformColorPresets();
    public ObservableCollection<EnumOption<VideoTriggerMoment>> TriggerMomentOptions { get; } = BuildTriggerMomentOptions();
    public ObservableCollection<EnumOption<VideoTriggerAction>> TriggerActionOptions { get; } = BuildTriggerActionOptions();
    public ObservableCollection<EnumOption<VideoTriggerSource>> TriggerSourceOptions { get; } = BuildTriggerSourceOptions();
    public ObservableCollection<VideoLayer> TargetLayerOptions => new(_project.Current.VideoLayers);

    public bool IsProjectVideoEnabled => _project.Current.VideoEnabled;
    public bool HasInspectorSelection => SelectedLayer is not null
        || SelectedTrigger is not null || SelectedVisibilityRegion is not null;
    public bool ShowInspectorPlaceholder => IsProjectVideoEnabled && !HasInspectorSelection;
    public bool ShowInspectorPanel => HasInspectorSelection || ShowInspectorPlaceholder;
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
    public bool ShowLayerEngine3DInspector => SelectedLayer is not null && LayerContentKind == VideoLayerContentKind.Engine3D;

    public bool ShowVisualiserGradientColor => SelectedLayer?.VisualiserColorMode == VideoVisualiserColorMode.Gradient
        && SelectedLayer.WaveformStyle != VideoWaveformStyle.Scope3D;

    public bool ShowVisualiserSpectrumSettings => SelectedLayer?.WaveformStyle == VideoWaveformStyle.Spectrum;

    public bool ShowVisualiserLineThickness => SelectedLayer?.WaveformStyle is VideoWaveformStyle.Spectrum
        or VideoWaveformStyle.Mirrored or VideoWaveformStyle.Scope3D;

    public bool ShowVisualiserSpectrumLineThickness => SelectedLayer?.WaveformStyle is VideoWaveformStyle.Spectrum
        or VideoWaveformStyle.Mirrored;

    public bool ShowScope3DInspector => SelectedLayer?.WaveformStyle == VideoWaveformStyle.Scope3D;

    public bool ShowVisualiser2DColorSettings => SelectedLayer?.WaveformStyle != VideoWaveformStyle.Scope3D;

    public bool ShowEngine3DTexturedCubeInspector => SelectedLayer?.Engine3DEffectKind == VideoEngine3DEffectKind.TexturedCube;

    public bool ShowEngine3DParticlesInspector => SelectedLayer?.Engine3DEffectKind == VideoEngine3DEffectKind.Particles;

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
            OnPropertyChanged(nameof(ShowScope3DInspector));
            OnPropertyChanged(nameof(ShowVisualiser2DColorSettings));
            OnPropertyChanged(nameof(ShowVisualiserGradientColor));
        }
    }

    public EnumOption<VideoEngine3DEffectKind>? SelectedEngine3DEffectKind
    {
        get => SelectedLayer?.Engine3DEffectKind is { } k
            ? Engine3DEffectKindOptions.FirstOrDefault(o => o.Value == k)
            : null;
        set
        {
            if (SelectedLayer is null || value is null || SelectedLayer.Engine3DEffectKind == value.Value) return;
            _history.Capture("Set 3D FX effect");
            SelectedLayer.Engine3DEffectKind = value.Value;
            if (value.Value == VideoEngine3DEffectKind.Particles
                && SelectedLayer.Engine3DAudioSourceTrackId is null)
            {
                SelectedLayer.Engine3DAudioSourceTrackId = AudioSourceTrackOptions
                    .FirstOrDefault(o => o.Id is not null)?.Id;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowEngine3DTexturedCubeInspector));
            OnPropertyChanged(nameof(ShowEngine3DParticlesInspector));
            OnPropertyChanged(nameof(SelectedEngine3DAudioSourceTrack));
            RebuildLanes();
        }
    }

    public VideoAudioSourceOption? SelectedEngine3DAudioSourceTrack
    {
        get
        {
            var id = SelectedLayer?.Engine3DAudioSourceTrackId;
            return AudioSourceTrackOptions.FirstOrDefault(o => o.Id == id)
                   ?? AudioSourceTrackOptions.FirstOrDefault();
        }
        set
        {
            if (SelectedLayer is null || value?.Id == SelectedLayer.Engine3DAudioSourceTrackId) return;
            _history.Capture("Set 3D FX audio source");
            SelectedLayer.Engine3DAudioSourceTrackId = value?.Id;
            OnPropertyChanged();
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

    public double Scope3DCameraYaw
    {
        get => SelectedLayer?.Scope3DCameraYaw ?? 0.5;
        set => SetVisualiserDouble(v => SelectedLayer!.Scope3DCameraYaw = v, value, nameof(Scope3DCameraYaw), -Math.PI, Math.PI);
    }

    public double Scope3DCameraPitch
    {
        get => SelectedLayer?.Scope3DCameraPitch ?? 0.32;
        set => SetVisualiserDouble(v => SelectedLayer!.Scope3DCameraPitch = v, value, nameof(Scope3DCameraPitch), -1.2, 1.2);
    }

    public double Scope3DCameraDistance
    {
        get => SelectedLayer?.Scope3DCameraDistance ?? 3.8;
        set => SetVisualiserDouble(v => SelectedLayer!.Scope3DCameraDistance = v, value, nameof(Scope3DCameraDistance), 1.5, 12);
    }

    public double Scope3DLineThickness
    {
        get => SelectedLayer?.Scope3DLineThickness ?? 0.018;
        set => SetVisualiserDouble(v => SelectedLayer!.Scope3DLineThickness = v, value, nameof(Scope3DLineThickness), 0.005, 0.08);
    }

    public int Scope3DTrailCount
    {
        get => SelectedLayer?.Scope3DTrailCount ?? 20;
        set
        {
            if (SelectedLayer is null) return;
            var clamped = Math.Clamp(value, 4, 40);
            if (SelectedLayer.Scope3DTrailCount == clamped) return;
            _history.Capture("Set 3D scope trail count");
            SelectedLayer.Scope3DTrailCount = clamped;
            OnPropertyChanged();
        }
    }

    public bool Scope3DTransparentBackground
    {
        get => SelectedLayer?.Scope3DTransparentBackground ?? true;
        set
        {
            if (SelectedLayer is null || SelectedLayer.Scope3DTransparentBackground == value) return;
            _history.Capture("Toggle 3D scope transparency");
            SelectedLayer.Scope3DTransparentBackground = value;
            OnPropertyChanged();
        }
    }

    public double Engine3DX
    {
        get => SelectedLayer?.Engine3DX ?? 0.25;
        set => SetEngine3DBound(v => SelectedLayer!.Engine3DX = v, value, nameof(Engine3DX));
    }

    public double Engine3DY
    {
        get => SelectedLayer?.Engine3DY ?? 0.25;
        set => SetEngine3DBound(v => SelectedLayer!.Engine3DY = v, value, nameof(Engine3DY));
    }

    public double Engine3DWidth
    {
        get => SelectedLayer?.Engine3DWidth ?? 0.5;
        set => SetEngine3DBound(v => SelectedLayer!.Engine3DWidth = Math.Clamp(v, 0.05, 1), value, nameof(Engine3DWidth));
    }

    public double Engine3DHeight
    {
        get => SelectedLayer?.Engine3DHeight ?? 0.5;
        set => SetEngine3DBound(v => SelectedLayer!.Engine3DHeight = Math.Clamp(v, 0.03, 1), value, nameof(Engine3DHeight));
    }

    public double Engine3DCameraYaw
    {
        get => SelectedLayer?.Engine3DCameraYaw ?? 0.6;
        set => SetVisualiserDouble(v => SelectedLayer!.Engine3DCameraYaw = v, value, nameof(Engine3DCameraYaw), -Math.PI, Math.PI);
    }

    public double Engine3DCameraPitch
    {
        get => SelectedLayer?.Engine3DCameraPitch ?? 0.35;
        set => SetVisualiserDouble(v => SelectedLayer!.Engine3DCameraPitch = v, value, nameof(Engine3DCameraPitch), -1.2, 1.2);
    }

    public double Engine3DCameraDistance
    {
        get => SelectedLayer?.Engine3DCameraDistance ?? 4.0;
        set => SetVisualiserDouble(v => SelectedLayer!.Engine3DCameraDistance = v, value, nameof(Engine3DCameraDistance), 1.5, 12);
    }

    public int Engine3DParticleCount
    {
        get => SelectedLayer?.Engine3DParticleCount ?? 128;
        set
        {
            if (SelectedLayer is null) return;
            var clamped = Math.Clamp(value, 16, 384);
            if (SelectedLayer.Engine3DParticleCount == clamped) return;
            _history.Capture("Set 3D particle count");
            SelectedLayer.Engine3DParticleCount = clamped;
            OnPropertyChanged();
            RebuildLanes();
        }
    }

    public double Engine3DParticleSize
    {
        get => SelectedLayer?.Engine3DParticleSize ?? 0.08;
        set => SetVisualiserDouble(v => SelectedLayer!.Engine3DParticleSize = v, value, nameof(Engine3DParticleSize), 0.01, 0.35);
    }

    public EnumOption<VideoEngine3DParticleShape>? SelectedEngine3DParticleShape
    {
        get => SelectedLayer is null
            ? null
            : Engine3DParticleShapeOptions.FirstOrDefault(o => o.Value == SelectedLayer.Engine3DParticleShape);
        set
        {
            if (SelectedLayer is null || value is null || SelectedLayer.Engine3DParticleShape == value.Value) return;
            _history.Capture("Set 3D particle shape");
            SelectedLayer.Engine3DParticleShape = value.Value;
            OnPropertyChanged();
            RebuildLanes();
        }
    }

    public WaveformColorPreset? SelectedEngine3DParticleColor
    {
        get
        {
            var argb = SelectedLayer?.Engine3DParticleColorArgb ?? 0xFFBB9AF7;
            return WaveformColorPresets.FirstOrDefault(p => p.Argb == argb)
                   ?? WaveformColorPresets.FirstOrDefault();
        }
        set
        {
            if (SelectedLayer is null || value is null || SelectedLayer.Engine3DParticleColorArgb == value.Argb) return;
            _history.Capture("Set 3D particle color");
            SelectedLayer.Engine3DParticleColorArgb = value.Argb;
            OnPropertyChanged();
        }
    }

    public bool Engine3DTransparentBackground
    {
        get => SelectedLayer?.Engine3DTransparentBackground ?? true;
        set
        {
            if (SelectedLayer is null || SelectedLayer.Engine3DTransparentBackground == value) return;
            _history.Capture("Toggle 3D FX transparency");
            SelectedLayer.Engine3DTransparentBackground = value;
            OnPropertyChanged();
        }
    }

    public string? Engine3DImagePath
    {
        get => SelectedLayer?.Engine3DImagePath;
        set
        {
            if (SelectedLayer is null || SelectedLayer.Engine3DImagePath == value) return;
            _history.Capture("Set 3D cube texture");
            SelectedLayer.Engine3DImagePath = value;
            OnPropertyChanged();
            RebuildLanes();
        }
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
    public RelayCommand ImportMarkersAsTriggersCommand { get; }
    public RelayCommand AddKeyframeAtPlayheadCommand { get; }
    public RelayCommand BrowseSubtitleSrtCommand { get; }
    public RelayCommand BrowseLutCubeCommand { get; }
    public RelayCommand BrowseMaskImageCommand { get; }
    public RelayCommand BrowseEngine3DImageCommand { get; }
    public RelayCommand GenerateProxyCommand { get; }
    public RelayCommand CaptureEngine3DSnapshotCommand { get; }

    public bool ShowLayerItemInspector => SelectedLayerItem is not null;
    public bool ShowLayerItemTextInspector => SelectedLayerItem?.Kind is VideoElementKind.Text or VideoElementKind.Subtitle;
    public bool ShowLayerItemSubtitleInspector => SelectedLayerItem?.Kind == VideoElementKind.Subtitle;
    public bool ShowLayerItemMediaPath => SelectedLayerItem?.Kind is VideoElementKind.Image
        or VideoElementKind.AnimatedGif or VideoElementKind.Video or VideoElementKind.Engine3D;
    public bool ShowLayerItemEffectsInspector => SelectedLayerItem?.Kind is VideoElementKind.Image
        or VideoElementKind.AnimatedGif or VideoElementKind.Video or VideoElementKind.Engine3D;
    public bool ShowLayerItemProxyInspector => SelectedLayerItem?.Kind == VideoElementKind.Video
        && _proxyCache.IsAvailable
        && !string.IsNullOrWhiteSpace(SelectedLayerItem.SourcePath);
    public bool ShowEngine3DCapture => SelectedLayerItem?.Kind == VideoElementKind.Engine3D
        && App.ServiceProvider?.GetService<I3DEngineFactory>()?.IsAvailable == true;
    public bool ShowTriggerClipInspector => SelectedTrigger?.Source is VideoTriggerSource.ArrangementClip
        or VideoTriggerSource.SessionClip;
    public bool ShowTriggerMidiCcInspector => SelectedTrigger?.Source == VideoTriggerSource.MidiCc;

    public string LayerItemTextContent
    {
        get => SelectedLayerItem?.TextContent ?? "";
        set
        {
            if (SelectedLayerItem is null || SelectedLayerItem.TextContent == value) return;
            _history.Capture("Edit text content");
            SelectedLayerItem.TextContent = value;
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public double LayerItemFontSizePx
    {
        get => SelectedLayerItem?.FontSizePx ?? 48;
        set
        {
            if (SelectedLayerItem is null || Math.Abs(SelectedLayerItem.FontSizePx - value) < 1e-6) return;
            _history.Capture("Edit text size");
            SelectedLayerItem.FontSizePx = Math.Clamp(value, 8, 256);
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public WaveformColorPreset? SelectedLayerItemTextColor
    {
        get
        {
            if (SelectedLayerItem is null) return null;
            return WaveformColorPresets.FirstOrDefault(p => p.Argb == SelectedLayerItem.TextColorArgb)
                   ?? WaveformColorPresets.Last();
        }
        set
        {
            if (SelectedLayerItem is null || value is null || SelectedLayerItem.TextColorArgb == value.Argb) return;
            _history.Capture("Edit text color");
            SelectedLayerItem.TextColorArgb = value.Argb;
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public string LayerItemSubtitleSrtPath
    {
        get => SelectedLayerItem?.SubtitleSrtPath ?? "";
        set
        {
            if (SelectedLayerItem is null) return;
            var v = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SelectedLayerItem.SubtitleSrtPath == v) return;
            _history.Capture("Set subtitle SRT path");
            SelectedLayerItem.SubtitleSrtPath = v;
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public bool LayerItemChromaKeyEnabled
    {
        get => SelectedLayerItem?.ChromaKeyEnabled ?? false;
        set
        {
            if (SelectedLayerItem is null || SelectedLayerItem.ChromaKeyEnabled == value) return;
            _history.Capture("Toggle chroma key");
            SelectedLayerItem.ChromaKeyEnabled = value;
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public double LayerItemChromaKeyTolerance
    {
        get => SelectedLayerItem?.ChromaKeyTolerance ?? 0.15;
        set
        {
            if (SelectedLayerItem is null || Math.Abs(SelectedLayerItem.ChromaKeyTolerance - value) < 1e-6) return;
            _history.Capture("Edit chroma key tolerance");
            SelectedLayerItem.ChromaKeyTolerance = Math.Clamp(value, 0, 1);
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public double LayerItemBrightness
    {
        get => SelectedLayerItem?.Brightness ?? 1;
        set
        {
            if (SelectedLayerItem is null || Math.Abs(SelectedLayerItem.Brightness - value) < 1e-6) return;
            _history.Capture("Edit brightness");
            SelectedLayerItem.Brightness = Math.Clamp(value, 0, 2);
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public double LayerItemContrast
    {
        get => SelectedLayerItem?.Contrast ?? 1;
        set
        {
            if (SelectedLayerItem is null || Math.Abs(SelectedLayerItem.Contrast - value) < 1e-6) return;
            _history.Capture("Edit contrast");
            SelectedLayerItem.Contrast = Math.Clamp(value, 0, 2);
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public double LayerItemSaturation
    {
        get => SelectedLayerItem?.Saturation ?? 1;
        set
        {
            if (SelectedLayerItem is null || Math.Abs(SelectedLayerItem.Saturation - value) < 1e-6) return;
            _history.Capture("Edit saturation");
            SelectedLayerItem.Saturation = Math.Clamp(value, 0, 2);
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public string LayerItemLutCubePath
    {
        get => SelectedLayerItem?.LutCubePath ?? "";
        set
        {
            if (SelectedLayerItem is null) return;
            var v = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SelectedLayerItem.LutCubePath == v) return;
            _history.Capture("Set LUT path");
            SelectedLayerItem.LutCubePath = v;
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public string LayerItemMaskImagePath
    {
        get => SelectedLayerItem?.MaskImagePath ?? "";
        set
        {
            if (SelectedLayerItem is null) return;
            var v = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SelectedLayerItem.MaskImagePath == v) return;
            _history.Capture("Set mask image path");
            SelectedLayerItem.MaskImagePath = v;
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public string ProxyCacheStatus => _proxyCacheStatus;

    public EnumOption<VideoTriggerSource>? SelectedTriggerSource
    {
        get => SelectedTrigger is null ? null : TriggerSourceOptions.FirstOrDefault(o => o.Value == SelectedTrigger.Source);
        set
        {
            if (SelectedTrigger is null || value is null || SelectedTrigger.Source == value.Value) return;
            _history.Capture("Change trigger source");
            SelectedTrigger.Source = value.Value;
            if (value.Value == VideoTriggerSource.MidiCc)
            {
                SelectedTrigger.MidiCcChannel ??= 0;
                SelectedTrigger.MidiCcNumber ??= 1;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowTriggerClipInspector));
            OnPropertyChanged(nameof(ShowTriggerMidiCcInspector));
            RebuildLanes();
        }
    }

    public int TriggerMidiCcChannel
    {
        get => SelectedTrigger?.MidiCcChannel ?? 0;
        set
        {
            if (SelectedTrigger is null || SelectedTrigger.MidiCcChannel == value) return;
            _history.Capture("Edit MIDI CC channel");
            SelectedTrigger.MidiCcChannel = Math.Clamp(value, 0, 15);
            OnPropertyChanged();
        }
    }

    public int TriggerMidiCcNumber
    {
        get => SelectedTrigger?.MidiCcNumber ?? 0;
        set
        {
            if (SelectedTrigger is null || SelectedTrigger.MidiCcNumber == value) return;
            _history.Capture("Edit MIDI CC number");
            SelectedTrigger.MidiCcNumber = Math.Clamp(value, 0, 127);
            OnPropertyChanged();
        }
    }

    public double TriggerMidiCcThreshold
    {
        get => SelectedTrigger?.MidiCcThreshold ?? 64;
        set
        {
            if (SelectedTrigger is null || Math.Abs(SelectedTrigger.MidiCcThreshold - value) < 1e-6) return;
            _history.Capture("Edit MIDI CC threshold");
            SelectedTrigger.MidiCcThreshold = Math.Clamp(value, 0, 127);
            OnPropertyChanged();
        }
    }

    public double LayerItemRotation
    {
        get => SelectedLayerItem?.Rotation ?? 0;
        set
        {
            if (SelectedLayerItem is null || Math.Abs(SelectedLayerItem.Rotation - value) < 1e-6) return;
            _history.Capture("Edit item rotation");
            SelectedLayerItem.Rotation = value;
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public double LayerItemOpacity
    {
        get => SelectedLayerItem?.Opacity ?? 1;
        set
        {
            if (SelectedLayerItem is null || Math.Abs(SelectedLayerItem.Opacity - value) < 1e-6) return;
            _history.Capture("Edit item opacity");
            SelectedLayerItem.Opacity = Math.Clamp(value, 0, 1);
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public double RegionFadeInBeats
    {
        get => SelectedVisibilityRegion?.FadeInBeats ?? 0;
        set
        {
            if (SelectedVisibilityRegion is null || Math.Abs(SelectedVisibilityRegion.FadeInBeats - value) < 1e-6) return;
            _history.Capture("Edit region fade in");
            SelectedVisibilityRegion.FadeInBeats = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    public double RegionFadeOutBeats
    {
        get => SelectedVisibilityRegion?.FadeOutBeats ?? 0;
        set
        {
            if (SelectedVisibilityRegion is null || Math.Abs(SelectedVisibilityRegion.FadeOutBeats - value) < 1e-6) return;
            _history.Capture("Edit region fade out");
            SelectedVisibilityRegion.FadeOutBeats = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    public EnumOption<VideoBlendMode>? SelectedLayerBlendMode
    {
        get => SelectedLayer is null ? null : BlendModeOptions.FirstOrDefault(o => o.Value == SelectedLayer.BlendMode);
        set
        {
            if (SelectedLayer is null || value is null || SelectedLayer.BlendMode == value.Value) return;
            _history.Capture("Set layer blend mode");
            SelectedLayer.BlendMode = value.Value;
            OnPropertyChanged();
            LanesChanged?.Invoke();
        }
    }

    public ObservableCollection<EnumOption<VideoBlendMode>> BlendModeOptions { get; } = BuildBlendModeOptions();

    public Func<Task<string?>>? PickImagePathAsync { get; set; }
    public Func<Task<string?>>? PickSubtitleSrtPathAsync { get; set; }
    public Func<Task<string?>>? PickLutCubePathAsync { get; set; }
    public Func<Task<string?>>? PickMaskImagePathAsync { get; set; }

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
        OnPropertyChanged(nameof(ShowInspectorPanel));
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
        OnPropertyChanged(nameof(RegionFadeInBeats));
        OnPropertyChanged(nameof(RegionFadeOutBeats));
        OnPropertyChanged(nameof(LayerOffsetSeconds));
        OnPropertyChanged(nameof(LayerInPointSeconds));
        OnPropertyChanged(nameof(LayerOutPointSeconds));
        OnPropertyChanged(nameof(LayerFps));
        OnPropertyChanged(nameof(SelectedLayerItemKind));
        OnPropertyChanged(nameof(ShowLayerItemInspector));
        OnPropertyChanged(nameof(ShowLayerItemTextInspector));
        OnPropertyChanged(nameof(ShowLayerItemMediaPath));
        OnPropertyChanged(nameof(LayerItemTextContent));
        OnPropertyChanged(nameof(LayerItemFontSizePx));
        OnPropertyChanged(nameof(SelectedLayerItemTextColor));
        OnPropertyChanged(nameof(LayerItemSubtitleSrtPath));
        OnPropertyChanged(nameof(LayerItemChromaKeyEnabled));
        OnPropertyChanged(nameof(LayerItemChromaKeyTolerance));
        OnPropertyChanged(nameof(LayerItemBrightness));
        OnPropertyChanged(nameof(LayerItemContrast));
        OnPropertyChanged(nameof(LayerItemSaturation));
        OnPropertyChanged(nameof(LayerItemLutCubePath));
        OnPropertyChanged(nameof(LayerItemMaskImagePath));
        RefreshProxyCacheStatus();
        OnPropertyChanged(nameof(ProxyCacheStatus));
        OnPropertyChanged(nameof(ShowLayerItemProxyInspector));
        OnPropertyChanged(nameof(ShowEngine3DCapture));
        OnPropertyChanged(nameof(ShowLayerItemSubtitleInspector));
        OnPropertyChanged(nameof(ShowLayerItemEffectsInspector));
        OnPropertyChanged(nameof(LayerItemRotation));
        OnPropertyChanged(nameof(LayerItemOpacity));
        OnPropertyChanged(nameof(SelectedLayerBlendMode));
        OnPropertyChanged(nameof(SelectedTriggerSource));
        OnPropertyChanged(nameof(ShowTriggerClipInspector));
        OnPropertyChanged(nameof(ShowTriggerMidiCcInspector));
        OnPropertyChanged(nameof(TriggerMidiCcChannel));
        OnPropertyChanged(nameof(TriggerMidiCcNumber));
        OnPropertyChanged(nameof(TriggerMidiCcThreshold));
        AddKeyframeAtPlayheadCommand.RaiseCanExecuteChanged();
        BrowseSubtitleSrtCommand.RaiseCanExecuteChanged();
        BrowseLutCubeCommand.RaiseCanExecuteChanged();
        BrowseMaskImageCommand.RaiseCanExecuteChanged();
        GenerateProxyCommand.RaiseCanExecuteChanged();
        CaptureEngine3DSnapshotCommand.RaiseCanExecuteChanged();
        ImportMarkersAsTriggersCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedTriggerMoment));
        OnPropertyChanged(nameof(SelectedTriggerAction));
        OnPropertyChanged(nameof(SelectedTriggerClip));
        OnPropertyChanged(nameof(SelectedTriggerTarget));
        OnPropertyChanged(nameof(HasInspectorSelection));
        OnPropertyChanged(nameof(ShowInspectorPlaceholder));
        OnPropertyChanged(nameof(ShowInspectorPanel));
        OnPropertyChanged(nameof(ShowLayerSyncInspector));
        OnPropertyChanged(nameof(ShowLayerMediaInspector));
        OnPropertyChanged(nameof(ShowLayerWaveformInspector));
        OnPropertyChanged(nameof(ShowLayerEngine3DInspector));
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
        OnPropertyChanged(nameof(ShowScope3DInspector));
        OnPropertyChanged(nameof(ShowVisualiser2DColorSettings));
        OnPropertyChanged(nameof(Scope3DCameraYaw));
        OnPropertyChanged(nameof(Scope3DCameraPitch));
        OnPropertyChanged(nameof(Scope3DCameraDistance));
        OnPropertyChanged(nameof(Scope3DLineThickness));
        OnPropertyChanged(nameof(Scope3DTrailCount));
        OnPropertyChanged(nameof(Scope3DTransparentBackground));
        OnPropertyChanged(nameof(SelectedEngine3DEffectKind));
        OnPropertyChanged(nameof(ShowEngine3DTexturedCubeInspector));
        OnPropertyChanged(nameof(ShowEngine3DParticlesInspector));
        OnPropertyChanged(nameof(SelectedEngine3DAudioSourceTrack));
        OnPropertyChanged(nameof(Engine3DX));
        OnPropertyChanged(nameof(Engine3DY));
        OnPropertyChanged(nameof(Engine3DWidth));
        OnPropertyChanged(nameof(Engine3DHeight));
        OnPropertyChanged(nameof(Engine3DCameraYaw));
        OnPropertyChanged(nameof(Engine3DCameraPitch));
        OnPropertyChanged(nameof(Engine3DCameraDistance));
        OnPropertyChanged(nameof(Engine3DParticleCount));
        OnPropertyChanged(nameof(Engine3DParticleSize));
        OnPropertyChanged(nameof(SelectedEngine3DParticleShape));
        OnPropertyChanged(nameof(SelectedEngine3DParticleColor));
        OnPropertyChanged(nameof(Engine3DTransparentBackground));
        OnPropertyChanged(nameof(Engine3DImagePath));
        BrowseEngine3DImageCommand.RaiseCanExecuteChanged();
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
        SelectedLayer is not null
        && LayerContentKind is not VideoLayerContentKind.Waveform
        && LayerContentKind is not VideoLayerContentKind.Engine3D;

    private bool CanBrowseLayerMedia() =>
        SelectedLayer is not null
        && LayerContentKind is not VideoLayerContentKind.Waveform
        && LayerContentKind is not VideoLayerContentKind.Engine3D;

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
                SelectedLayer.Engine3DEffectKind = null;
                if (SelectedLayer.AudioSourceTrackId is null)
                    SelectedLayer.AudioSourceTrackId = AudioSourceTrackOptions.FirstOrDefault(o => o.Id is not null)?.Id;
                ApplyDefaultWaveformLayout(SelectedLayer);
                break;
            case VideoLayerContentKind.Engine3D:
                SelectedLayer.Items.Clear();
                SelectedLayerItem = null;
                SelectedLayer.AudioSourceTrackId = null;
                SelectedLayer.Engine3DEffectKind ??= VideoEngine3DEffectKind.TexturedCube;
                if (SelectedLayer.Engine3DAudioSourceTrackId is null)
                    SelectedLayer.Engine3DAudioSourceTrackId = AudioSourceTrackOptions.FirstOrDefault(o => o.Id is not null)?.Id;
                ApplyDefaultEngine3DLayout(SelectedLayer);
                break;
            case VideoLayerContentKind.Media:
                SelectedLayer.AudioSourceTrackId = null;
                SelectedLayer.Engine3DEffectKind = null;
                if (SelectedLayer.Items.Count == 0)
                {
                    var item = VideoLayer.CreateDefaultItem();
                    SelectedLayer.Items.Add(item);
                    SelectedLayerItem = item;
                }
                break;
            default:
                SelectedLayer.AudioSourceTrackId = null;
                SelectedLayer.Engine3DEffectKind = null;
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

    private void ApplyDefaultEngine3DLayout(VideoLayer layer)
    {
        var fxIndex = _project.Current.VideoLayers.Count(l => l.IsEngine3DLayer);
        layer.Engine3DX = 0.15;
        layer.Engine3DWidth = 0.7;
        layer.Engine3DHeight = 0.45;
        layer.Engine3DY = Math.Clamp(0.45 - fxIndex * 0.2, 0.05, 0.75);
    }

    private void SetWaveformBound(Action<double> setter, double value, string propertyName)
    {
        if (SelectedLayer is null) return;
        var clamped = Math.Clamp(value, 0, 1);
        setter(clamped);
        OnPropertyChanged(propertyName);
    }

    private void SetEngine3DBound(Action<double> setter, double value, string propertyName)
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
        new(VideoLayerContentKind.Waveform, L("VideoTrack_Content_visualiser")),
        new(VideoLayerContentKind.Engine3D, L("VideoTrack_Content_engine3d"))
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
        new(VideoWaveformStyle.Spectrum, L("VideoTrack_Visualiser_style_spectrum")),
        new(VideoWaveformStyle.Scope3D, L("VideoTrack_Visualiser_style_scope3d"))
    };

    private static ObservableCollection<EnumOption<VideoEngine3DParticleShape>> BuildEngine3DParticleShapeOptions() => new()
    {
        new(VideoEngine3DParticleShape.Disc, L("VideoTrack_Engine3d_particle_shape_disc")),
        new(VideoEngine3DParticleShape.Quad, L("VideoTrack_Engine3d_particle_shape_quad")),
        new(VideoEngine3DParticleShape.Point, L("VideoTrack_Engine3d_particle_shape_point"))
    };

    private static ObservableCollection<EnumOption<VideoEngine3DEffectKind>> BuildEngine3DEffectKindOptions() => new()
    {
        new(VideoEngine3DEffectKind.TexturedCube, L("VideoTrack_Engine3d_effect_cube")),
        new(VideoEngine3DEffectKind.Particles, L("VideoTrack_Engine3d_effect_particles"))
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

    private void AddKeyframeAtPlayhead()
    {
        if (SelectedLayerItem is null) return;
        _history.Capture("Add video keyframe");
        _project.Current.VideoLayerKeyframes.Add(new VideoLayerKeyframe
        {
            ItemId = SelectedLayerItem.Id,
            Beat = _transport.PlayheadBeats,
            X = SelectedLayerItem.X,
            Y = SelectedLayerItem.Y,
            Width = SelectedLayerItem.Width,
            Height = SelectedLayerItem.Height,
            Opacity = SelectedLayerItem.Opacity
        });
    }

    private async Task BrowseSubtitleSrtAsync()
    {
        if (SelectedLayerItem is null || PickSubtitleSrtPathAsync is null) return;
        var path = await PickSubtitleSrtPathAsync();
        if (!string.IsNullOrWhiteSpace(path))
            LayerItemSubtitleSrtPath = path;
    }

    private async Task BrowseLutCubeAsync()
    {
        if (SelectedLayerItem is null || PickLutCubePathAsync is null) return;
        var path = await PickLutCubePathAsync();
        if (!string.IsNullOrWhiteSpace(path))
            LayerItemLutCubePath = path;
    }

    private async Task BrowseMaskImageAsync()
    {
        if (SelectedLayerItem is null || PickMaskImagePathAsync is null) return;
        var path = await PickMaskImagePathAsync();
        if (!string.IsNullOrWhiteSpace(path))
            LayerItemMaskImagePath = path;
    }

    private bool CanGenerateProxy() => ShowLayerItemProxyInspector && !_generatingProxy;

    private void RefreshProxyCacheStatus()
    {
        if (!ShowLayerItemProxyInspector || SelectedLayerItem?.SourcePath is not { } source)
        {
            _proxyCacheStatus = "";
            return;
        }

        var projectDir = _projectFile.CurrentPath is { } p ? Path.GetDirectoryName(p) : null;
        var existing = _proxyCache.GetProxyPath(source, projectDir);
        _proxyCacheStatus = existing is not null
            ? L("VideoTrack_Proxy_ready", Path.GetFileName(existing))
            : "";
    }

    private async Task GenerateProxyAsync()
    {
        if (SelectedLayerItem is null || string.IsNullOrWhiteSpace(SelectedLayerItem.SourcePath)) return;

        var sourcePath = SelectedLayerItem.SourcePath;
        var projectDir = _projectFile.CurrentPath is { } p ? Path.GetDirectoryName(p) : null;

        var existing = _proxyCache.GetProxyPath(sourcePath, projectDir);
        if (existing is not null)
        {
            _proxyCacheStatus = L("VideoTrack_Proxy_ready", Path.GetFileName(existing));
            OnPropertyChanged(nameof(ProxyCacheStatus));
            return;
        }

        _generatingProxy = true;
        _proxyCacheStatus = L("VideoTrack_Proxy_generating");
        OnPropertyChanged(nameof(ProxyCacheStatus));
        GenerateProxyCommand.RaiseCanExecuteChanged();

        try
        {
            var proxy = await _proxyCache.EnsureProxyAsync(sourcePath, projectDir);
            _proxyCacheStatus = proxy is not null
                ? L("VideoTrack_Proxy_ready", Path.GetFileName(proxy))
                : L("VideoTrack_Proxy_failed");
            if (proxy is not null)
                LanesChanged?.Invoke();
        }
        finally
        {
            _generatingProxy = false;
            OnPropertyChanged(nameof(ProxyCacheStatus));
            GenerateProxyCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task BrowseEngine3DImageAsync()
    {
        if (SelectedLayer is null || PickImagePathAsync is null) return;
        var path = await PickImagePathAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            Engine3DImagePath = path;
            RebuildLanes();
        }
    }

    private async Task CaptureEngine3DSnapshotAsync()
    {
        if (SelectedLayerItem is null || SelectedLayerItem.Kind != VideoElementKind.Engine3D) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not Window owner)
            return;

        var path = await Engine3DSnapshotWindow.ShowAsync(owner, _project, _projectFile);
        if (string.IsNullOrWhiteSpace(path)) return;

        _history.Capture("Capture 3D snapshot");
        SelectedLayerItem.SourcePath = path;
        RebuildLanes();
    }

    private void ImportMarkersAsTriggers()
    {
        _history.Capture("Import markers as video triggers");
        var target = SelectedLayer ?? _project.Current.VideoLayers.FirstOrDefault();
        if (target is null) return;
        foreach (var marker in _project.Current.Markers.OrderBy(m => m.Beat))
        {
            var clip = _project.Current.Tracks.SelectMany(t => t.Clips)
                .FirstOrDefault(c => marker.Beat >= c.StartBeat && marker.Beat < c.EndBeat);
            if (clip is null) continue;
            _project.Current.VideoTriggers.Add(new VideoTrigger
            {
                TargetLayerId = target.Id,
                Source = VideoTriggerSource.ArrangementClip,
                ClipId = clip.Id,
                Moment = VideoTriggerMoment.ClipStart,
                Action = VideoTriggerAction.Show,
                FadeDurationSeconds = 0.5
            });
        }

        RebuildLanes();
    }

    private static ObservableCollection<EnumOption<VideoTriggerSource>> BuildTriggerSourceOptions() => new()
    {
        new(VideoTriggerSource.ArrangementClip, L("VideoTrack_Trigger_source_clip")),
        new(VideoTriggerSource.SessionClip, L("VideoTrack_Trigger_source_session")),
        new(VideoTriggerSource.MidiNote, L("VideoTrack_Trigger_source_midi_note")),
        new(VideoTriggerSource.MidiCc, L("VideoTrack_Trigger_source_midi_cc"))
    };

    private static ObservableCollection<EnumOption<VideoBlendMode>> BuildBlendModeOptions() => new()
    {
        new(VideoBlendMode.Normal, L("VideoTrack_Blend_Normal")),
        new(VideoBlendMode.Multiply, L("VideoTrack_Blend_Multiply")),
        new(VideoBlendMode.Screen, L("VideoTrack_Blend_Screen")),
        new(VideoBlendMode.Overlay, L("VideoTrack_Blend_Overlay"))
    };

    private static ObservableCollection<EnumOption<VideoElementKind>> BuildElementKindOptions() => new()
    {
        new(VideoElementKind.Image, L("VideoTrack_Kind_Image")),
        new(VideoElementKind.AnimatedGif, L("VideoTrack_Kind_AnimatedGif")),
        new(VideoElementKind.Video, L("VideoTrack_Kind_Video")),
        new(VideoElementKind.Text, L("VideoTrack_Kind_Text")),
        new(VideoElementKind.Subtitle, L("VideoTrack_Kind_Subtitle")),
        new(VideoElementKind.Engine3D, L("VideoTrack_Kind_Engine3D"))
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
