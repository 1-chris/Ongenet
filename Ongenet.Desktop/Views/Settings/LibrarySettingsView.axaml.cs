using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ongenet.Desktop.ViewModels;

namespace Ongenet.Desktop.Views.Settings
{
    /// <summary>Library settings: pick the folders scanned for samples and sound fonts + the auto-play toggle.</summary>
    public partial class LibrarySettingsView : UserControl
    {
        public LibrarySettingsView()
        {
            InitializeComponent();
        }

        private async void OnAddSample(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not LibrarySettingsViewModel vm) return;
            if (await PickFolder() is { } path) vm.AddSampleFolder(path);
        }

        private void OnRemoveSample(object? sender, RoutedEventArgs e)
            => (DataContext as LibrarySettingsViewModel)?.RemoveSelectedSample();

        private async void OnAddSoundFont(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not LibrarySettingsViewModel vm) return;
            if (await PickFolder() is { } path) vm.AddSoundFontFolder(path);
        }

        private void OnRemoveSoundFont(object? sender, RoutedEventArgs e)
            => (DataContext as LibrarySettingsViewModel)?.RemoveSelectedSoundFont();

        private async System.Threading.Tasks.Task<string?> PickFolder()
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return null;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose a folder to scan",
                AllowMultiple = false
            });
            return folders.FirstOrDefault()?.TryGetLocalPath();
        }
    }
}
