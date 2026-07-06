using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Panels
{
    /// <summary>
    /// Renders the <see cref="ProjectClipsViewModel"/> (the left sidebar's Project Clips tab) as
    /// grouped clip cards. Cards drag onto the timeline carrying the representative clip's id
    /// (<see cref="DragFormats.ProjectClip"/>); the timeline validates the target track kind.
    /// Right-click offers rename / delete-all via the item's commands.
    /// </summary>
    public partial class ProjectClipsView : UserControl
    {
        private const double DragThreshold = 4;
        private ProjectClipItemViewModel? _pressed;
        private PointerPressedEventArgs? _pressArgs;
        private Point _pressPoint;

        public ProjectClipsView()
        {
            InitializeComponent();
            GroupsList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            GroupsList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        }

        private static ProjectClipItemViewModel? ItemOf(object? source)
            => (source as Control)?.DataContext as ProjectClipItemViewModel;

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _pressed = ItemOf(e.Source);
            _pressArgs = _pressed is not null ? e : null;
            if (_pressed is not null) _pressPoint = e.GetPosition(this);
        }

        private async void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_pressed is null || _pressArgs is null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pressed = null; return; }
            var delta = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

            var item = _pressed;
            var args = _pressArgs;
            _pressed = null;
            _pressArgs = null;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragFormats.ProjectClip, item.DragPayload));
            try { await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Copy); }
            catch (Exception) { /* drag cancelled */ }
        }
    }
}
