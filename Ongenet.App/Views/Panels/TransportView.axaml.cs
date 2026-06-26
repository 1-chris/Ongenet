using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Controls;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Views.Panels
{
    /// <summary>Top-bar transport controls. Refreshes the master meter + playhead time from the shared
    /// PlaybackClock (pumped by the timeline's render-frame loop) — not its own timer, which competed
    /// with the render frame and capped playback at 30fps.</summary>
    public partial class TransportView : UserControl
    {
        public TransportView()
        {
            InitializeComponent();
            var clock = App.ServiceProvider?.GetService<IPlaybackClock>();
            if (clock is not null) clock.Tick += () => (DataContext as TransportViewModel)?.RefreshMeters();
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        }

        // Right-click the Tempo / Time editors → "Create automation track" on the master track, so tempo
        // and time signature automate through the same lane pipeline as any knob, fader or on/off switch.
        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;

            var editor = (e.Source as Visual)?.FindAncestorOfType<NumericUpDown>(includeSelf: true);
            if (editor is null) return;

            var project = App.ServiceProvider?.GetService<IProjectService>();
            var master = project?.Current.Master;
            if (project is null || master is null) return;

            if (editor.Name == "TempoEditor")
            {
                AutomationGesture.Offer(editor, master, AutomationGesture.ForTempo(project.Current));
                e.Handled = true;
            }
            else if (editor.Name == "TimeSigEditor")
            {
                AutomationGesture.Offer(editor, master, AutomationGesture.ForTimeSignature(project.Current));
                e.Handled = true;
            }
        }

        // Render → choose a WAV path → export off the UI thread.
        private async void OnRender(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not TransportViewModel vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Render to WAV",
                SuggestedFileName = "render.wav",
                DefaultExtension = "wav",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new("WAV audio") { Patterns = new[] { "*.wav" } }
                }
            });

            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) await vm.RenderToFileAsync(path);
        }
    }
}
