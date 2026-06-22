using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.App.ViewModels.Library;

namespace Ongenet.App.Views.Panels
{
    /// <summary>
    /// Renders any <see cref="LibraryListViewModel"/> (Everything / Samples / Soundfonts / Instruments /
    /// Effects / presets) as a searchable tree of folders and draggable leaves. Each leaf drags its node's
    /// payload (instrument/effect id, file path or preset path) and double-clicking runs its optional
    /// activate action (e.g. preview a sample). Folder rows are not draggable.
    /// </summary>
    public partial class LibraryListView : UserControl
    {
        private const double DragThreshold = 4;
        private LibraryNode? _pressed;
        private PointerPressedEventArgs? _pressArgs;
        private Point _pressPoint;

        public LibraryListView()
        {
            InitializeComponent();
            NodeTree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            NodeTree.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            NodeTree.AddHandler(DoubleTappedEvent, OnDoubleTapped);
        }

        // Only leaves with a drag payload are draggable; folders return null.
        private static LibraryNode? DraggableOf(object? source)
            => (source as Control)?.DataContext is LibraryNode { DragFormat: not null } n ? n : null;

        private static LibraryNode? NodeOf(object? source) => (source as Control)?.DataContext as LibraryNode;

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _pressed = DraggableOf(e.Source);
            _pressArgs = _pressed is not null ? e : null;
            if (_pressed is not null) _pressPoint = e.GetPosition(this);
        }

        private async void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_pressed is null || _pressArgs is null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pressed = null; return; }
            var delta = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

            var node = _pressed;
            var args = _pressArgs;
            _pressed = null;
            _pressArgs = null;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(node.DragFormat!, node.DragPayload!));
            try { await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Copy); }
            catch (Exception) { /* drag cancelled */ }
        }

        private void OnDoubleTapped(object? sender, TappedEventArgs e)
            => NodeOf(e.Source)?.Activate?.Invoke();

        private void OnExpandRecursive(object? sender, RoutedEventArgs e) => SetExpanded(sender, true);

        private void OnCollapseRecursive(object? sender, RoutedEventArgs e) => SetExpanded(sender, false);

        // The context menu's items inherit the folder row's DataContext (the LibraryNode).
        private static void SetExpanded(object? sender, bool expanded)
        {
            if ((sender as Control)?.DataContext is LibraryNode node) SetExpandedRecursive(node, expanded);
        }

        private static void SetExpandedRecursive(LibraryNode node, bool expanded)
        {
            if (!node.IsFolder) return;
            node.IsExpanded = expanded;
            foreach (var child in node.Children) SetExpandedRecursive(child, expanded);
        }
    }
}
