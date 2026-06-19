using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Midi;
using Ongenet.Desktop.Services;
using Ongenet.Desktop.ViewModels;
using Ongenet.Desktop.ViewModels.PianoRoll;
using Ongenet.Desktop.Views.Windows;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Piano-roll editor. Mirrors the timeline's scroll-sync (grid is master; ruler follows X,
    /// key gutter follows Y); handles note add/move/resize/delete, clickable keys, middle-mouse
    /// zoom, and (in Select mode) rubber-band multi-select + Delete.
    /// </summary>
    public partial class PianoRollView : UserControl
    {
        private const double ZoomSensitivity = 0.005;

        private enum Gesture { None, Move, Resize, Zoom, Band, Slice }

        private Gesture _gesture = Gesture.None;
        private NoteViewModel? _note;
        private bool _noteDragCaptured; // one history snapshot per move/resize drag of an existing note
        private bool _multiEdit;        // dragging a multi-note selection in Edit mode
        private double _pressBeat;

        private static IHistoryService? History => App.ServiceProvider?.GetService<IHistoryService>();
        private double _origStart;
        private double _origLength;
        private int _origPitch;

        private MidiGeneratorWindow? _generatorWindow;
        private ArpeggioWindow? _arpWindow;
        private int _previewPitch = -1;
        private int _keyPreview = -1;

        private Point _bandStart;

        private double _zoomStartPpb;
        private double _zoomAnchorBeat;
        private double _zoomStartY;

        private PianoRollViewModel? _vm;

        // Middle C (C4) — the row the editor scrolls to by default.
        private const int MiddleC = 60;

        // Re-entrancy guard for the grid<->gutter/ruler scroll sync so they can't fight each other.
        private bool _syncingScroll;
        private bool _centeredOnMiddleC;
        private bool _centerScheduled;

        public PianoRollView()
        {
            InitializeComponent();
            Focusable = true;

            // The grid is the master scroller; the ruler (X) and key gutter (Y) are pinned to it.
            // Sync is bidirectional so that wheel-scrolling over the gutter/ruler drives the grid too,
            // keeping the keys glued to their rows instead of drifting independently.
            PrGridScroll.AddHandler(ScrollViewer.ScrollChangedEvent, OnGridScrollChanged);
            PrKeysScroll.AddHandler(ScrollViewer.ScrollChangedEvent, OnKeysScrollChanged);
            PrRulerScroll.AddHandler(ScrollViewer.ScrollChangedEvent, OnRulerScrollChanged);
            PianoGrid.PointerPressed += OnGridPressed;
            PianoGrid.PointerMoved += OnGridMoved;
            PianoGrid.PointerReleased += OnGridReleased;
            KeyDown += OnKeyDown;

            // Center on Middle C once, as soon as the grid has a real viewport/extent.
            LayoutUpdated += OnLayoutUpdated;

            DataContextChanged += OnDataContextChanged;
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (_centeredOnMiddleC || _centerScheduled) return;
            if (DataContext is not PianoRollViewModel vm) return;

            var viewport = PrGridScroll.Viewport.Height;
            if (viewport <= 0) return;

            // Wait until the grid content has actually been laid out to its full height. Early layout
            // passes report Extent ≈ Viewport (the TotalHeight binding hasn't applied yet); centering
            // then would clamp to 0 and "center" on the top.
            if (PrGridScroll.Extent.Height + 1.0 < vm.Metrics.TotalHeight) return;

            // Defer to after this layout pass so the offset actually sticks.
            _centerScheduled = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (CenterOnMiddleC(vm))
                {
                    _centeredOnMiddleC = true;
                    LayoutUpdated -= OnLayoutUpdated;
                }
                else
                {
                    _centerScheduled = false; // transient; retry on a later layout pass
                }
            }, DispatcherPriority.Background);
        }

        // Scrolls so Middle C sits roughly in the vertical center of the grid. Returns false if the
        // viewport/extent aren't ready, so the caller can retry.
        private bool CenterOnMiddleC(PianoRollViewModel vm)
        {
            var viewport = PrGridScroll.Viewport.Height;
            var extent = PrGridScroll.Extent.Height;
            if (viewport <= 0 || extent <= 0) return false;

            var targetY = vm.Metrics.NoteToY(MiddleC) + PianoRollMetrics.KeyHeight / 2 - viewport / 2;
            targetY = Math.Clamp(targetY, 0, Math.Max(0, extent - viewport));
            PrGridScroll.Offset = new Vector(PrGridScroll.Offset.X, targetY);
            return true;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_vm is not null) _vm.ClipBound -= OnClipBound;
            _vm = DataContext as PianoRollViewModel;
            if (_vm is not null) _vm.ClipBound += OnClipBound;
        }

        private void OnClipBound()
            => Dispatcher.UIThread.Post(() => _vm?.FitToWidth(PrGridScroll.Bounds.Width));

        private void OnGridScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_syncingScroll) return;
            _syncingScroll = true;
            var offset = PrGridScroll.Offset;
            PrRulerScroll.Offset = new Vector(offset.X, 0);
            PrKeysScroll.Offset = new Vector(0, offset.Y);
            _syncingScroll = false;
        }

        // The key gutter was scrolled directly (e.g. mouse wheel over the keys): drive the master grid
        // so the rows, grid lines and notes follow — the keys stay pinned rather than drifting.
        private void OnKeysScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_syncingScroll) return;
            var y = PrKeysScroll.Offset.Y;
            if (Math.Abs(y - PrGridScroll.Offset.Y) < 0.5) return;
            _syncingScroll = true;
            PrGridScroll.Offset = new Vector(PrGridScroll.Offset.X, y);
            _syncingScroll = false;
        }

        // Likewise, scrolling the ruler horizontally drives the master grid.
        private void OnRulerScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_syncingScroll) return;
            var x = PrRulerScroll.Offset.X;
            if (Math.Abs(x - PrGridScroll.Offset.X) < 0.5) return;
            _syncingScroll = true;
            PrGridScroll.Offset = new Vector(x, PrGridScroll.Offset.Y);
            _syncingScroll = false;
        }

        // --- Clickable keys (preview only, no note added) ---

        private void OnKeyPressed(object? sender, PointerPressedEventArgs e)
        {
            if ((sender as Control)?.DataContext is PianoKeyViewModel key && DataContext is PianoRollViewModel vm)
            {
                _keyPreview = key.MidiNote;
                vm.PreviewOn(key.MidiNote);
                e.Pointer.Capture(sender as IInputElement);
            }
        }

        private void OnKeyReleased(object? sender, PointerReleasedEventArgs e) => ReleaseKeyPreview();
        private void OnKeyExited(object? sender, PointerEventArgs e) => ReleaseKeyPreview();

        private void ReleaseKeyPreview()
        {
            if (_keyPreview >= 0 && DataContext is PianoRollViewModel vm) vm.PreviewOff(_keyPreview);
            _keyPreview = -1;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && DataContext is PianoRollViewModel vm)
            {
                vm.DeleteSelectedNotes();
                e.Handled = true;
            }
        }

        // --- Grid: note editing / band select / zoom ---

        private void OnGridPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not PianoRollViewModel vm) return;
            Focus();
            var gridPos = e.GetPosition(PianoGrid);
            var point = e.GetCurrentPoint(PianoGrid);

            // Middle button → combined zoom/pan drag: vertical movement zooms, horizontal movement
            // scrolls (the grabbed beat stays pinned under the cursor).
            if (point.Properties.IsMiddleButtonPressed)
            {
                _gesture = Gesture.Zoom;
                _zoomStartPpb = vm.Metrics.PixelsPerBeat;
                _zoomAnchorBeat = vm.Metrics.PixelsToBeats(gridPos.X);
                _zoomStartY = gridPos.Y;
                e.Pointer.Capture(PianoGrid);
                e.Handled = true;
                return;
            }

            var note = ResolveNote(e);

            if (point.Properties.IsRightButtonPressed)
            {
                if (note is not null) vm.DeleteNote(note);
                e.Handled = true;
                return;
            }

            if (!point.Properties.IsLeftButtonPressed) return;

            // Slice mode: click-drag draws a cut line; notes it crosses are split on release.
            if (vm.IsSliceMode)
            {
                _gesture = Gesture.Slice;
                SliceLine.StartPoint = gridPos;
                SliceLine.EndPoint = gridPos;
                SliceLine.IsVisible = true;
                e.Pointer.Capture(PianoGrid);
                e.Handled = true;
                return;
            }

            // Select mode: click-drag draws a rubber band.
            if (vm.IsSelectMode)
            {
                _gesture = Gesture.Band;
                _bandStart = gridPos;
                vm.SelectNotesInRect(gridPos.X, gridPos.Y, gridPos.X, gridPos.Y);
                ShowBand(gridPos, gridPos);
                e.Pointer.Capture(PianoGrid);
                e.Handled = true;
                return;
            }

            var beat = vm.Metrics.PixelsToBeats(gridPos.X);
            var pitch = vm.Metrics.YToNote(gridPos.Y);

            if (note is null)
            {
                note = vm.AddNote(beat, pitch); // captures "Add note" itself
                if (note is null) return;
                _gesture = Gesture.Move;
                _noteDragCaptured = true; // the add already snapshotted; don't recapture as it's dragged
                _multiEdit = false;
            }
            else
            {
                // Clicking an unselected note selects only it; clicking one that's already part of a
                // multi-selection keeps the selection so the whole group can be dragged/resized.
                if (!note.IsSelected) vm.SelectOnly(note);
                vm.RememberLength(note.Model.LengthBeats); // remember the last-clicked note's length
                var localX = gridPos.X - note.Left;
                var zone = Math.Min(6.0, note.Width * 0.3);
                _gesture = localX >= note.Width - zone ? Gesture.Resize : Gesture.Move;
                _noteDragCaptured = false; // snapshot on the first real move/resize of this existing note
                pitch = note.Model.Note;
                _multiEdit = vm.SelectedCount > 1;
                if (_multiEdit) vm.CaptureSelectionBaseline();
            }

            _note = note;
            _pressBeat = beat;
            _origStart = note.Model.StartBeat;
            _origLength = note.Model.LengthBeats;
            _origPitch = note.Model.Note;
            StartPreview(vm, pitch);

            e.Pointer.Capture(PianoGrid);
            e.Handled = true;
        }

        private void OnGridMoved(object? sender, PointerEventArgs e)
        {
            if (_gesture == Gesture.None || DataContext is not PianoRollViewModel vm) return;
            var gridPos = e.GetPosition(PianoGrid);

            if (_gesture == Gesture.Zoom)
            {
                vm.Metrics.PixelsPerBeat = _zoomStartPpb * Math.Exp(-(gridPos.Y - _zoomStartY) * ZoomSensitivity);
                // Pin the anchor beat under the current pointer X: vertical drag zooms, horizontal drag pans.
                var x = Math.Max(0, _zoomAnchorBeat * vm.Metrics.PixelsPerBeat - e.GetPosition(PrGridScroll).X);
                PrGridScroll.Offset = new Vector(x, PrGridScroll.Offset.Y);
                e.Handled = true;
                return;
            }

            if (_gesture == Gesture.Band)
            {
                vm.SelectNotesInRect(_bandStart.X, _bandStart.Y, gridPos.X, gridPos.Y);
                ShowBand(_bandStart, gridPos);
                e.Handled = true;
                return;
            }

            if (_gesture == Gesture.Slice)
            {
                SliceLine.EndPoint = gridPos;
                e.Handled = true;
                return;
            }

            if (_note is null) return;
            if (!_noteDragCaptured)
            {
                History?.Capture(_gesture == Gesture.Move ? "Move note" : "Resize note");
                _noteDragCaptured = true;
            }

            var beat = vm.Metrics.PixelsToBeats(gridPos.X);

            if (_gesture == Gesture.Move)
            {
                var pitch = vm.Metrics.YToNote(gridPos.Y);
                if (pitch != _previewPitch) StartPreview(vm, pitch);
                if (_multiEdit)
                    vm.MoveSelectionBy(beat - _pressBeat, pitch - _origPitch);
                else
                    vm.MoveNote(_note, _origStart + (beat - _pressBeat), pitch);
            }
            else // Resize
            {
                if (_multiEdit)
                {
                    // Scale every selected note's length proportionally to the dragged note's change.
                    var factor = _origLength > 0 ? (_origLength + (beat - _pressBeat)) / _origLength : 1.0;
                    vm.ScaleSelectionLength(factor);
                }
                else
                {
                    vm.ResizeNote(_note, _origLength + (beat - _pressBeat));
                }
            }

            e.Handled = true;
        }

        private void OnGridReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_gesture == Gesture.None) return;
            if (_gesture == Gesture.Band) Band.IsVisible = false;
            if (_gesture == Gesture.Slice)
            {
                if (DataContext is PianoRollViewModel vm) DoSlice(vm);
                SliceLine.IsVisible = false;
            }

            StopPreview();
            _gesture = Gesture.None;
            _note = null;
            _multiEdit = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        // --- Slice tool ---

        // On release of a slice drag, splits every note the cut line crosses (or, for a click with no
        // drag, the single note under the cursor). Captures one history entry for the whole gesture.
        private void DoSlice(PianoRollViewModel vm)
        {
            var a = SliceLine.StartPoint;
            var b = SliceLine.EndPoint;
            var cuts = new List<(NoteViewModel note, double beat)>();
            var dragLen = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

            if (dragLen < 3.0)
            {
                // A plain click: slice the note under the pointer at the grid-snapped beat.
                var clickBeat = vm.Metrics.Snap(vm.Metrics.PixelsToBeats(a.X));
                var hit = vm.Notes.FirstOrDefault(n => HitNote(n, a));
                if (hit is not null) cuts.Add((hit, clickBeat));
            }
            else
            {
                foreach (var n in vm.Notes.ToList())
                    if (TryLineCutBeat(a, b, n, vm, out var cutBeat))
                        cuts.Add((n, cutBeat));
            }

            if (cuts.Count == 0) return;
            History?.Capture("Slice");
            foreach (var (note, beat) in cuts) vm.SliceNote(note, beat);
        }

        private static bool HitNote(NoteViewModel n, Point p)
            => p.X >= n.Left && p.X <= n.Left + n.Width && p.Y >= n.Top && p.Y <= n.Top + n.Height;

        // True if the segment a→b crosses the note's vertical centre within the note's horizontal extent;
        // outputs the clip-local beat at the crossing. Horizontal drags (no unique crossing) are ignored.
        private static bool TryLineCutBeat(Point a, Point b, NoteViewModel n, PianoRollViewModel vm, out double cutBeat)
        {
            cutBeat = 0;
            var yc = n.Top + n.Height / 2;
            if (yc < Math.Min(a.Y, b.Y) || yc > Math.Max(a.Y, b.Y)) return false;

            var dy = b.Y - a.Y;
            if (Math.Abs(dy) < 0.0001) return false;

            var t = (yc - a.Y) / dy;
            var x = a.X + t * (b.X - a.X);
            if (x <= n.Left || x >= n.Left + n.Width) return false;

            cutBeat = vm.Metrics.PixelsToBeats(x);
            return true;
        }

        private void ShowBand(Point a, Point b)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            Band.Margin = new Thickness(x, y, 0, 0);
            Band.Width = Math.Abs(a.X - b.X);
            Band.Height = Math.Abs(a.Y - b.Y);
            Band.IsVisible = true;
        }

        private void StartPreview(PianoRollViewModel vm, int pitch)
        {
            if (_previewPitch == pitch) return;
            if (_previewPitch >= 0) vm.PreviewOff(_previewPitch);
            vm.PreviewOn(pitch);
            _previewPitch = pitch;
        }

        private void StopPreview()
        {
            if (_previewPitch >= 0 && DataContext is PianoRollViewModel vm) vm.PreviewOff(_previewPitch);
            _previewPitch = -1;
        }

        private static NoteViewModel? ResolveNote(PointerEventArgs e)
            => (e.Source as StyledElement)?.DataContext as NoteViewModel;

        // --- Control bar ---

        private static readonly FilePickerFileType MidiFileType =
            new("MIDI files") { Patterns = new[] { "*.mid", "*.midi" } };

        private async void Import_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not PianoRollViewModel vm || !vm.HasClip) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import MIDI",
                AllowMultiple = false,
                FileTypeFilter = new[] { MidiFileType }
            });
            if (files.Count == 0) return;

            var owner = top as Window;
            MidiClipData data;
            try
            {
                await using var stream = await files[0].OpenReadAsync();
                data = StandardMidiFile.Read(stream);
            }
            catch (Exception ex)
            {
                if (owner is not null) await MessageDialog.Notify(owner, "Couldn't import MIDI", ex.Message);
                return;
            }

            if (data.Notes.Count == 0)
            {
                if (owner is not null) await MessageDialog.Notify(owner, "No notes", "That MIDI file contains no notes.");
                return;
            }

            if (vm.CurrentNotes.Count > 0 && owner is not null)
            {
                var ok = await MessageDialog.Confirm(owner, "Replace notes?",
                    $"Import {data.Notes.Count} note(s) and replace the {vm.CurrentNotes.Count} already in this clip?",
                    "Replace", "Cancel");
                if (!ok) return;
            }

            vm.ReplaceNotes(data.Notes, data.LengthBeats, "Import MIDI");
        }

        private async void Export_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not PianoRollViewModel vm || !vm.HasClip) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var owner = top as Window;

            if (vm.CurrentNotes.Count == 0)
            {
                if (owner is not null) await MessageDialog.Notify(owner, "Nothing to export", "This clip has no notes.");
                return;
            }

            var name = string.IsNullOrWhiteSpace(vm.ClipName) ? "clip" : vm.ClipName;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export MIDI",
                SuggestedFileName = $"{name}.mid",
                DefaultExtension = "mid",
                FileTypeChoices = new[] { MidiFileType }
            });
            if (file is null) return;

            try
            {
                await using var stream = await file.OpenWriteAsync();
                StandardMidiFile.Write(stream, vm.CurrentNotes, vm.CurrentLengthBeats,
                    vm.ProjectTempo, vm.ProjectTimeSignature);
            }
            catch (Exception ex)
            {
                if (owner is not null) await MessageDialog.Notify(owner, "Couldn't export MIDI", ex.Message);
            }
        }

        private void Generator_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not PianoRollViewModel vm) return;
            var owner = TopLevel.GetTopLevel(this) as Window;

            if (_generatorWindow is null)
            {
                _generatorWindow = new MidiGeneratorWindow();
                _generatorWindow.SetViewModel(new MidiGeneratorViewModel(vm));
                _generatorWindow.Closed += (_, _) => _generatorWindow = null;
                if (owner is not null) _generatorWindow.Show(owner); else _generatorWindow.Show();
            }
            else
            {
                _generatorWindow.Activate();
            }
        }

        private void Arp_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not PianoRollViewModel vm) return;
            var owner = TopLevel.GetTopLevel(this) as Window;

            if (_arpWindow is null)
            {
                _arpWindow = new ArpeggioWindow();
                _arpWindow.SetViewModel(new ArpeggiatorViewModel(vm));
                _arpWindow.Closed += (_, _) => _arpWindow = null;
                if (owner is not null) _arpWindow.Show(owner); else _arpWindow.Show();
            }
            else
            {
                _arpWindow.Activate();
            }
        }
    }
}
