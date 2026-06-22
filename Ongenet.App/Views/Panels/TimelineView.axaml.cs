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
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.App.ViewModels.Timeline;

namespace Ongenet.App.Views.Panels
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
        private const double BoundaryBand = 7.0;      // px around a track line that inserts a new track

        private enum Gesture { None, Move, ResizeLeft, ResizeRight, Zoom, Band }

        private ScrollViewer? _lanesScroll;

        // Track-header drag-to-reorder state.
        private const double DragThreshold = 5.0;
        private TrackLaneViewModel? _dragLane;
        private Point _dragStartPoint;
        private bool _dragging;

        // Active-gesture state.
        private Gesture _gesture = Gesture.None;
        private ClipViewModel? _dragClip;
        private bool _clipDragCaptured; // history snapshot taken once per move/resize drag (on first actual move)
        private double _pressBeat;
        private double _origStart;
        private double _origLength;
        private double _zoomStartPpb;
        private double _zoomAnchorBeat;
        private double _zoomStartY;
        private Point _bandStart;
        private bool _bandMoved;     // whether the rubber band actually dragged (vs. a plain empty-space click)
        private int _bandPressRow;   // row under the press, used to select the track on a no-drag click

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

            // Clicking a track header selects that track (and clears any clip selection); dragging it
            // reorders / re-groups tracks.
            HeaderScroll.AddHandler(PointerPressedEvent, OnHeaderPressed, RoutingStrategies.Tunnel);
            HeaderScroll.AddHandler(PointerMovedEvent, OnHeaderMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            HeaderScroll.AddHandler(PointerReleasedEvent, OnHeaderReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            // Clicking the bar ruler sets the start marker.
            RulerScroll.AddHandler(PointerPressedEvent, OnRulerPressed, RoutingStrategies.Tunnel);

            // Advances the playhead overlay and refreshes the track meters. While playing this runs off
            // the compositor's render frame (vsync-aligned, smooth ~display rate); while stopped it falls
            // back to a cheap timer for live-input meter polling / scrub. A plain DispatcherTimer can't
            // hold a clean 60Hz (it jitters under dispatcher load); RequestAnimationFrame can.
            _ticker = new Services.FrameTicker(this, OnTick);

            // NB: we deliberately do NOT reposition overlays from LayoutUpdated. That event fires
            // after every layout pass in the whole window, and UpdateOverlays writes layout-affecting
            // properties (Canvas.Left/Height/IsVisible) — so doing it from LayoutUpdated re-dirties
            // layout and feeds itself a fresh pass each frame while the playhead is moving, pinning the
            // layout manager and dropping the UI to ~10fps during playback. Resize alignment is handled
            // by OnPropertyChanged(BoundsProperty) below; scroll/zoom/playhead are handled by the
            // scroll/metrics/vm handlers and the 30fps tick.

            // Move the overlay lines/markers via RenderTransform (a pure render-thread op) instead of
            // Canvas.Left. Canvas.Left changes invalidate *layout* (arrange), and because the playhead
            // is repositioned every frame while the transport runs, that was forcing a full layout pass
            // ~30x/sec (measured at ~85ms each → ~13fps). A render transform costs zero layout.
            PlayheadLine.RenderTransform = _playheadXform;
            StartMarkerLine.RenderTransform = _startMarkerXform;
            StartMarkerIcon.RenderTransform = _startIconXform;
            LoopRegion.RenderTransform = _loopXform;
            LoopRegionRuler.RenderTransform = _loopRulerXform;

            DataContextChanged += OnDataContextChanged;
        }

        private readonly Services.FrameTicker _ticker;
        private TimelineViewModel? _vm;

        // Persistent translate transforms for the overlay elements (mutated each frame; never reallocated).
        private readonly Avalonia.Media.TranslateTransform _playheadXform = new();
        private readonly Avalonia.Media.TranslateTransform _startMarkerXform = new();
        private readonly Avalonia.Media.TranslateTransform _startIconXform = new();
        private readonly Avalonia.Media.TranslateTransform _loopXform = new();
        private readonly Avalonia.Media.TranslateTransform _loopRulerXform = new();

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

            _ticker.SetFast(_vm?.IsPlaying ?? false);
        }

        // The single per-frame UI driver. Runs off the timeline's render-frame loop (RAF while playing,
        // a slow timer while idle). Besides the playhead/meters, it pumps the shared PlaybackClock so the
        // transport meter/time + inspectors refresh from THIS one loop (PlaybackClock self-throttles to
        // ~30Hz) — having them on their own competing timers wrecked vsync pacing (dropped to 30fps).
        private IPlaybackClock? _clock;

        private void OnTick()
        {
            _vm?.RefreshRecording();
            UpdateOverlays();
            _vm?.RefreshMeters();
            (_clock ??= App.ServiceProvider?.GetService<IPlaybackClock>())?.Pump();
        }

        // Interpolated playhead position. The engine only reports the playhead once per audio block
        // (~24-30Hz here), so reading it directly makes the scene change — and thus the compositor
        // present — only at that rate. Playback advances at exactly tempo (beats/sec) in real time, so
        // between reported samples we extrapolate from the last sample using wall-clock. The playhead
        // then moves every render frame → smooth 60fps independent of the audio buffer size.
        private double _phAnchorBeat = double.NaN;
        private long _phAnchorMs;

        private double SmoothPlayheadBeats()
        {
            if (_vm is null) return 0;
            var raw = _vm.PlayheadBeats;
            var now = Environment.TickCount64;

            // Re-anchor when stopped or when a fresh audio sample arrives (raw advances at exactly the
            // tempo, so the new sample lands ~where we'd extrapolated — the reset is seamless).
            if (!_vm.IsPlaying || raw != _phAnchorBeat)
            {
                _phAnchorBeat = raw;
                _phAnchorMs = now;
                return raw;
            }

            // Cap the extrapolation window so a stalled/paused engine can't run the playhead away.
            var elapsed = Math.Min((now - _phAnchorMs) / 1000.0, 0.10);
            return _phAnchorBeat + elapsed * _vm.BeatsPerSecond;
        }

        // Realign the overlays when the control is resized (the only layout change the scroll/metrics/vm
        // handlers and the 30fps tick don't already cover). Reacting to BoundsProperty specifically —
        // rather than the global LayoutUpdated event — keeps this from re-running on unrelated layout
        // passes elsewhere in the window.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BoundsProperty) UpdateOverlays();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineViewModel.IsPlaying) or nameof(TimelineViewModel.StartBeat))
            {
                if (e.PropertyName == nameof(TimelineViewModel.IsPlaying)) _ticker.SetFast(_vm?.IsPlaying ?? false);
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
            _startMarkerXform.X = startX;
            StartMarkerLine.IsVisible = startX >= 0 && startX <= width;

            var playX = lanesOrigin + SmoothPlayheadBeats() * ppb;
            PlayheadLine.Height = height;
            _playheadXform.X = playX;
            PlayheadLine.IsVisible = playX >= 0 && playX <= width;

            var rulerOrigin = ContentOriginX(RulerScroll, RulerOverlay);
            var iconX = rulerOrigin + _vm.StartBeat * ppb;
            _startIconXform.X = iconX;
            Canvas.SetTop(StartMarkerIcon, 7);
            StartMarkerIcon.IsVisible = iconX >= -9 && iconX <= RulerOverlay.Bounds.Width;

            // Loop region: a faint band over the lanes plus a stronger band on the ruler.
            if (_vm.IsLoopActive)
            {
                var loopX = lanesOrigin + _vm.LoopStart * ppb;
                var loopW = System.Math.Max(0, (_vm.LoopEnd - _vm.LoopStart) * ppb);
                _loopXform.X = loopX;
                LoopRegion.Width = loopW;
                LoopRegion.Height = height;
                LoopRegion.IsVisible = true;

                _loopRulerXform.X = rulerOrigin + _vm.LoopStart * ppb;
                LoopRegionRuler.Width = loopW;
                LoopRegionRuler.IsVisible = true;
            }
            else
            {
                LoopRegion.IsVisible = false;
                LoopRegionRuler.IsVisible = false;
            }
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

            var rightClick = e.GetCurrentPoint(null).Properties.IsRightButtonPressed;

            // Walk up from the click target to the header row's lane view model, so clicking anywhere on
            // the header (name, meter, padding) selects the track — not just the small name text.
            var v = e.Source as Visual;
            while (v is not null)
            {
                switch ((v as StyledElement)?.DataContext)
                {
                    case TrackLaneViewModel lane:
                        // Right-click keeps an existing multi-selection (so "Group tracks" sees all of them);
                        // Ctrl+click toggles membership; a plain click selects just this lane.
                        if (rightClick) vm.EnsureContextSelection(lane);
                        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) vm.ToggleLaneSelection(lane);
                        else vm.SelectLane(lane);

                        // Arm a potential reorder drag (activates once the pointer moves past a threshold).
                        // The master is pinned, so it can't be dragged.
                        if (!rightClick && !lane.IsMaster)
                        {
                            _dragLane = lane;
                            _dragStartPoint = e.GetPosition(HeaderScroll);
                            _dragging = false;
                        }

                        return;
                    case AutomationLaneViewModel auto:
                        if (!rightClick) vm.SelectTrack(auto.OwnerTrack);
                        return;
                }

                v = v.GetVisualParent();
            }
        }

        private void OnHeaderMoved(object? sender, PointerEventArgs e)
        {
            if (_dragLane is null || DataContext is not TimelineViewModel vm) return;
            if (!e.GetCurrentPoint(HeaderScroll).Properties.IsLeftButtonPressed) { CancelDrag(); return; }

            var pos = e.GetPosition(HeaderScroll);
            if (!_dragging)
            {
                if (System.Math.Abs(pos.Y - _dragStartPoint.Y) < DragThreshold &&
                    System.Math.Abs(pos.X - _dragStartPoint.X) < DragThreshold) return;
                _dragging = true;
                e.Pointer.Capture(HeaderScroll);
            }

            var plan = vm.ComputeDrop(pos.Y + HeaderScroll.Offset.Y, _dragLane.Model);
            ShowDragIndicator(plan);
            e.Handled = true;
        }

        private void OnHeaderReleased(object? sender, PointerReleasedEventArgs e)
        {
            // A plain click (no drag started): don't touch capture/Handled so header buttons still work.
            if (!_dragging) { _dragLane = null; return; }

            if (_dragLane is not null && DataContext is TimelineViewModel vm)
            {
                var pos = e.GetPosition(HeaderScroll);
                var plan = vm.ComputeDrop(pos.Y + HeaderScroll.Offset.Y, _dragLane.Model);
                if (plan.Valid) vm.MoveTrack(_dragLane.Model, plan);
            }

            e.Handled = true;
            CancelDrag();
            e.Pointer.Capture(null);
        }

        private void ShowDragIndicator(TimelineViewModel.DragDropPlan plan)
        {
            if (!plan.Valid)
            {
                DragInsertLine.IsVisible = false;
                return;
            }

            var y = plan.IndicatorY - HeaderScroll.Offset.Y;
            Canvas.SetTop(DragInsertLine, y - 1.5);
            Canvas.SetLeft(DragInsertLine, plan.IndicatorX);
            DragInsertLine.Width = System.Math.Max(0, DragOverlay.Bounds.Width - plan.IndicatorX);
            DragInsertLine.IsVisible = true;
        }

        private void CancelDrag()
        {
            _dragLane = null;
            _dragging = false;
            DragInsertLine.IsVisible = false;
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
            // A soundfont or instrument preset spawns an instrument track just like an instrument does.
            var isInstrument = e.DataTransfer.Contains(DragFormats.Instrument)
                               || e.DataTransfer.Contains(DragFormats.SoundFont)
                               || e.DataTransfer.Contains(DragFormats.Preset);
            if (!isAudio && !isInstrument)
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            e.DragEffects = DragDropEffects.Copy;

            // An audio file or instrument dragged near the line between two tracks inserts a NEW track
            // there, shown with the same insertion line used for track reordering.
            {
                var (insertIndex, indicatorY) = LocateBoundary(e, vm);
                if (insertIndex >= 0)
                {
                    vm.SetDropHighlight(null);
                    NewTrackGhost.IsVisible = false;
                    ShowInsertLine(indicatorY);
                    e.Handled = true;
                    return;
                }
            }

            DragInsertLine.IsVisible = false;
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
            DragInsertLine.IsVisible = false;
            if (DataContext is not TimelineViewModel vm) return;
            vm.ClearDropHighlight();

            var (rowIndex, beat) = LocatePoint(e.GetPosition(LanesList), vm);
            var (insertIndex, _) = LocateBoundary(e, vm);
            if (e.DataTransfer.TryGetValue(DragFormats.Instrument) is { } instrumentId)
            {
                var laneIndex = insertIndex >= 0 ? insertIndex : vm.TrackInsertIndexForRow(rowIndex);
                vm.CreateInstrumentTrack(instrumentId, laneIndex);
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.SoundFont) is { } soundFontPath)
            {
                var laneIndex = insertIndex >= 0 ? insertIndex : vm.TrackInsertIndexForRow(rowIndex);
                vm.CreateSoundFontTrack(soundFontPath, laneIndex);
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.Preset) is { } presetPath)
            {
                var laneIndex = insertIndex >= 0 ? insertIndex : vm.TrackInsertIndexForRow(rowIndex);
                vm.CreateInstrumentPresetTrack(presetPath, laneIndex);
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.AudioFile) is { } path)
            {
                if (insertIndex >= 0) vm.AddAudioClip(path, null, beat, insertIndex);
                else vm.AddAudioClip(path, rowIndex >= vm.RowCount ? null : vm.TrackLaneAtRow(rowIndex), beat);
            }
            else
            {
                // External OS file drop: one file lands on the hovered lane (or a new track at a boundary);
                // multiple files each get their own new audio track so they don't pile up on top of each other.
                var paths = ExternalAudioPaths(e, vm);
                if (paths.Count == 1)
                {
                    if (insertIndex >= 0) vm.AddAudioClip(paths[0], null, beat, insertIndex);
                    else vm.AddAudioClip(paths[0], rowIndex >= vm.RowCount ? null : vm.TrackLaneAtRow(rowIndex), beat);
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
            DragInsertLine.IsVisible = false;
            (DataContext as TimelineViewModel)?.ClearDropHighlight();
        }

        // Content-Y of the drag mapped to an inter-track boundary (insert index + indicator Y), or (-1, 0).
        private (int InsertIndex, double IndicatorY) LocateBoundary(DragEventArgs e, TimelineViewModel vm)
        {
            _lanesScroll ??= LanesList.FindDescendantOfType<ScrollViewer>();
            var scrollY = _lanesScroll?.Offset.Y ?? 0;
            return vm.HitTrackBoundary(e.GetPosition(LanesList).Y + scrollY, BoundaryBand);
        }

        // Shows the horizontal insertion line (reused from track reordering) at a content-Y boundary.
        private void ShowInsertLine(double contentY)
        {
            _lanesScroll ??= LanesList.FindDescendantOfType<ScrollViewer>();
            var scrollY = _lanesScroll?.Offset.Y ?? 0;
            Canvas.SetTop(DragInsertLine, contentY - scrollY - 1.5);
            Canvas.SetLeft(DragInsertLine, 0);
            DragInsertLine.Width = DragOverlay.Bounds.Width;
            DragInsertLine.IsVisible = true;
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

            // Middle button → start a combined zoom/pan drag: vertical movement zooms, horizontal
            // movement scrolls (the grabbed beat is pinned under the cursor, so the timeline pans
            // as the pointer moves sideways).
            if (point.Properties.IsMiddleButtonPressed)
            {
                var (_, anchorBeat) = LocatePoint(pos, vm);
                _gesture = Gesture.Zoom;
                _zoomStartPpb = vm.Metrics.PixelsPerBeat;
                _zoomAnchorBeat = anchorBeat;
                _zoomStartY = pos.Y;
                e.Pointer.Capture(LanesList);
                e.Handled = true;
                return;
            }

            if (!point.Properties.IsLeftButtonPressed) return;

            Focus(); // so the Delete key targets the selected clips

            // Slice is armed by the Slice tool OR by holding CTRL: click a clip to cut it at the snapped beat.
            if (vm.IsSliceMode || e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var sliceHit = ResolveClip(e);
                if (sliceHit is not null)
                {
                    var (_, sliceBeat) = LocatePoint(pos, vm);
                    vm.SliceClip(sliceHit.Value.Clip, vm.Metrics.Snap(sliceBeat));
                    e.Handled = true;
                    return;
                }

                // CTRL held over empty space: fall through to normal empty-space handling (rubber band).
                if (vm.IsSliceMode) return;
            }

            var (rowIndex, beat) = LocatePoint(pos, vm);

            // Automation rows: don't intercept — let the curve control handle the pointer.
            if (vm.IsAutomationRow(rowIndex)) return;

            var hit = ResolveClip(e);

            if (hit is null)
            {
                // Empty lane space: double-click on an instrument lane creates a clip. Otherwise arm a
                // rubber-band drag — if the pointer doesn't move it's treated as a plain click that selects
                // the track (deselecting any clip and showing the track's settings).
                var trackLane = vm.TrackLaneAtRow(rowIndex);
                if (e.ClickCount == 2 && trackLane is { Model.Kind: Core.Models.Audio.TrackKind.Instrument })
                {
                    vm.CreateMidiClip(rowIndex, beat);
                    e.Handled = true;
                    return;
                }

                _gesture = Gesture.Band;
                _bandStart = pos;
                _bandMoved = false;
                _bandPressRow = rowIndex;
                BandSelect(pos, pos, vm); // clears any existing clip selection
                ShowBand(pos, pos);
                e.Pointer.Capture(LanesList);
                e.Handled = true;
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
            _clipDragCaptured = false; // snapshot on the first real move, not on a plain click-select
            _pressBeat = beat;
            _origStart = clip.Model.StartBeat;
            _origLength = clip.Model.LengthBeats;

            e.Pointer.Capture(LanesList);
            e.Handled = true;
        }

        // History service resolved on demand (controls aren't constructed through DI).
        private static IHistoryService? History => App.ServiceProvider?.GetService<IHistoryService>();

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
                    // Keep the anchor beat under the current pointer X: vertical drag zooms, horizontal
                    // drag pans.
                    var x = System.Math.Max(0, _zoomAnchorBeat * vm.Metrics.PixelsPerBeat - pos.X);
                    _lanesScroll.Offset = new Vector(x, _lanesScroll.Offset.Y);
                }

                e.Handled = true;
                return;
            }

            if (_gesture == Gesture.Band)
            {
                if (System.Math.Abs(pos.X - _bandStart.X) > 3 || System.Math.Abs(pos.Y - _bandStart.Y) > 3)
                    _bandMoved = true;
                BandSelect(_bandStart, pos, vm);
                ShowBand(_bandStart, pos);
                e.Handled = true;
                return;
            }

            if (_dragClip is null) return;
            if (!_clipDragCaptured)
            {
                History?.Capture(_gesture == Gesture.Move ? "Move clip" : "Resize clip");
                _clipDragCaptured = true;
            }

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
            if (_gesture == Gesture.Move) UpdateClipDragGhost(vm, pos);
            e.Handled = true;
        }

        // Shows a translucent preview on the compatible track under the pointer while moving a clip, so the
        // destination lane is visible before the drop. Hidden when over the clip's own lane or a wrong kind.
        private void UpdateClipDragGhost(TimelineViewModel vm, Point pos)
        {
            if (_dragClip is null) { ClipDragGhost.IsVisible = false; return; }

            var (targetRow, _) = LocatePoint(pos, vm);
            var targetLane = vm.TrackLaneAtRow(targetRow);
            var origin = vm.FindLaneOf(_dragClip);
            var kind = targetLane?.Model.Kind;
            var compatible = _dragClip.Model.IsAudio
                ? kind == Core.Models.Audio.TrackKind.Audio
                : kind == Core.Models.Audio.TrackKind.Instrument;

            if (targetLane is null || origin is null || ReferenceEquals(origin, targetLane) || !compatible)
            {
                ClipDragGhost.IsVisible = false;
                return;
            }

            _lanesScroll ??= LanesList.FindDescendantOfType<ScrollViewer>();
            var scrollX = _lanesScroll?.Offset.X ?? 0;
            var scrollY = _lanesScroll?.Offset.Y ?? 0;

            var rowIndex = vm.Lanes.IndexOf(targetLane);
            Canvas.SetTop(ClipDragGhost, vm.RowTop(rowIndex) + 6 - scrollY); // clips sit 6px from the lane top
            Canvas.SetLeft(ClipDragGhost, _dragClip.Left - scrollX);
            ClipDragGhost.Width = System.Math.Max(2, _dragClip.Width);
            ClipDragGhost.IsVisible = true;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            ClipDragGhost.IsVisible = false;
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

            if (_gesture == Gesture.Band)
            {
                Band.IsVisible = false;
                // A press with no drag is a plain click on empty space: select that row's track.
                if (!_bandMoved && DataContext is TimelineViewModel bvm) bvm.SelectTrackAtRow(_bandPressRow);
            }

            _gesture = Gesture.None;
            _dragClip = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not TimelineViewModel vm) return;

            if (e.Key == Key.Delete)
            {
                vm.DeleteSelectedClips();
                e.Handled = true;
            }
            else if (e.Key == Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                vm.DuplicateSelectedClip();
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
