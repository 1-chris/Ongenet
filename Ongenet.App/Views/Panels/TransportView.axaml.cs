using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;

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
