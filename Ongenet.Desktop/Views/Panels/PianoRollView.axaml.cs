using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Ongenet.Desktop.ViewModels;
using Ongenet.Desktop.ViewModels.PianoRoll;

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

        private enum Gesture { None, Move, Resize, Zoom, Band }

        private Gesture _gesture = Gesture.None;
        private NoteViewModel? _note;
        private double _pressBeat;
        private double _origStart;
        private double _origLength;
        private int _previewPitch = -1;
        private int _keyPreview = -1;

        private Point _bandStart;

        private double _zoomStartPpb;
        private double _zoomAnchorBeat;
        private double _zoomStartY;
        private double _zoomAnchorScreenX;

        private PianoRollViewModel? _vm;

        public PianoRollView()
        {
            InitializeComponent();
            Focusable = true;

            PrGridScroll.AddHandler(ScrollViewer.ScrollChangedEvent, OnGridScrollChanged);
            PianoGrid.PointerPressed += OnGridPressed;
            PianoGrid.PointerMoved += OnGridMoved;
            PianoGrid.PointerReleased += OnGridReleased;
            KeyDown += OnKeyDown;

            DataContextChanged += OnDataContextChanged;
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
            var offset = PrGridScroll.Offset;
            PrRulerScroll.Offset = new Vector(offset.X, 0);
            PrKeysScroll.Offset = new Vector(0, offset.Y);
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

            if (point.Properties.IsMiddleButtonPressed)
            {
                _gesture = Gesture.Zoom;
                _zoomStartPpb = vm.Metrics.PixelsPerBeat;
                _zoomAnchorBeat = vm.Metrics.PixelsToBeats(gridPos.X);
                _zoomStartY = gridPos.Y;
                _zoomAnchorScreenX = e.GetPosition(PrGridScroll).X;
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
                note = vm.AddNote(beat, pitch);
                if (note is null) return;
                _gesture = Gesture.Move;
            }
            else
            {
                vm.SelectOnly(note);
                var localX = gridPos.X - note.Left;
                var zone = Math.Min(6.0, note.Width * 0.3);
                _gesture = localX >= note.Width - zone ? Gesture.Resize : Gesture.Move;
                pitch = note.Model.Note;
            }

            _note = note;
            _pressBeat = beat;
            _origStart = note.Model.StartBeat;
            _origLength = note.Model.LengthBeats;
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
                var x = Math.Max(0, _zoomAnchorBeat * vm.Metrics.PixelsPerBeat - _zoomAnchorScreenX);
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

            if (_note is null) return;
            var beat = vm.Metrics.PixelsToBeats(gridPos.X);

            if (_gesture == Gesture.Move)
            {
                var pitch = vm.Metrics.YToNote(gridPos.Y);
                if (pitch != _previewPitch) StartPreview(vm, pitch);
                vm.MoveNote(_note, _origStart + (beat - _pressBeat), pitch);
            }
            else // Resize
            {
                vm.ResizeNote(_note, _origLength + (beat - _pressBeat));
            }

            e.Handled = true;
        }

        private void OnGridReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_gesture == Gesture.None) return;
            if (_gesture == Gesture.Band) Band.IsVisible = false;
            StopPreview();
            _gesture = Gesture.None;
            _note = null;
            e.Pointer.Capture(null);
            e.Handled = true;
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
    }
}
