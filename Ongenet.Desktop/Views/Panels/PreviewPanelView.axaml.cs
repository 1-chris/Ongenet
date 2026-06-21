using Avalonia.Controls;
using Avalonia.Interactivity;
using Ongenet.Desktop.ViewModels;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>Library/file audio preview: waveform, BPM/length/key stats, and play/stop + auto-play.</summary>
    public partial class PreviewPanelView : UserControl
    {
        public PreviewPanelView()
        {
            InitializeComponent();
        }

        private void OnPlay(object? sender, RoutedEventArgs e)
            => (DataContext as AudioPreviewViewModel)?.PlayCommand.Execute(null);

        private void OnStop(object? sender, RoutedEventArgs e)
            => (DataContext as AudioPreviewViewModel)?.StopCommand.Execute(null);
    }
}
