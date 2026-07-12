using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Controls;
using Ongenet.App.ViewModels.Panels;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;
using AudioTrack = Ongenet.Core.Models.Audio.Track;

namespace Ongenet.App.Views.Panels;

public partial class MixerView : UserControl
{
    public MixerView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsLeftButtonPressed)
        {
            if ((e.Source as Visual)?.FindAncestorOfType<Slider>(includeSelf: true) is { Name: "VolumeSlider" or "PanSlider" or "SendLevelSlider" } slider)
            {
                var action = slider.Name switch
                {
                    "VolumeSlider" => "Adjust volume",
                    "PanSlider" => "Adjust pan",
                    _ => "Adjust send level"
                };
                App.ServiceProvider?.GetService<IHistoryService>()?.Capture(action);
                return;
            }

            if (FindStripViewModel(e.Source as Visual) is { } clickedStrip && !IsInteractiveControl(e.Source as Visual))
            {
                clickedStrip.SelectTrack();
                return;
            }
        }

        if (!props.IsRightButtonPressed) return;

        var strip = FindStripViewModel(e.Source as Visual);
        var owner = strip?.Track ?? App.ServiceProvider?.GetService<ISelectionService>()?.SelectedTrack;
        if (owner is null) return;

        var panOrVolume = (e.Source as Visual)?.FindAncestorOfType<Slider>(includeSelf: true);
        if (panOrVolume is null) return;

        if (panOrVolume.Name == "VolumeSlider")
        {
            AutomationGesture.Offer(panOrVolume, owner, AutomationGesture.ForVolume(owner),
                () => owner.Volume = AudioTrack.DefaultVolume);
            e.Handled = true;
        }
        else if (panOrVolume.Name == "PanSlider")
        {
            AutomationGesture.Offer(panOrVolume, owner, AutomationGesture.ForPan(owner),
                () => owner.Pan = AudioTrack.DefaultPan);
            e.Handled = true;
        }
        else if (panOrVolume.Name == "SendLevelSlider" && panOrVolume.Tag is TrackSend send)
        {
            var targetName = App.ServiceProvider?.GetService<IProjectService>()?.Current.Tracks
                .FirstOrDefault(t => t.Id == send.TargetTrackId)?.Name;
            AutomationGesture.Offer(panOrVolume, owner, AutomationGesture.ForSendLevel(owner, send, targetName),
                () => send.Level = 0.5);
            e.Handled = true;
        }
    }

    private static MixerStripViewModel? FindStripViewModel(Visual? source)
    {
        while (source is not null)
        {
            if (source is Control { Tag: "mixer-strip", DataContext: MixerStripViewModel vm })
                return vm;
            source = source.GetVisualParent();
        }

        return null;
    }

    private static bool IsInteractiveControl(Visual? source)
    {
        while (source is not null)
        {
            if (source is Button or ToggleButton or Slider or ComboBox or CheckBox)
                return true;
            source = source.GetVisualParent();
        }

        return false;
    }
}
