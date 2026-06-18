using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ongenet.Desktop.ViewModels;
using Ongenet.Desktop.ViewModels.Timeline;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Centre arrange view. Keeps the ruler and track headers scroll-synchronised with the
    /// lanes (the lanes' scroll viewer is the master), and is the drop target for audio files
    /// dragged from the file browser.
    /// </summary>
    public partial class TimelineView : UserControl
    {
        private const double EdgeZone = 8.0;          // px at each clip end that resizes
        private const double MinClipBeats = 0.25;     // minimum clip length
        private const double ZoomSensitivity = 0.005; // middle-drag zoom factor

        private enum Gesture { None, Move, ResizeLeft, ResizeRight, Zoom, Band }

        private ScrollViewer? _lanesScroll;

        // Active-gesture state.
        private Gesture _gesture = Gesture.None;
        private ClipViewModel? _dragClip;
        private double _pressBeat;
        private double _origStart;
        private double _origLength;
        private double _zoomStartPpb;
        private double _zoomAnchorBeat;
        private double _zoomStartY;
        private double _zoomAnchorScreenX;
        private Point _bandStart;

        public TimelineView()
        {
            InitializeComponent();
            Focusable = true;
            KeyDown += OnKeyDown;
            LanesList.AddHandler(ScrollViewer.ScrollChangedEvent, OnLanesScrollChanged);
            LanesList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            LanesList.AddHandler(DragDrop.DropEvent, OnDrop);
            LanesList.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);

            // Tunnel so we act before the ListBox handles selection/scroll for clip gestures.
            LanesList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            LanesList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            LanesList.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            // Clicking a track header selects that track (and clears any clip selection).
            HeaderScroll.AddHandler(PointerPressedEvent, OnHeaderPressed, RoutingStrategies.Tunnel);

            // Clicking the bar ruler sets the start marker.
            RulerScroll.AddHandler(PointerPressedEvent, OnRulerPressed, RoutingStrategies.Tunnel);

            // Always-on ~30fps tick: advances the playhead overlay and refreshes the track meters.
            _playheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _playheadTimer.Tick += (_, _) => OnTick();
            _playheadTimer.Start();

            // Reposition overlays on any layout change (resize/zoom/scroll), so they stay aligned
            // with the content regardless of window size.
            LayoutUpdated += (_, _) => UpdateOverlays();

            DataContextChanged += OnDataContextChanged;
        }

        private DispatcherTimer _playheadTimer;
        private TimelineViewModel? _vm;

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_vm is not null)
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm.Metrics.PropertyChanged -= OnMetricsPropertyChanged;
            }

            _vm = DataContext as TimelineViewModel;
            if (_vm is not null)
            {
                _vm.PropertyChanged += OnVmPropertyChanged;
                _vm.Metrics.PropertyChanged += OnMetricsPropertyChanged;
                UpdateOverlays();
            }
        }

        private void OnTick()
        {
            _vm?.RefreshRecording();
            UpdateOverlays();
            _vm?.RefreshMeters();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineViewModel.IsPlaying) or nameof(TimelineViewModel.StartBeat))
            {
                UpdateOverlays();
            }
        }

        private void OnMetricsPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateOverlays();

        // Positions the playhead/start-marker lines and the ruler play icon. The horizontal origin
        // is derived from where the scrolled content actually renders (via the visual tree), so it
        // stays correct regardless of scroll offset, list chrome, or window size.
        private void UpdateOverlays()
        {
            if (_vm is null) return;
            _lanesScroll ??= LanesList.FindDescendantOfType<ScrollViewer>();

            var ppb = _vm.Metrics.PixelsPerBeat;
            var height = PlayheadOverlay.Bounds.Height;
            var width = PlayheadOverlay.Bounds.Width;

            // Pixel X (in the overlay's space) where content beat 0 currently renders.
            var lanesOrigin = ContentOriginX(_lanesScroll, PlayheadOverlay);

            var startX = lanesOrigin + _vm.StartBeat * ppb;
            StartMarkerLine.Height = height;
            Canvas.SetLeft(StartMarkerLine, startX);
            StartMarkerLine.IsVisible = startX >= 0 && startX <= width;

            var playX = lanesOrigin + _vm.PlayheadBeats * ppb;
            PlayheadLine.Height = height;
            Canvas.SetLeft(PlayheadLine, playX);
            PlayheadLine.IsVisible = playX >= 0 && playX <= width;

            var rulerOrigin = ContentOriginX(RulerScroll, RulerOverlay);
            var iconX = rulerOrigin + _vm.StartBeat * ppb;
            Canvas.SetLeft(StartMarkerIcon, iconX);
            Canvas.SetTop(StartMarkerIcon, 7);
            StartMarkerIcon.IsVisible = iconX >= -9 && iconX <= RulerOverlay.Bounds.Width;
        }

        // The X (in <paramref name="overlay"/>'s coordinates) at which the scroll viewer's content
        // origin (beat 0) renders — accounts for scroll offset, chrome, and any content alignment.
        private static double ContentOriginX(ScrollViewer? scroll, Visual overlay)
        {
            if (scroll?.Presenter is not Visual presenter) return 0;
            var content = presenter.GetVisualChildren().FirstOrDefault() ?? presenter;
            var transform = content.TransformToVisual(overlay);
            return transform.HasValue ? transform.Value.Transform(new Point(0, 0)).X : 0;
        }

        private void OnRulerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_vm is null) return;
            var x = e.GetPosition(RulerScroll).X + RulerScroll.Offset.X;
            _vm.SetStartBeat(_vm.Metrics.PixelsPerBeat > 0 ? x / _vm.Metrics.PixelsPerBeat : 0);
            e.Handled = true;
        }

        private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not TimelineViewModel vm) return;
            switch ((e.Source as StyledElement)?.DataContext)
            {
                case TrackLaneViewModel lane:
                    vm.SelectLane(lane);
                    break;
                case AutomationLaneViewModel auto:
                    vm.SelectTrack(auto.OwnerTrack);
                    break;
            }
        }

        private void OnLanesScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            _lanesScroll ??= LanesList.FindDescendantOfType<ScrollViewer>();
            if (_lanesScroll is null) return;

            var offset = _lanesScroll.Offset;
            RulerScroll.Offset = new Vector(offset.X, 0);
            HeaderScroll.Offset = new Vector(0, offset.Y);
            UpdateOverlays();
        }

        // --- Drag & drop: insert an audio clip on the hovered lane, or a new audio track ---

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (DataContext is not TimelineViewModel vm) return;
            // Audio can arrive from the in-app file browser (DragFormats.AudioFile) or from the OS
            // file manager as files.
            var isAudio = e.DataTransfer.Contains(DragFormats.AudioFile) || ExternalAudioPaths(e, vm).Count > 0;
            var isInstrument = e.DataTransfer.Contains(DragFormats.Instrument);
            if (!isAudio && !isInstrument)
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            e.DragEffects = DragDropEffects.Copy;
            var (target, _, isNewTrack) = Locate(e, vm);

            // An instrument drop always creates a new track; an audio drop targets a hovered lane.
            if (isInstrument || isNewTrack || target is null)
            {
                vm.SetDropHighlight(null);
                ShowNewTrackGhost(vm, isInstrument ? "New instrument track" : "New audio track");
            }
            else
            {
                vm.SetDropHighlight(target);
                NewTrackGhost.IsVisible = false;
            }

            e.Handled = true;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            NewTrackGhost.IsVisible = false;
            if (DataContext is not TimelineViewModel vm) return;
            vm.ClearDropHighlight();

            var (rowIndex, beat) = LocatePoint(e.GetPosition(LanesList), vm);
            if (e.DataTransfer.TryGetValue(DragFormats.Instrument) is { } instrumentId)
            {
                vm.CreateInstrumentTrack(instrumentId, vm.TrackInsertIndexForRow(rowIndex));
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.AudioFile) is { } path)
            {
                var target = rowIndex >= vm.RowCount ? null : vm.TrackLaneAtRow(rowIndex);
                vm.AddAudioClip(path, target, beat);
            }
            else
            {
                // External OS file drop: one file lands on the hovered lane; multiple files each get
                // their own new audio track so they don't pile up on top of each other.
                var paths = ExternalAudioPaths(e, vm);
                if (paths.Count == 1)
                {
                    var target = rowIndex >= vm.RowCount ? null : vm.TrackLaneAtRow(rowIndex);
                    vm.AddAudioClip(paths[0], target, beat);
                }
                else
                {
                    foreach (var p in paths) vm.AddAudioClip(p, null, beat);
                }
            }

            e.Handled = true;
        }

        private void OnDragLeave(object? sender, DragEventArgs e)
        {
            NewTrackGhost.IsVisible = false;
            (DataContext as TimelineViewModel)?.ClearDropHighlight();
        }

        // Extracts the local paths of any dragged OS files the timeline can ingest as audio.
        private static List<string> ExternalAudioPaths(DragEventArgs e, TimelineViewModel vm)
        {
            var paths = new List<string>();
            var items = e.DataTransfer.TryGetFiles();
            if (items is null) return paths;

            foreach (var item in items)
            {
                var local = item.TryGetLocalPath();
                if (!string.IsNullOrEmpty(local) && vm.CanIngest(local)) paths.Add(local);
            }

            return paths;
        }

        // Maps a drag position to a target track lane (null = new track) + beat.
        private (TrackLaneViewModel? Target, double Beat, bool IsNewTrack) Locate(DragEventArgs e, TimelineViewModel vm)
        {
            var (rowIndex, beat) = LocatePoint(e.GetPosition(LanesList), vm);
            var isNewTrack = rowIndex >= vm.RowCount;
            return (isNewTrack ? null : vm.TrackLaneAtRow(rowIndex), beat, isNewTrack);
        }

        // Maps a point (in LanesList coordinates) to a row index + beat, accounting for scroll/zoom.
        private (int RowIndex, double Beat) LocatePoint(Point pos, TimelineViewModel vm)
        {
            _lanesScroll ??= LanesList.FindDescendantOfType<ScrollViewer>();
            var scrollX = _lanesScroll?.Offset.X ?? 0;
            var scrollY = _lanesScroll?.Offset.Y ?? 0;

            var index = vm.RowIndexAtY(pos.Y + scrollY);
            var beat = vm.Metrics.PixelsPerBeat > 0 ? (pos.X + scrollX) / vm.Metrics.PixelsPerBeat : 0;
            return (index, beat);
        }

        // --- Clip gestures: select / move / resize / cross-track / create / zoom ---

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not TimelineViewModel vm) return;
            var point = e.GetCurrentPoint(LanesList);
            var pos = point.Position;

            // Middle button → start a time-zoom drag.
            if (point.Properties.IsMiddleButtonPressed)
            {
                var (_, anchorBeat) = LocatePoint(pos, vm);
                _gesture = Gesture.Zoom;
                _zoomStartPpb = vm.Metrics.PixelsPerBeat;
                _zoomAnchorBeat = anchorBeat;
                _zoomStartY = pos.Y;
                _zoomAnchorScreenX = pos.X;
                e.Pointer.Capture(LanesList);
                e.Handled = true;
                return;
            }

            if (!point.Properties.IsLeftButtonPressed) return;

            Focus(); // so the Delete key targets the selected clips

            // Select mode: click-drag draws a rubber band over the lanes.
            if (vm.IsSelectMode)
            {
                _gesture = Gesture.Band;
                _bandStart = pos;
                BandSelect(pos, pos, vm);
                ShowBand(pos, pos);
                e.Pointer.Capture(LanesList);
                e.Handled = true;
                return;
            }

            var (rowIndex, beat) = LocatePoint(pos, vm);

            // Automation rows: don't intercept — let the curve control handle the pointer.
            if (vm.IsAutomationRow(rowIndex)) return;

            var hit = ResolveClip(e);

            if (hit is null)
            {
                // Empty lane space: double-click on an instrument lane creates a clip; a single
                // click clears any selected clip and selects the track (so blank-space clicks
                // and track clicks deselect the clip and show the track's settings).
                var trackLane = vm.TrackLaneAtRow(rowIndex);
                if (trackLane is not null)
                {
                    if (e.ClickCount == 2 && trackLane.Model.Kind == Core.Models.Audio.TrackKind.Instrument)
                    {
                        vm.CreateMidiClip(rowIndex, beat);
                    }
                    else
                    {
                        vm.SelectTrackAtRow(rowIndex);
                    }

                    e.Handled = true;
                }

                return;
            }

            // A clip was hit: select it and begin a move/resize gesture. Edge zones are measured
            // against the clip's own visual rectangle so body clicks always move (never resize).
            var (clip, clipVisual) = hit.Value;
            vm.SelectClip(clip);

            var localX = e.GetPosition(clipVisual).X;
            var width = clipVisual.Bounds.Width;
            var zone = System.Math.Min(EdgeZone, width * 0.25);
            _gesture = localX <= zone ? Gesture.ResizeLeft
                : localX >= width - zone ? Gesture.ResizeRight
                : Gesture.Move;

            _dragClip = clip;
            _pressBeat = beat;
            _origStart = clip.Model.StartBeat;
            _origLength = clip.Model.LengthBeats;

            e.Pointer.Capture(LanesList);
            e.Handled = true;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_gesture == Gesture.None || DataContext is not TimelineViewModel vm) return;
            var pos = e.GetPosition(LanesList);

            if (_gesture == Gesture.Zoom)
            {
                var newPpb = _zoomStartPpb * System.Math.Exp(-(pos.Y - _zoomStartY) * ZoomSensitivity);
                vm.Metrics.PixelsPerBeat = newPpb; // clamped in the setter
                _lanesScroll ??= LanesList.FindDescendantOfType<ScrollViewer>();
                if (_lanesScroll is not null)
                {
                    var x = System.Math.Max(0, _zoomAnchorBeat * vm.Metrics.PixelsPerBeat - _zoomAnchorScreenX);
                    _lanesScroll.Offset = new Vector(x, _lanesScroll.Offset.Y);
                }

                e.Handled = true;
                return;
            }

            if (_gesture == Gesture.Band)
            {
                BandSelect(_bandStart, pos, vm);
                ShowBand(_bandStart, pos);
                e.Handled = true;
                return;
            }

            if (_dragClip is null) return;
            var (_, beat) = LocatePoint(pos, vm);
            var delta = beat - _pressBeat;

            switch (_gesture)
            {
                case Gesture.Move:
                    _dragClip.Model.StartBeat = System.Math.Max(0, vm.Metrics.Snap(_origStart + delta));
                    break;
                case Gesture.ResizeRight:
                    var end = vm.Metrics.Snap(_origStart + _origLength + delta);
                    _dragClip.Model.LengthBeats = System.Math.Max(MinClipBeats, end - _origStart);
                    break;
                case Gesture.ResizeLeft:
                    var newStart = System.Math.Max(0, vm.Metrics.Snap(_origStart + delta));
                    var newLen = _origStart + _origLength - newStart;
                    if (newLen < MinClipBeats) { newStart = _origStart + _origLength - MinClipBeats; newLen = MinClipBeats; }
                    _dragClip.Model.StartBeat = newStart;
                    _dragClip.Model.LengthBeats = newLen;
                    break;
            }

            vm.NotifyClipGeometryChanged(_dragClip);
            e.Handled = true;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_gesture == Gesture.None) { return; }
            if (DataContext is TimelineViewModel vm && _gesture == Gesture.Move && _dragClip is not null)
            {
                // Cross-track move: reparent to the instrument lane under the pointer, if different.
                var (targetRow, _) = LocatePoint(e.GetPosition(LanesList), vm);
                var targetLane = vm.TrackLaneAtRow(targetRow);
                var origin = vm.FindLaneOf(_dragClip);
                if (origin is not null && targetLane is not null && !ReferenceEquals(origin, targetLane))
                {
                    vm.TryReparentClip(_dragClip, targetLane);
                }
            }

            if (_gesture == Gesture.Band) Band.IsVisible = false;

            _gesture = Gesture.None;
            _dragClip = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && DataContext is TimelineViewModel vm)
            {
                vm.DeleteSelectedClips();
                e.Handled = true;
            }
        }

        // Selects clips inside the band (band points are in LanesList/viewport coords; add the
        // scroll offset to reach content coordinates that match clip Left/lane positions).
        private void BandSelect(Point a, Point b, TimelineViewModel vm)
        {
            _lanesScroll ??= LanesList.FindDescendantOfType<ScrollViewer>();
            var sx = _lanesScroll?.Offset.X ?? 0;
            var sy = _lanesScroll?.Offset.Y ?? 0;
            vm.SelectClipsInRect(a.X + sx, a.Y + sy, b.X + sx, b.Y + sy);
        }

        private void ShowBand(Point a, Point b)
        {
            Canvas.SetLeft(Band, System.Math.Min(a.X, b.X));
            Canvas.SetTop(Band, System.Math.Min(a.Y, b.Y));
            Band.Width = System.Math.Abs(a.X - b.X);
            Band.Height = System.Math.Abs(a.Y - b.Y);
            Band.IsVisible = true;
        }

        // Finds the clip under the pointer and its outermost clip-scoped visual (the item
        // container, sized to the clip), or null for empty lane space.
        private static (ClipViewModel Clip, Visual Visual)? ResolveClip(PointerEventArgs e)
        {
            var v = e.Source as Visual;
            ClipViewModel? clip = null;
            Visual? visual = null;
            while (v is not null)
            {
                if (v is StyledElement { DataContext: ClipViewModel cvm })
                {
                    clip = cvm;
                    visual = v;
                }
                else if (clip is not null)
                {
                    break; // walked out of the clip's subtree
                }

                v = v.GetVisualParent();
            }

            return clip is not null && visual is not null ? (clip, visual) : null;
        }

        private void ShowNewTrackGhost(TimelineViewModel vm, string label)
        {
            var scrollY = _lanesScroll?.Offset.Y ?? 0;
            Canvas.SetTop(NewTrackGhost, vm.RowsTotalHeight - scrollY);
            NewTrackGhost.Width = LanesList.Bounds.Width;
            NewTrackGhostLabel.Text = label;
            NewTrackGhost.IsVisible = true;
        }
    }
}
