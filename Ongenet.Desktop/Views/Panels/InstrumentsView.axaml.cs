using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Instruments tab. Dragging an instrument out starts a drag carrying its type id, which the
    /// timeline turns into a new instrument track.
    /// </summary>
    public partial class InstrumentsView : UserControl
    {
        private const double DragThreshold = 4.0;

        private Point _pressPoint;
        private InstrumentInfo? _pressed;
        private PointerPressedEventArgs? _pressArgs;

        public InstrumentsView()
        {
            InitializeComponent();
            InstrumentList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            InstrumentList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _pressed = (e.Source as Control)?.DataContext as InstrumentInfo;
            _pressArgs = _pressed is not null ? e : null;
            if (_pressed is not null) _pressPoint = e.GetPosition(this);
        }

        private async void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_pressed is null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _pressed = null;
                return;
            }

            var delta = e.GetPosition(this) - _pressPoint;
            if (System.Math.Abs(delta.X) < DragThreshold && System.Math.Abs(delta.Y) < DragThreshold) return;
            if (_pressArgs is null) { _pressed = null; return; }

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragFormats.Instrument, _pressed.Id));
            var pressArgs = _pressArgs;
            _pressed = null;
            _pressArgs = null;

            try
            {
                await DragDrop.DoDragDropAsync(pressArgs, data, DragDropEffects.Copy);
            }
            catch (Exception)
            {
                // ignore a failed drag
            }
        }
    }
}
