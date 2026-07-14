using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Services;
using Ongenet.App.Theming;
using Ongenet.App.ViewModels;
using Ongenet.App.ViewModels.PianoRoll;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// Per-note velocity lane for the piano roll. Draws a thin vertical bar (with thumb) for each
    /// note, aligned to the note start, and supports single-bar drag plus horizontal "paint"
    /// gestures that set velocity from the pointer's Y across many notes.
    /// </summary>
    public sealed class VelocityLaneControl : ThemedControl, ICustomHitTest
    {
        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<VelocityLaneControl, int>(nameof(Revision));

        private const double BarWidth = 3.0;
        private const double ThumbHalfWidth = 5.0;
        private const double ThumbHalfHeight = 3.0;
        private const double HitRadiusX = 8.0;
        private const double PaintTravelThreshold = 3.0;

        private IBrush _trackBrush = Brushes.Gray;
        private IBrush _barBrush = Brushes.Gray;
        private IBrush _selectedBarBrush = Brushes.White;
        private IBrush _thumbBrush = Brushes.White;
        private IPen _guidePen = new Pen(Brushes.Gray, 1);

        private PianoRollViewModel? _subscribedVm;
        private bool _painting;
        private bool _historyTaken;
        private Point _pressPos;
        private double _lastPaintX;
        private NoteViewModel? _anchorNote;

        private static IHistoryService? History => App.ServiceProvider?.GetService<IHistoryService>();

        static VelocityLaneControl()
        {
            AffectsRender<VelocityLaneControl>(RevisionProperty);
        }

        public VelocityLaneControl()
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            ClipToBounds = true;
            DataContextChanged += OnDataContextChanged;
        }

        public int Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        private PianoRollViewModel? Vm => DataContext as PianoRollViewModel;

        protected override void BuildThemeResources()
        {
            _trackBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Surface0, 120));
            _barBrush = new SolidColorBrush(ThemePalette.Mauve);
            _selectedBarBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Mauve, 230));
            _thumbBrush = new SolidColorBrush(ThemePalette.Text);
            _guidePen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Text, 40)), 1);
        }

        protected override Size MeasureOverride(Size availableSize) => availableSize;

        protected override Size ArrangeOverride(Size finalSize) => finalSize;

        bool ICustomHitTest.HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

        public override void Render(DrawingContext context)
        {
            var vm = Vm;
            if (vm is null) return;
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 1 || h < 1) return;

            context.FillRectangle(_trackBrush, new Rect(0, 0, w, h));
            context.DrawLine(_guidePen, new Point(0, 0.5), new Point(w, 0.5));
            context.DrawLine(_guidePen, new Point(0, h - 0.5), new Point(w, h - 0.5));

            foreach (var note in vm.Notes)
            {
                var x = note.Left + BarWidth * 0.5;
                var velocity = Math.Clamp(note.Velocity, 0f, 1f);
                var fillH = Math.Max(1.0, h * velocity);
                var top = h - fillH;
                var barBrush = note.IsSelected ? _selectedBarBrush : _barBrush;
                context.FillRectangle(barBrush, new Rect(x - BarWidth * 0.5, top, BarWidth, fillH));
                context.FillRectangle(_thumbBrush,
                    new Rect(x - ThumbHalfWidth, top - ThumbHalfHeight, ThumbHalfWidth * 2, ThumbHalfHeight * 2));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var vm = Vm;
            if (vm is null) { base.OnPointerPressed(e); return; }
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { base.OnPointerPressed(e); return; }

            var pos = e.GetPosition(this);
            _pressPos = pos;
            _lastPaintX = pos.X;
            _painting = true;
            _historyTaken = false;
            _anchorNote = FindNearestNote(vm, pos.X);

            if (_anchorNote is not null)
                ApplyVelocityAt(vm, _anchorNote, pos.Y);

            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_painting) { base.OnPointerMoved(e); return; }
            var vm = Vm;
            if (vm is null) return;

            var pos = e.GetPosition(this);
            var velocity = YToVelocity(pos.Y);

            // Vertical drag on the anchor bar, or paint across any bars between last X and current X.
            var dx = Math.Abs(pos.X - _pressPos.X);
            if (dx < PaintTravelThreshold && _anchorNote is not null)
            {
                ApplyVelocityAt(vm, _anchorNote, pos.Y);
            }
            else
            {
                ApplyPaintStroke(vm, _lastPaintX, pos.X, velocity);
                if (_anchorNote is not null)
                    ApplyVelocityValue(vm, _anchorNote, velocity);
            }

            _lastPaintX = pos.X;
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_painting) { base.OnPointerReleased(e); return; }
            _painting = false;
            _anchorNote = null;
            if (ReferenceEquals(e.Pointer.Captured, this))
                e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _painting = false;
            _anchorNote = null;
            base.OnPointerCaptureLost(e);
        }

        private void ApplyVelocityAt(PianoRollViewModel vm, NoteViewModel note, double y)
            => ApplyVelocityValue(vm, note, YToVelocity(y));

        private void ApplyVelocityValue(PianoRollViewModel vm, NoteViewModel note, float velocity)
        {
            EnsureHistory();
            if (!vm.TrySetNoteVelocity(note, velocity)) return;
            vm.PublishNoteEdits();
            InvalidateVisual();
        }

        private void ApplyPaintStroke(PianoRollViewModel vm, double x0, double x1, float velocity)
        {
            var minX = Math.Min(x0, x1) - HitRadiusX;
            var maxX = Math.Max(x0, x1) + HitRadiusX;
            var any = false;
            foreach (var note in vm.Notes)
            {
                var barX = note.Left + BarWidth * 0.5;
                if (barX < minX || barX > maxX) continue;
                EnsureHistory();
                if (!vm.TrySetNoteVelocity(note, velocity)) continue;
                any = true;
            }

            if (!any) return;
            vm.PublishNoteEdits();
            InvalidateVisual();
        }

        private void EnsureHistory()
        {
            if (_historyTaken) return;
            History?.Capture("Edit velocity");
            _historyTaken = true;
        }

        private float YToVelocity(double y)
        {
            var h = Bounds.Height;
            if (h < 1) return 0f;
            return (float)Math.Clamp(1.0 - y / h, 0.0, 1.0);
        }

        private static NoteViewModel? FindNearestNote(PianoRollViewModel vm, double x)
        {
            NoteViewModel? best = null;
            var bestDist = HitRadiusX;
            foreach (var note in vm.Notes)
            {
                var barX = note.Left + BarWidth * 0.5;
                var dist = Math.Abs(barX - x);
                if (dist > bestDist) continue;
                bestDist = dist;
                best = note;
            }

            return best;
        }

        private void OnDataContextChanged(object? sender, EventArgs e) => Resubscribe(Vm);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Resubscribe(Vm);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Resubscribe(null);
            base.OnDetachedFromVisualTree(e);
        }

        private void Resubscribe(PianoRollViewModel? vm)
        {
            if (ReferenceEquals(_subscribedVm, vm)) return;
            if (_subscribedVm is not null)
            {
                _subscribedVm.Notes.CollectionChanged -= OnNotesCollectionChanged;
                _subscribedVm.Metrics.PropertyChanged -= OnMetricsChanged;
                foreach (var note in _subscribedVm.Notes)
                    note.PropertyChanged -= OnNotePropertyChanged;
            }

            _subscribedVm = vm;
            if (_subscribedVm is null) return;

            _subscribedVm.Notes.CollectionChanged += OnNotesCollectionChanged;
            _subscribedVm.Metrics.PropertyChanged += OnMetricsChanged;
            foreach (var note in _subscribedVm.Notes)
                note.PropertyChanged += OnNotePropertyChanged;
            InvalidateVisual();
        }

        private void OnNotesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (NoteViewModel note in e.OldItems)
                    note.PropertyChanged -= OnNotePropertyChanged;
            }

            if (e.NewItems is not null)
            {
                foreach (NoteViewModel note in e.NewItems)
                    note.PropertyChanged += OnNotePropertyChanged;
            }

            InvalidateVisual();
        }

        private void OnNotePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(NoteViewModel.Velocity)
                or nameof(NoteViewModel.Left)
                or nameof(NoteViewModel.IsSelected))
                InvalidateVisual();
        }

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PianoRollMetrics.PixelsPerBeat)
                or nameof(PianoRollMetrics.TotalWidth)
                or nameof(PianoRollMetrics.TotalBeats))
                InvalidateVisual();
        }
    }
}
