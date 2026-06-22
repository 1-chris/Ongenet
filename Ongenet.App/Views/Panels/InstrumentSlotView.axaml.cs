using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ongenet.App.ViewModels.Instruments;

namespace Ongenet.App.Views.Panels
{
    /// <summary>
    /// One instrument card in the rack. The "Load sample/SFZ" buttons open file pickers; "Open plugin UI"
    /// opens a CLAP/LV2 plugin's own GUI window. DataContext is an <see cref="InstrumentSlotViewModel"/>.
    /// </summary>
    public partial class InstrumentSlotView : UserControl
    {
        public InstrumentSlotView()
        {
            InitializeComponent();

            // Dropping a library instrument onto this card inserts above/below it or replaces it,
            // depending on where in the card the pointer is released.
            AddHandler(DragDrop.DragOverEvent, OnCardDragOver);
            AddHandler(DragDrop.DropEvent, OnCardDrop);
            AddHandler(DragDrop.DragLeaveEvent, OnCardDragLeave);
        }

        // Instrument, instrument-preset and sound-font drops all use the vertical zone (above / replace /
        // below); in the replace zone a sound font loads into a Sampler card or replaces a non-sampler.
        private static bool IsZoned(DragEventArgs e)
            => e.DataTransfer.Contains(DragFormats.Instrument)
               || e.DataTransfer.Contains(DragFormats.Preset)
               || e.DataTransfer.Contains(DragFormats.SoundFont);

        // Maps the pointer's vertical position within the card to a rack edit: top/bottom thirds insert
        // above/below, the middle replaces.
        private InstrumentSlotViewModel.RackDropZone ZoneAt(DragEventArgs e)
        {
            var height = CardRoot.Bounds.Height;
            if (height <= 0) return InstrumentSlotViewModel.RackDropZone.Replace;
            var t = e.GetPosition(CardRoot).Y / height;
            return t < 0.30 ? InstrumentSlotViewModel.RackDropZone.Above
                 : t > 0.70 ? InstrumentSlotViewModel.RackDropZone.Below
                 : InstrumentSlotViewModel.RackDropZone.Replace;
        }

        private void OnCardDragOver(object? sender, DragEventArgs e)
        {
            if (IsZoned(e))
            {
                e.DragEffects = DragDropEffects.Copy;
                ShowDropIndicator(ZoneAt(e));
                e.Handled = true; // don't let the rack's empty-area "append" handler also fire
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
                ClearDropIndicators();
            }
        }

        private void OnCardDrop(object? sender, DragEventArgs e)
        {
            ClearDropIndicators();
            if (DataContext is not InstrumentSlotViewModel vm) return;

            if (e.DataTransfer.TryGetValue(DragFormats.Instrument) is { } id)
            {
                vm.DropInstrument(id, ZoneAt(e));
                e.Handled = true;
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.Preset) is { } presetPath)
            {
                vm.DropPreset(presetPath, ZoneAt(e));
                e.Handled = true;
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.SoundFont) is { } sfPath)
            {
                if (vm.DropSoundFont(sfPath, ZoneAt(e))) e.Handled = true;
            }
        }

        private void OnCardDragLeave(object? sender, DragEventArgs e) => ClearDropIndicators();

        private void OnSavePreset(object? sender, RoutedEventArgs e)
        {
            (DataContext as InstrumentSlotViewModel)?.SaveAsPreset();
            SavePresetButton.Flyout?.Hide();
        }

        private void ShowDropIndicator(InstrumentSlotViewModel.RackDropZone zone)
        {
            DropReplace.IsVisible = zone == InstrumentSlotViewModel.RackDropZone.Replace;
            DropLineTop.IsVisible = zone == InstrumentSlotViewModel.RackDropZone.Above;
            DropLineBottom.IsVisible = zone == InstrumentSlotViewModel.RackDropZone.Below;
        }

        private void ClearDropIndicators()
        {
            DropReplace.IsVisible = false;
            DropLineTop.IsVisible = false;
            DropLineBottom.IsVisible = false;
        }

        private void OnTogglePluginUi(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not InstrumentSlotViewModel vm || vm.CurrentEditor is null) return;
            Windows.PluginEditorHost.Toggle(vm.CurrentEditor, vm.InstrumentName, TopLevel.GetTopLevel(this) as Window);
        }

        private async void OnLoadSample(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not InstrumentSlotViewModel vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load sample",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Audio") { Patterns = new[] { "*.wav", "*.wave" } }
                }
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) vm.LoadSampleFromPath(path);
        }

        private async void OnLoadSampler(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not InstrumentSlotViewModel vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load sound-font instrument",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Sound fonts") { Patterns = new[] { "*.sfz", "*.sf2" } },
                    new("SFZ instrument") { Patterns = new[] { "*.sfz" } },
                    new("SF2 SoundFont") { Patterns = new[] { "*.sf2" } }
                }
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) vm.LoadSamplerFromPath(path);
        }

        private void OnBendReleased(object? sender, PointerReleasedEventArgs e)
            => (DataContext as InstrumentSlotViewModel)?.ResetPitchBend();

        private void OnBendCaptureLost(object? sender, PointerCaptureLostEventArgs e)
            => (DataContext as InstrumentSlotViewModel)?.ResetPitchBend();
    }
}
