using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Localization;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.VideoTimeline;
using Ongenet.Core.Models.Media;

namespace Ongenet.App.Views.Panels;

public partial class VideoTimelineView : UserControl
{
    private const double EdgeHitWidth = 8;
    private const double MinRegionBeats = 0.25;

    private enum VisibilityDragMode { None, Move, TrimStart, TrimEnd }

    private readonly FrameTicker _ticker;
    private VideoTimelineViewModel? _vm;
    private IPlaybackClock? _clock;
    private readonly TranslateTransform _playheadXform = new();
    private bool _syncingScroll;
    private VideoVisibilityBlockViewModel? _dragVisibility;
    private VideoOverlayLaneViewModel? _dragLane;
    private VideoOverlayLaneViewModel? _dragReorderLane;
    private Control? _dragReorderHeader;
    private VisibilityDragMode _visibilityDragMode;
    private double _dragStartY;
    private double _dragPressBeat;
    private double _dragOrigStart;
    private double _dragOrigEnd;
    private bool _visibilityDragCaptured;
    private double _regionCreateStartBeat;
    private bool _creatingRegion;
    private double _phAnchorBeat = double.NaN;
    private long _phAnchorMs;

    public VideoTimelineView()
    {
        InitializeComponent();
        PlayheadLine.RenderTransform = _playheadXform;
        _ticker = new FrameTicker(this, OnTick);
        DataContextChanged += (_, _) => AttachVm();
        LanesScroll.ScrollChanged += OnLanesScrollChanged;
        RulerScroll.PointerPressed += OnRulerPointerPressed;
        LanesScroll.AddHandler(PointerMovedEvent, OnLanesPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        LanesScroll.AddHandler(PointerReleasedEvent, OnLanesPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    private void AttachVm()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Metrics.PropertyChanged -= OnMetricsPropertyChanged;
            _vm.LanesChanged -= OnLanesChanged;
            _vm.PickImagePathAsync = null;
            _vm.PickSubtitleSrtPathAsync = null;
            _vm.PickLutCubePathAsync = null;
            _vm.PickMaskImagePathAsync = null;
        }

        _vm = DataContext as VideoTimelineViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Metrics.PropertyChanged += OnMetricsPropertyChanged;
            _vm.LanesChanged += OnLanesChanged;
            _vm.PickImagePathAsync = PickLayerPathAsync;
            _vm.PickSubtitleSrtPathAsync = PickSubtitleSrtPathAsync;
            _vm.PickLutCubePathAsync = PickLutCubePathAsync;
            _vm.PickMaskImagePathAsync = PickMaskImagePathAsync;
            SyncScrollFromMetrics();
            SyncHeaderScroll();
            UpdatePlayhead();
            SyncTickerSpeed();
            UpdateInspectorColumns();
        }
    }

    private void SyncTickerSpeed() => _ticker.SetFast(_vm?.IsPlaying == true && IsVisible);

    private void OnTick()
    {
        if (!IsVisible || _vm is null) return;
        if (_vm.IsPlaying)
            UpdatePlayhead();
        (_clock ??= App.ServiceProvider?.GetService<IPlaybackClock>())?.Pump();
    }

    private void OnLanesChanged()
    {
        SyncHeaderScroll();
        UpdatePlayhead();
    }

    private void SyncHeaderScroll()
    {
        if (_syncingScroll) return;
        HeaderScroll.Offset = new Vector(0, LanesScroll.Offset.Y);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VideoTimelineViewModel.PlayheadBeats) or nameof(VideoTimelineViewModel.IsPlaying))
        {
            if (e.PropertyName == nameof(VideoTimelineViewModel.IsPlaying))
                SyncTickerSpeed();
            UpdatePlayhead();
        }

        if (e.PropertyName is nameof(VideoTimelineViewModel.ShowInspectorPanel))
            UpdateInspectorColumns();
    }

    private void UpdateInspectorColumns()
    {
        if (_vm is null) return;
        var cols = ContentGrid.ColumnDefinitions;
        if (cols.Count < 3) return;

        if (_vm.ShowInspectorPanel)
        {
            cols[1].Width = cols[1].Width.Value > 0 ? cols[1].Width : new GridLength(4);
            cols[2].Width = cols[2].Width.Value > 0 ? cols[2].Width : new GridLength(340);
        }
        else
        {
            cols[1].Width = new GridLength(0);
            cols[2].Width = new GridLength(0);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            SyncTickerSpeed();
    }

    private void OnMetricsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.Timeline.TimelineMetrics.HorizontalOffset))
            SyncScrollFromMetrics();
        if (e.PropertyName is nameof(ViewModels.Timeline.TimelineMetrics.PixelsPerBeat)
            or nameof(ViewModels.Timeline.TimelineMetrics.TotalWidth))
            UpdatePlayhead();
    }

    private void OnLanesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_vm is null || _syncingScroll) return;
        _syncingScroll = true;
        var offset = LanesScroll.Offset;
        RulerScroll.Offset = new Vector(offset.X, 0);
        HeaderScroll.Offset = new Vector(0, offset.Y);
        _vm.Metrics.HorizontalOffset = offset.X;
        _syncingScroll = false;
        UpdatePlayhead();
    }

    private void SyncScrollFromMetrics()
    {
        if (_vm is null || _syncingScroll) return;
        _syncingScroll = true;
        var x = _vm.Metrics.HorizontalOffset;
        LanesScroll.Offset = new Vector(x, LanesScroll.Offset.Y);
        RulerScroll.Offset = new Vector(x, 0);
        _syncingScroll = false;
        UpdatePlayhead();
    }

    private double SmoothPlayheadBeats()
    {
        if (_vm is null) return 0;
        var raw = _vm.PlayheadBeats;
        var now = Environment.TickCount64;
        if (!_vm.IsPlaying || raw != _phAnchorBeat)
        {
            _phAnchorBeat = raw;
            _phAnchorMs = now;
            return raw;
        }

        var elapsed = Math.Min((now - _phAnchorMs) / 1000.0, 0.10);
        return _phAnchorBeat + elapsed * _vm.BeatsPerSecond;
    }

    private void UpdatePlayhead()
    {
        if (_vm is null) return;
        var ppb = _vm.Metrics.PixelsPerBeat;
        var x = SmoothPlayheadBeats() * ppb - _vm.Metrics.HorizontalOffset;
        _playheadXform.X = x;

        var totalHeight = _vm.OverlayLanes.Sum(l => l.LaneHeight);
        PlayheadLine.Height = Math.Max(36, totalHeight > 0 ? totalHeight : LanesScroll.Bounds.Height);
    }

    private double BeatAtPointer(PointerEventArgs e) =>
        BeatAtX(e.GetCurrentPoint(LanesScroll).Position.X);

    private double BeatAtX(double xInLanesScroll) =>
        _vm!.Metrics.PixelsToBeats(xInLanesScroll + _vm.Metrics.HorizontalOffset);

    private void OnRulerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || e.GetCurrentPoint(RulerScroll).Properties.IsRightButtonPressed) return;
        var pos = e.GetCurrentPoint(RulerScroll).Position;
        var beat = _vm.Metrics.PixelsToBeats(pos.X + _vm.Metrics.HorizontalOffset);
        _vm.SeekToBeat(beat, snap: true);
        e.Handled = true;
    }

    private void Marker_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || sender is not Control { DataContext: VideoTriggerMarkerViewModel marker }) return;
        _vm.SelectMarker(marker.Trigger);
        e.Handled = true;
    }

    private static VisibilityDragMode DragModeAtPoint(Border block, PointerPressedEventArgs e)
    {
        var x = e.GetCurrentPoint(block).Position.X;
        var width = block.Bounds.Width;
        if (width <= EdgeHitWidth * 2) return VisibilityDragMode.Move;
        if (x <= EdgeHitWidth) return VisibilityDragMode.TrimStart;
        if (x >= width - EdgeHitWidth) return VisibilityDragMode.TrimEnd;
        return VisibilityDragMode.Move;
    }

    private void BeginVisibilityBlockDrag(Border block, VideoVisibilityBlockViewModel regionBlock, VisibilityDragMode mode, PointerEventArgs e)
    {
        if (_vm is null || e.GetCurrentPoint(LanesScroll).Properties.IsRightButtonPressed) return;
        _vm.SelectVisibilityRegion(regionBlock.Region);
        _dragVisibility = regionBlock;
        _visibilityDragMode = mode;
        _dragPressBeat = BeatAtPointer(e);
        _dragOrigStart = regionBlock.Region.StartBeat;
        _dragOrigEnd = regionBlock.Region.EndBeat;
        _visibilityDragCaptured = false;
        e.Pointer.Capture(LanesScroll);
        e.Handled = true;
    }

    private void VisibilityBlock_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: VideoVisibilityBlockViewModel block }) return;
        if (e.GetCurrentPoint(LanesScroll).Properties.IsRightButtonPressed)
        {
            _vm?.SelectVisibilityRegion(block.Region);
            e.Handled = true;
            return;
        }

        BeginVisibilityBlockDrag((Border)sender, block, DragModeAtPoint((Border)sender, e), e);
    }

    private void VisibilityBlock_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Border block || _dragVisibility is not null) return;
        var x = e.GetCurrentPoint(block).Position.X;
        var width = block.Bounds.Width;
        block.Cursor = width <= EdgeHitWidth * 2 || (x > EdgeHitWidth && x < width - EdgeHitWidth)
            ? new Cursor(StandardCursorType.SizeAll)
            : new Cursor(StandardCursorType.SizeWestEast);
    }

    private void VisibilityBlock_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: VideoVisibilityBlockViewModel block }) return;
        _vm?.SelectVisibilityRegion(block.Region);
    }

    private void VisibilityBlockDuplicate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null || sender is not MenuItem { Tag: VideoVisibilityBlockViewModel block }) return;
        _vm.DuplicateVisibilityRegion(block.Region);
    }

    private void VisibilityBlockDelete_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null || sender is not MenuItem { Tag: VideoVisibilityBlockViewModel block }) return;
        _vm.DeleteVisibilityRegion(block.Region);
    }

    private void OverlayHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || sender is not Border { DataContext: VideoOverlayLaneViewModel lane }) return;
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsRightButtonPressed)
        {
            ShowLayerContextMenu(lane);
            e.Handled = true;
            return;
        }

        _vm.SelectOverlay(lane.Layer);
        _dragReorderLane = lane;
        _dragReorderHeader = (Control)sender;
        _dragStartY = e.GetCurrentPoint(LanesScroll).Position.Y;
        e.Pointer.Capture(_dragReorderHeader);
        _dragReorderHeader.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        e.Handled = true;
    }

    private void OverlayHeader_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragReorderLane is null || _vm is null) return;
        if (!e.GetCurrentPoint(LanesScroll).Properties.IsLeftButtonPressed) return;
        TryReorderLayerAtY(e.GetCurrentPoint(LanesScroll).Position.Y);
        e.Handled = true;
    }

    private void OverlayHeader_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragReorderHeader is not null)
        {
            e.Pointer.Capture(null);
            _dragReorderHeader.Cursor = Cursor.Default;
        }

        _dragReorderLane = null;
        _dragReorderHeader = null;
    }

    private void TryReorderLayerAtY(double y)
    {
        if (_vm is null || _dragReorderLane is null) return;
        var deltaY = y - _dragStartY;
        if (Math.Abs(deltaY) < 18) return;

        var lanes = _vm.OverlayLanes.ToList();
        var index = lanes.FindIndex(l => l.Layer.Id == _dragReorderLane.Layer.Id);
        if (index < 0) return;
        var target = deltaY < 0 ? index - 1 : index + 1;
        if (target < 0 || target >= lanes.Count) return;

        _vm.ReorderLayer(_dragReorderLane.Layer, target);
        _dragStartY = y;
    }

    private void OverlayLane_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || sender is not Border { DataContext: VideoOverlayLaneViewModel lane }) return;
        if (e.GetCurrentPoint(LanesScroll).Properties.IsRightButtonPressed) return;
        _vm.SelectOverlay(lane.Layer);
        _dragLane = lane;
        _regionCreateStartBeat = BeatAtPointer(e);
        _creatingRegion = true;
        e.Pointer.Capture((Control)sender);
        e.Handled = true;
    }

    private void OverlayLane_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || sender is not Border { DataContext: VideoOverlayLaneViewModel lane }) return;
        _creatingRegion = false;
        _dragLane = null;
        _vm.SelectOverlay(lane.Layer);
        var beat = BeatAtX(e.GetPosition(LanesScroll).X);
        var span = Math.Max(1, _vm.BeatsPerBar);
        _vm.CreateVisibilityRegion(lane.Layer, beat, beat + span);
        ScrollBeatIntoView(beat);
        e.Handled = true;
    }

    private void ScrollBeatIntoView(double beat)
    {
        if (_vm is null) return;
        var px = _vm.Metrics.BeatsToPixels(beat);
        var viewWidth = LanesScroll.Viewport.Width;
        if (viewWidth <= 0) return;
        var offset = LanesScroll.Offset.X;
        if (px >= offset + 8 && px <= offset + viewWidth - 40) return;
        _syncingScroll = true;
        var targetX = Math.Max(0, px - viewWidth * 0.25);
        LanesScroll.Offset = new Vector(targetX, LanesScroll.Offset.Y);
        RulerScroll.Offset = new Vector(targetX, 0);
        _vm.Metrics.HorizontalOffset = targetX;
        _syncingScroll = false;
        UpdatePlayhead();
    }

    private void ShowLayerContextMenu(VideoOverlayLaneViewModel lane)
    {
        if (_vm is null) return;
        _vm.SelectOverlay(lane.Layer);
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem
                {
                    Header = Loc.Get("VideoTimeline_Add_region", "+ Region"),
                    Command = _vm.AddVisibilityRegionCommand
                },
                new MenuItem { Header = "-" },
                new MenuItem
                {
                    Header = Loc.Get("VideoTimeline_Move_layer_up", "Move up"),
                    Command = _vm.MoveLayerUpCommand
                },
                new MenuItem
                {
                    Header = Loc.Get("VideoTimeline_Move_layer_down", "Move down"),
                    Command = _vm.MoveLayerDownCommand
                },
                new MenuItem
                {
                    Header = Loc.Get("VideoTimeline_Delete_layer", "Delete layer"),
                    Command = _vm.RemoveLayerCommand
                }
            }
        };
        menu.Open(this);
    }

    private void OnLanesPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm is null) return;
        if (_dragVisibility is not null && e.GetCurrentPoint(LanesScroll).Properties.IsLeftButtonPressed)
        {
            var beat = BeatAtPointer(e);
            if (!_visibilityDragCaptured)
            {
                _vm.BeginVisibilityRegionEdit();
                _visibilityDragCaptured = true;
            }

            switch (_visibilityDragMode)
            {
                case VisibilityDragMode.TrimEnd:
                    _vm.SetVisibilityRegionSpan(_dragVisibility.Region, _dragOrigStart,
                        Math.Max(_dragOrigStart + MinRegionBeats, beat));
                    break;
                case VisibilityDragMode.TrimStart:
                    _vm.SetVisibilityRegionSpan(_dragVisibility.Region,
                        Math.Max(0, Math.Min(beat, _dragOrigEnd - MinRegionBeats)), _dragOrigEnd);
                    break;
                default:
                    var span = _dragOrigEnd - _dragOrigStart;
                    var start = Math.Max(0, _dragOrigStart + (beat - _dragPressBeat));
                    _vm.SetVisibilityRegionSpan(_dragVisibility.Region, start, start + span);
                    break;
            }

            e.Handled = true;
            return;
        }

        if (_dragReorderLane is not null && e.GetCurrentPoint(LanesScroll).Properties.IsLeftButtonPressed)
        {
            TryReorderLayerAtY(e.GetCurrentPoint(LanesScroll).Position.Y);
            e.Handled = true;
        }
    }

    private void OnLanesPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_vm is not null && _creatingRegion && _dragLane is not null)
        {
            var endBeat = BeatAtPointer(e);
            if (Math.Abs(endBeat - _regionCreateStartBeat) > 0.25)
                _vm.CreateVisibilityRegion(_dragLane.Layer, _regionCreateStartBeat, endBeat);
        }

        if (_visibilityDragCaptured)
            _vm?.EndVisibilityRegionEdit();

        _dragVisibility = null;
        _visibilityDragMode = VisibilityDragMode.None;
        _visibilityDragCaptured = false;
        _dragLane = null;
        if (_dragReorderHeader is not null)
        {
            e.Pointer.Capture(null);
            _dragReorderHeader.Cursor = Cursor.Default;
            _dragReorderHeader = null;
        }

        _dragReorderLane = null;
        _creatingRegion = false;
    }

    private async Task<string?> PickLayerPathAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("VideoResources_Add_layer", "+ Layer"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Media") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.mp4", "*.mov", "*.mkv", "*.avi", "*.webm"] },
                FilePickerFileTypes.All
            ]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickSubtitleSrtPathAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("VideoTrack_Subtitle_srt", "Subtitle SRT"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Subtitles") { Patterns = ["*.srt", "*.vtt"] },
                FilePickerFileTypes.All
            ]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickLutCubePathAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("VideoTrack_Lut_cube", "LUT (.cube)"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("LUT") { Patterns = ["*.cube"] },
                FilePickerFileTypes.All
            ]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickMaskImagePathAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("VideoTrack_Mask_image", "Mask image"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp"] },
                FilePickerFileTypes.All
            ]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
