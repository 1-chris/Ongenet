using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Input;
using Ongenet.Desktop.ViewModels;

namespace Ongenet.Desktop.Views.Windows
{
    /// <summary>
    /// The application's main window. Blank-slate content with the original custom Catppuccin title bar.
    /// </summary>
    public partial class MainWindow : Window
    {
        private LogWindow? _logWindow;

        // FL-Studio-style typing-keyboard note input: tracks which physical keys are currently
        // held (→ which MIDI notes are sounding) so auto-repeat KeyDowns don't re-trigger.
        private readonly Dictionary<Key, int> _heldKeys = new();

        public MainWindow()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel);
        }

        private IPreviewService? Preview => App.ServiceProvider?.GetService<IPreviewService>();
        private ISelectionService? Selection => App.ServiceProvider?.GetService<ISelectionService>();
        private ITransportService? Transport => App.ServiceProvider?.GetService<ITransportService>();

        private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
        {
            // Don't steal typing from text inputs (track rename, numeric fields, etc.).
            if (e.Source is TextBox) return;

            // App shortcuts. These run before (and instead of) the typing-keyboard MIDI below, so a
            // modified key like Shift+[ never also sounds a note.
            switch (e.Key)
            {
                case Key.Space:
                    TogglePlayStop();
                    e.Handled = true;
                    return;
                case Key.OemOpenBrackets when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    if (Transport is { } ts) ts.LoopStart = ts.StartBeat;
                    e.Handled = true;
                    return;
                case Key.OemCloseBrackets when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    if (Transport is { } te) te.LoopEnd = te.StartBeat;
                    e.Handled = true;
                    return;
            }

            // FL-style typing-keyboard note input — only on an unmodified key. This guarantees Shift/Ctrl/
            // Alt + any key (e.g. Shift+[ for the loop, Ctrl+D to duplicate) never triggers a MIDI note.
            if (e.KeyModifiers != KeyModifiers.None) return;
            if (Selection?.SelectedTrack?.Instrument is null) return;
            if (!ComputerKeyboard.TryGetNote(e.Key, out var note)) return;
            if (_heldKeys.ContainsKey(e.Key)) { e.Handled = true; return; }

            _heldKeys[e.Key] = note;
            Preview?.NoteOn(note);
            e.Handled = true;
        }

        // Space toggles transport. Routed through the transport view model so it honours the same
        // recording-aware stop and can-play guards as the toolbar buttons.
        private void TogglePlayStop()
        {
            if (DataContext is not MainViewModel vm) return;
            var transport = vm.Transport;
            if (transport.IsPlaying || transport.IsRecording) transport.StopCommand.Execute(null);
            else if (transport.CanPlay) transport.PlayCommand.Execute(null);
        }

        private void OnGlobalKeyUp(object? sender, KeyEventArgs e)
        {
            if (!_heldKeys.Remove(e.Key, out var note)) return;
            Preview?.NoteOff(note);
            e.Handled = true;
        }

        // --- Custom title bar (ported verbatim from the original MainWindow) ---

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is not Control c || c is Button || c.Parent is Button) return;

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }

        private void OnResizeHandlePressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is string tag)
            {
                var edge = tag switch
                {
                    "Left" => WindowEdge.West,
                    "Right" => WindowEdge.East,
                    "Top" => WindowEdge.North,
                    "Bottom" => WindowEdge.South,
                    "TopLeft" => WindowEdge.NorthWest,
                    "TopRight" => WindowEdge.NorthEast,
                    "BottomLeft" => WindowEdge.SouthWest,
                    "BottomRight" => WindowEdge.SouthEast,
                    _ => WindowEdge.North
                };

                BeginResizeDrag(edge, e);
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // --- Blank-slate content ---

        private void OpenLogs_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            if (_logWindow is null)
            {
                _logWindow = new LogWindow();
                _logWindow.SetViewModel(viewModel);
                _logWindow.Closed += (_, _) => _logWindow = null;
                _logWindow.Show();
            }
            else
            {
                _logWindow.Activate();
            }
        }
    }
}
