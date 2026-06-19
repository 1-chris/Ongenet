using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
        private ThemeWindow? _themeWindow;

        // FL-Studio-style typing-keyboard note input: tracks which physical keys are currently
        // held (→ which MIDI notes are sounding) so auto-repeat KeyDowns don't re-trigger.
        private readonly Dictionary<Key, int> _heldKeys = new();

        public MainWindow()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel);
            Closing += OnClosing;
        }

        // Don't let the app exit while a save is still writing — that would truncate the file.
        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (ProjectFile?.IsBusy == true)
            {
                e.Cancel = true;
                _ = MessageDialog.Notify(this, "Please wait",
                    "A save is still in progress. Try closing again once it finishes.");
            }
        }

        private IPreviewService? Preview => App.ServiceProvider?.GetService<IPreviewService>();
        private ISelectionService? Selection => App.ServiceProvider?.GetService<ISelectionService>();
        private ITransportService? Transport => App.ServiceProvider?.GetService<ITransportService>();
        private Services.IHistoryService? History => App.ServiceProvider?.GetService<Services.IHistoryService>();

        private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
        {
            // Don't steal typing from text inputs (track rename, numeric fields, etc.).
            if (e.Source is TextBox) return;

            // Project file shortcuts (Ctrl+N/O/S, Ctrl+Shift+S).
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                switch (e.Key)
                {
                    case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift): _ = SaveAsAsync(); e.Handled = true; return;
                    case Key.S: _ = SaveAsync(); e.Handled = true; return;
                    case Key.O: _ = OpenAsync(); e.Handled = true; return;
                    case Key.N: _ = NewAsync(); e.Handled = true; return;
                    case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Shift): History?.Redo(); e.Handled = true; return;
                    case Key.Z: History?.Undo(); e.Handled = true; return;
                    case Key.Y: History?.Redo(); e.Handled = true; return;
                }
            }

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

        private void OpenTheme_Click(object? sender, RoutedEventArgs e)
        {
            var viewModel = App.ServiceProvider?.GetService<ThemeEditorViewModel>();
            if (viewModel is null) return;

            if (_themeWindow is null)
            {
                _themeWindow = new ThemeWindow();
                _themeWindow.SetViewModel(viewModel);
                _themeWindow.Closed += (_, _) => _themeWindow = null;
                _themeWindow.Show();
            }
            else
            {
                _themeWindow.Activate();
            }
        }

        // --- Project file: New / Open / Save / Save As ---

        private IProjectFileService? ProjectFile => App.ServiceProvider?.GetService<IProjectFileService>();

        private static readonly FilePickerFileType OngenFileType =
            new("Ongenet project") { Patterns = new[] { "*.ongen" } };

        private void OnNew_Click(object? sender, RoutedEventArgs e) => _ = NewAsync();
        private void OnOpen_Click(object? sender, RoutedEventArgs e) => _ = OpenAsync();
        private void OnSave_Click(object? sender, RoutedEventArgs e) => _ = SaveAsync();
        private void OnSaveAs_Click(object? sender, RoutedEventArgs e) => _ = SaveAsAsync();
        private void OnUndo_Click(object? sender, RoutedEventArgs e) => History?.Undo();
        private void OnRedo_Click(object? sender, RoutedEventArgs e) => History?.Redo();

        private async Task NewAsync()
        {
            if (ProjectFile is not { } pf) return;
            if (!await ConfirmDiscardAsync(pf)) return;
            pf.NewProject();
            History?.Clear(); // undo history doesn't carry across projects
        }

        private async Task OpenAsync()
        {
            if (ProjectFile is not { } pf) return;
            if (!await ConfirmDiscardAsync(pf)) return;

            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open project",
                AllowMultiple = false,
                FileTypeFilter = new[] { OngenFileType }
            });

            var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var result = await pf.LoadAsync(path);
                History?.Clear(); // start fresh history for the opened project
                if (result.Warnings.Count > 0)
                    await MessageDialog.Notify(this, "Project opened with warnings",
                        string.Join("\n", result.Warnings));
            }
            catch (Exception ex)
            {
                await MessageDialog.Notify(this, "Couldn't open project", ex.Message);
            }
        }

        private async Task SaveAsync()
        {
            if (ProjectFile is not { } pf) return;
            if (pf.CurrentPath is null) { await SaveAsAsync(); return; }

            if (pf.OpenedFromNewerVersion && !await MessageDialog.Confirm(this, "Overwrite newer project?",
                    "This project was created by a newer version of Ongenet. Saving now may discard data " +
                    "this version couldn't read. Continue?", "Save anyway"))
                return;

            await DoSaveAsync(pf, pf.CurrentPath);
        }

        private async Task SaveAsAsync()
        {
            if (ProjectFile is not { } pf) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save project as",
                SuggestedFileName = pf.DisplayName + ".ongen",
                DefaultExtension = "ongen",
                FileTypeChoices = new[] { OngenFileType }
            });

            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) await DoSaveAsync(pf, path);
        }

        private async Task DoSaveAsync(IProjectFileService pf, string path)
        {
            try { await pf.SaveAsync(path); }
            catch (Exception ex) { await MessageDialog.Notify(this, "Couldn't save project", ex.Message); }
        }

        private async Task<bool> ConfirmDiscardAsync(IProjectFileService pf)
        {
            if (!pf.IsDirty) return true;
            return await MessageDialog.Confirm(this, "Discard changes?",
                "You have unsaved changes that will be lost. Continue?", "Discard", "Cancel");
        }
    }
}
