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

        public InstrumentsView()
        {
            InitializeComponent();
            InstrumentList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            InstrumentList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _pressed = (e.Source as Control)?.DataContext as InstrumentInfo;
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

            var data = new DataObject();
            data.Set(DragFormats.Instrument, _pressed.Id);
            _pressed = null;

            try
            {
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }
            catch (Exception)
            {
                // ignore a failed drag
            }
        }
    }
}
