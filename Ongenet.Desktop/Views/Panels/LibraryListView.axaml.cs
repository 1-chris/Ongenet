using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.Desktop.ViewModels.Library;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Renders any <see cref="LibraryListViewModel"/> (Effects / Samples / Soundfonts / presets): grouped,
    /// draggable rows. Each row drags its entry's payload (instrument/effect id, file path or preset path)
    /// and double-clicking runs its optional activate action (e.g. preview a sample).
    /// </summary>
    public partial class LibraryListView : UserControl
    {
        private const double DragThreshold = 4;
        private LibraryEntry? _pressed;
        private PointerPressedEventArgs? _pressArgs;
        private Point _pressPoint;

        public LibraryListView()
        {
            InitializeComponent();
            SectionList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            SectionList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            SectionList.AddHandler(DoubleTappedEvent, OnDoubleTapped);
        }

        private static LibraryEntry? EntryOf(object? source) => (source as Control)?.DataContext as LibraryEntry;

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _pressed = EntryOf(e.Source);
            _pressArgs = _pressed is not null ? e : null;
            if (_pressed is not null) _pressPoint = e.GetPosition(this);
        }

        private async void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_pressed is null || _pressArgs is null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pressed = null; return; }
            var delta = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

            var entry = _pressed;
            var args = _pressArgs;
            _pressed = null;
            _pressArgs = null;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(entry.DragFormat, entry.DragPayload));
            try { await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Copy); }
            catch (Exception) { /* drag cancelled */ }
        }

        private void OnDoubleTapped(object? sender, TappedEventArgs e)
            => EntryOf(e.Source)?.Activate?.Invoke();
    }
}
