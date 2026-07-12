using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ongenet.Core.Models.Audio;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// Draws warp markers on an audio clip and allows dragging them when the clip is selected.
    /// </summary>
    public sealed class WarpMarkerControl : Control
    {
        private const double HandleWidth = 6.0;

        public static readonly StyledProperty<IReadOnlyList<WarpMarker>?> MarkersProperty =
            AvaloniaProperty.Register<WarpMarkerControl, IReadOnlyList<WarpMarker>?>(nameof(Markers));

        public static readonly StyledProperty<double> ClipLengthBeatsProperty =
            AvaloniaProperty.Register<WarpMarkerControl, double>(nameof(ClipLengthBeats), 1.0);

        public static readonly StyledProperty<double> SourceDurationSecondsProperty =
            AvaloniaProperty.Register<WarpMarkerControl, double>(nameof(SourceDurationSeconds));

        public static readonly StyledProperty<double> SourceOffsetSecondsProperty =
            AvaloniaProperty.Register<WarpMarkerControl, double>(nameof(SourceOffsetSeconds));

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<WarpMarkerControl, bool>(nameof(IsSelected));

        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<WarpMarkerControl, int>(nameof(Revision));

        public static readonly StyledProperty<Action<int, double>?> MarkerMovedProperty =
            AvaloniaProperty.Register<WarpMarkerControl, Action<int, double>?>(nameof(MarkerMoved));

        static WarpMarkerControl()
        {
            AffectsRender<WarpMarkerControl>(MarkersProperty, ClipLengthBeatsProperty, SourceDurationSecondsProperty,
                SourceOffsetSecondsProperty, IsSelectedProperty, RevisionProperty);
        }

        public IReadOnlyList<WarpMarker>? Markers { get => GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
        public double ClipLengthBeats { get => GetValue(ClipLengthBeatsProperty); set => SetValue(ClipLengthBeatsProperty, value); }
        public double SourceDurationSeconds { get => GetValue(SourceDurationSecondsProperty); set => SetValue(SourceDurationSecondsProperty, value); }
        public double SourceOffsetSeconds { get => GetValue(SourceOffsetSecondsProperty); set => SetValue(SourceOffsetSecondsProperty, value); }
        public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
        public int Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
        public Action<int, double>? MarkerMoved { get => GetValue(MarkerMovedProperty); set => SetValue(MarkerMovedProperty, value); }

        private int _dragIndex = -1;

        public override void Render(DrawingContext context)
        {
            var width = Bounds.Width;
            var height = Bounds.Height;
            if (width < 1 || height < 1 || Markers is null || ClipLengthBeats <= 0) return;

            var pen = new Pen(new SolidColorBrush(Colors.White, 0.85), 1.5);
            var fill = new SolidColorBrush(Colors.White, 0.35);

            for (var i = 0; i < Markers.Count; i++)
            {
                var wm = Markers[i];
                var x = wm.BeatPosition / ClipLengthBeats * width;
                if (x < 0 || x > width) continue;
                context.DrawLine(pen, new Point(x, 0), new Point(x, height));
                if (IsSelected)
                {
                    context.FillRectangle(fill, new Rect(x - HandleWidth * 0.5, height - 10, HandleWidth, 8));
                }
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is ViewModels.Timeline.ClipViewModel vm)
                MarkerMoved = vm.OnWarpMarkerMoved;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!IsSelected || Markers is null || ClipLengthBeats <= 0) return;
            var pos = e.GetPosition(this);
            var width = Bounds.Width;
            if (width < 1) return;

            for (var i = 0; i < Markers.Count; i++)
            {
                var x = Markers[i].BeatPosition / ClipLengthBeats * width;
                if (Math.Abs(pos.X - x) <= HandleWidth)
                {
                    _dragIndex = i;
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_dragIndex < 0 || Markers is null || ClipLengthBeats <= 0) return;
            var width = Bounds.Width;
            if (width < 1) return;

            var beat = Math.Clamp(e.GetPosition(this).X / width * ClipLengthBeats, 0, ClipLengthBeats);
            MarkerMoved?.Invoke(_dragIndex, beat);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_dragIndex < 0) return;
            _dragIndex = -1;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
