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

        // Render → choose a path/format → export off the UI thread. MP3/FLAC are offered only when a
        // system ffmpeg is available; the export format follows the chosen file's extension.
        private async void OnRender(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not TransportViewModel vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var types = new List<FilePickerFileType>
            {
                new("WAV audio") { Patterns = new[] { "*.wav" } }
            };
            if (Core.Audio.Files.FfmpegEncoder.IsAvailable)
            {
                types.Add(new FilePickerFileType("MP3 audio (320 kbps)") { Patterns = new[] { "*.mp3" } });
                types.Add(new FilePickerFileType("FLAC audio") { Patterns = new[] { "*.flac" } });
            }

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Render Audio",
                SuggestedFileName = "render.wav",
                DefaultExtension = "wav",
                FileTypeChoices = types
            });

            var path = file?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                await vm.RenderToFileAsync(path);
            }
            catch (Exception ex)
            {
                var logger = App.ServiceProvider?.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                    ?.CreateLogger("Render");
                if (logger is not null)
                    Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, ex, "Render to '{Path}' failed.", path);
            }
        }
    }
}
