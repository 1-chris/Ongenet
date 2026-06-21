using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.Desktop.ViewModels;
using Ongenet.Desktop.ViewModels.FileSystem;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Right-hand file browser. Starts a drag-and-drop operation when an audio file is dragged
    /// out, carrying the file path for the timeline to consume.
    /// </summary>
    public partial class FileBrowserView : UserControl
    {
        private const double DragThreshold = 4.0;

        private Point _pressPoint;
        private FileNodeViewModel? _pressedNode;
        private PointerPressedEventArgs? _pressArgs;

        public FileBrowserView()
        {
            InitializeComponent();

            // Tunnel so we see the press before the TreeView consumes it for selection.
            FileTree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            FileTree.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            FileTree.SelectionChanged += OnSelectionChanged;
        }

        // Previewing a selected audio file (waveform + stats + optional auto-play) is handled by the
        // shared AudioPreviewViewModel docked under the library tabs.
        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (FileTree.SelectedItem is not FileNodeViewModel { IsDirectory: false } node) return;
            if (App.ServiceProvider?.GetService(typeof(AudioPreviewViewModel)) is AudioPreviewViewModel preview)
                preview.Select(node.FullPath);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _pressedNode = null;
            _pressArgs = null;
            if ((e.Source as Control)?.DataContext is FileNodeViewModel { IsDirectory: false } node)
            {
                _pressPoint = e.GetPosition(this);
                _pressedNode = node;
                _pressArgs = e; // DoDragDropAsync requires the originating pressed event
            }
        }

        private async void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_pressedNode is null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _pressedNode = null;
                return;
            }

            var delta = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

            // Only audio files are draggable onto the timeline.
            if (DataContext is not FileBrowserViewModel vm || _pressArgs is null || !vm.IsAudioFile(_pressedNode.FullPath))
            {
                _pressedNode = null;
                return;
            }

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragFormats.AudioFile, _pressedNode.FullPath));
            var pressArgs = _pressArgs;
            _pressedNode = null;
            _pressArgs = null;

            try
            {
                await DragDrop.DoDragDropAsync(pressArgs, data, DragDropEffects.Copy);
            }
            catch (Exception)
            {
                // A failed drag shouldn't take the app down.
            }
        }
    }
}
