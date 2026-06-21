using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Desktop.ViewModels;
using Ongenet.Desktop.ViewModels.Instruments;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Instrument-rack inspector. The mini-keyboard previews notes on every enabled instrument; the
    /// "+ Add instrument" menu and dragging an instrument from the Instruments panel add a slot. Per-card
    /// loaders / plugin UIs live in <see cref="InstrumentSlotView"/>.
    /// </summary>
    public partial class InstrumentInspectorView : UserControl
    {
        private readonly DispatcherTimer _editorPump;

        public InstrumentInspectorView()
        {
            InitializeComponent();
            // Pumps any open plugin GUIs' main-thread work (cheap; only does anything while open).
            _editorPump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _editorPump.Tick += (_, _) => (DataContext as InstrumentInspectorViewModel)?.PumpEditors();
            _editorPump.Start();

            Windows.PluginEditorHost.EditorStateChanged += OnEditorStateChanged;

            // Dropping an instrument from the library adds it to the selected track's rack.
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        private void OnEditorStateChanged(IPluginEditor editor)
            => (DataContext as InstrumentInspectorViewModel)?.RefreshEditor(editor);

        private void OnAddInstrument(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is InstrumentInfo info && DataContext is InstrumentInspectorViewModel vm)
                vm.AddInstrument(info.Id);
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            var ok = DataContext is InstrumentInspectorViewModel { HasInstrumentTrack: true }
                     && (e.DataTransfer.Contains(DragFormats.Instrument)
                         || e.DataTransfer.Contains(DragFormats.Preset)
                         || e.DataTransfer.Contains(DragFormats.SoundFont));
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            if (DataContext is not InstrumentInspectorViewModel vm) return;
            if (e.DataTransfer.TryGetValue(DragFormats.Instrument) is { } id)
            {
                vm.AddInstrument(id);
                e.Handled = true;
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.Preset) is { } presetPath)
            {
                vm.AddInstrumentPreset(presetPath);
                e.Handled = true;
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.SoundFont) is { } soundFontPath)
            {
                vm.AddSoundFont(soundFontPath);
                e.Handled = true;
            }
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
                vm.NoteOff(note);
        }

        private static int? Key(object? sender)
            => (sender as Control)?.DataContext is KeyViewModel key ? key.MidiNote : null;
    }
}
