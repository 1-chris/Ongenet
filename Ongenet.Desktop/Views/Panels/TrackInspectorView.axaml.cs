using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Controls;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>Left-hand inspector for the selected track.</summary>
    public partial class TrackInspectorView : UserControl
    {
        public TrackInspectorView()
        {
            InitializeComponent();
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        }

        // Right-click the Volume/Pan sliders → "Create automation track" for that track property.
        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
            var owner = App.ServiceProvider?.GetService<ISelectionService>()?.SelectedTrack;
            if (owner is null) return;

            var slider = (e.Source as Visual)?.FindAncestorOfType<Slider>(includeSelf: true);
            if (slider is null) return;

            if (slider.Name == "VolumeSlider")
            {
                AutomationGesture.Offer(slider, owner, AutomationGesture.ForVolume(owner));
                e.Handled = true;
            }
            else if (slider.Name == "PanSlider")
            {
                AutomationGesture.Offer(slider, owner, AutomationGesture.ForPan(owner));
                e.Handled = true;
            }
        }
    }
}
