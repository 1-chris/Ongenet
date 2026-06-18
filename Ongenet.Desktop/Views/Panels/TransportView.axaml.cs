using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ongenet.Desktop.ViewModels;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>Top-bar transport controls. Polls the master meter + playhead time on a timer.</summary>
    public partial class TransportView : UserControl
    {
        private readonly DispatcherTimer _timer;

        public TransportView()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => (DataContext as TransportViewModel)?.RefreshMeters();
            _timer.Start();
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
