using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Desktop.ViewModels;
using Ongenet.Desktop.ViewModels.Instruments;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Instrument inspector. Mouse on the mini-keyboard previews notes; the "Load sample" button
    /// (sampler only) opens a file picker; the "Open plugin UI" button (CLAP plugins) opens the
    /// plugin's own GUI window, passing the host window's native handle.
    /// </summary>
    public partial class InstrumentInspectorView : UserControl
    {
        private readonly DispatcherTimer _editorPump;

        public InstrumentInspectorView()
        {
            InitializeComponent();
            // Pumps an open plugin GUI's main-thread work (cheap; only does anything while open).
            _editorPump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _editorPump.Tick += (_, _) => (DataContext as InstrumentInspectorViewModel)?.PumpEditor();
            _editorPump.Start();

            Windows.PluginEditorHost.EditorStateChanged += OnEditorStateChanged;
        }

        private void OnTogglePluginUi(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not InstrumentInspectorViewModel vm) return;
            var editor = vm.CurrentEditor;
            if (editor is null) return;
            Windows.PluginEditorHost.Toggle(editor, vm.InstrumentName, TopLevel.GetTopLevel(this) as Window);
        }

        // Refresh the open/close button when the editor's state changes (incl. user hiding its window).
        private void OnEditorStateChanged(IPluginEditor editor)
        {
            if (DataContext is InstrumentInspectorViewModel vm && ReferenceEquals(editor, vm.CurrentEditor))
                vm.NotifyEditorState();
        }

        private void OnKeyPressed(object? sender, PointerPressedEventArgs e)
        {
            if (Key(sender) is { } note && DataContext is InstrumentInspectorViewModel vm)
            {
                vm.NoteOn(note);
                e.Pointer.Capture(sender as IInputElement);
            }
        }

        private void OnKeyReleased(object? sender, PointerReleasedEventArgs e) => ReleaseKey(sender);
        private void OnKeyExited(object? sender, PointerEventArgs e) => ReleaseKey(sender);

        private void ReleaseKey(object? sender)
        {
            if (Key(sender) is { } note && DataContext is InstrumentInspectorViewModel vm)
            {
                vm.NoteOff(note);
            }
        }

        private static int? Key(object? sender)
            => (sender as Control)?.DataContext is KeyViewModel key ? key.MidiNote : null;

        private async void OnLoadSample(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not InstrumentInspectorViewModel vm) return;
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

        private async void OnLoadSfz(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not InstrumentInspectorViewModel vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load SFZ instrument",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("SFZ instrument") { Patterns = new[] { "*.sfz" } }
                }
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) vm.LoadSfzFromPath(path);
        }

        // Pitch bend springs back to centre when the user lets go of the slider.
        private void OnBendReleased(object? sender, PointerReleasedEventArgs e)
            => (DataContext as InstrumentInspectorViewModel)?.ResetPitchBend();

        private void OnBendCaptureLost(object? sender, PointerCaptureLostEventArgs e)
            => (DataContext as InstrumentInspectorViewModel)?.ResetPitchBend();
    }
}
