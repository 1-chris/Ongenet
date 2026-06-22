using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Controls;
using Ongenet.App.Services;

namespace Ongenet.App.Views.Panels
{
    /// <summary>Left-hand inspector for the selected track.</summary>
    public partial class TrackInspectorView : UserControl
    {
        public TrackInspectorView()
        {
            InitializeComponent();
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        }

        // Right-click the Volume/Pan sliders → "Create automation track"; left-press snapshots for undo.
        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;

            // A left press on a fader is the start of a value change — snapshot once for undo.
            if (props.IsLeftButtonPressed
                && (e.Source as Visual)?.FindAncestorOfType<Slider>(includeSelf: true) is { } s
                && (s.Name == "VolumeSlider" || s.Name == "PanSlider"))
            {
                App.ServiceProvider?.GetService<IHistoryService>()?.Capture(s.Name == "VolumeSlider" ? "Adjust volume" : "Adjust pan");
                return;
            }

            if (!props.IsRightButtonPressed) return;
            var owner = App.ServiceProvider?.GetService<ISelectionService>()?.SelectedTrack;
            if (owner is null) return;

            var slider = (e.Source as Visual)?.FindAncestorOfType<Slider>(includeSelf: true);
            if (slider is null) return;

            var vm = DataContext as ViewModels.TrackInspectorViewModel;

            if (slider.Name == "VolumeSlider")
            {
                AutomationGesture.Offer(slider, owner, AutomationGesture.ForVolume(owner),
                    () => { if (vm is not null) vm.Volume = Core.Models.Audio.Track.DefaultVolume; });
                e.Handled = true;
            }
            else if (slider.Name == "PanSlider")
            {
                AutomationGesture.Offer(slider, owner, AutomationGesture.ForPan(owner),
                    () => { if (vm is not null) vm.Pan = Core.Models.Audio.Track.DefaultPan; });
                e.Handled = true;
            }
        }
    }
}
