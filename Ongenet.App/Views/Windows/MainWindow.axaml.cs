using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Music;
using Avalonia.Media;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Localization;
using Ongenet.App.Input;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.App.Views.Windows;

namespace Ongenet.App.Views.Windows
{
    /// <summary>
    /// The application's main window. Blank-slate content with the original custom Catppuccin title bar.
    /// </summary>
    public partial class MainWindow : ChromedWindow
    {
        private LogWindow? _logWindow;
        private SettingsWindow? _settingsWindow;
        private HistoryWindow? _historyWindow;
        private GuideWindow? _guideWindow;
        private TempoMapWindow? _tempoMapWindow;
        private SectionPlaylistWindow? _sectionPlaylistWindow;
        private ChordTrackWindow? _chordTrackWindow;
        private ExpressionMapWindow? _expressionMapWindow;

        // FL-Studio-style typing-keyboard note input: tracks which physical keys are currently
        // held (→ which MIDI notes are sounding) so auto-repeat KeyDowns don't re-trigger.
        private readonly Dictionary<Key, int> _heldKeys = new();

        public MainWindow()
        {
            InitializeComponent();
            if (App.ServiceProvider?.GetService<IAudioEditorService>() is AudioEditorService audioEditor)
                audioEditor.SetOwner(this);
            AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel);
            // Clicking a tab on a collapsed panel expands it (and selects that tab). Tunnel + handledEventsToo
            // so we run before the TabItem consumes the press.
            BottomTabs.AddHandler(PointerPressedEvent, OnBottomTabsPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            RightTabStrip.AddHandler(PointerPressedEvent, OnRightTabsPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            // Start with the Files/Instruments sidebar collapsed to its sideways tab strip.
            SetRightCollapsed(true);
            Closing += OnClosing;
        }

        // Auto-enable the renderer diagnostics overlay when ONGENET_FPS=1, so jank can be measured
        // without touching the keyboard. Toggle at runtime with F8.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            PopulateBuiltInNewMenu();
            RestoreWindowLayout();
            if (Environment.GetEnvironmentVariable("ONGENET_FPS") == "1") ToggleRenderDiagnostics();
            _ = TryOfferRecoveryAsync();
        }

        private async Task TryOfferRecoveryAsync()
        {
            if (ProjectFile is not { } pf) return;
            var candidates = ProjectAutosaveService.ScanForRecovery(pf.CurrentPath);
            if (candidates.Count == 0) return;

            var newest = candidates[0];
            var kind = newest.IsAutosave
                ? Loc.Get("Dialog_RecoverKind_Autosave")
                : Loc.Get("Dialog_RecoverKind_Incomplete");
            var dialog = new Window
            {
                Title = Loc.Get("Dialog_RecoverProject_Title"),
                Width = 420,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            Text = Loc.Format("Dialog_RecoverProject_Message", kind,
                                newest.TimestampUtc.ToLocalTime().ToString("g"))
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Button { Content = Loc.Get("Dialog_Dismiss"), Tag = false },
                                new Button { Content = Loc.Get("Dialog_Recover"), Tag = true }
                            }
                        }
                    }
                }
            };
            if (dialog.Content is StackPanel root &&
                root.Children.LastOrDefault() is StackPanel buttons)
            {
                foreach (var child in buttons.Children.OfType<Button>())
                    child.Click += (_, _) => { dialog.Tag = child.Tag; dialog.Close(); };
            }

            await dialog.ShowDialog(this);
            if (dialog.Tag is true)
                await pf.LoadAsync(newest.Path);
        }

        private void PopulateBuiltInNewMenu()
        {
            var flyout = this.FindControl<Button>("NewButton")?.Flyout as MenuFlyout;
            if (flyout is null) return;
            flyout.Items.Add(new Separator());
            foreach (var info in BuiltInProjects.All)
            {
                var item = new MenuItem { Header = info.Name, Tag = info };
                item.Click += OnNewBuiltIn_Click;
                flyout.Items.Add(item);
            }
        }

        private async void OnNewBuiltIn_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: BuiltInProjectInfo info }) return;
            if (ProjectFile is not { } pf) return;
            if (!await ConfirmDiscardAsync(pf)) return;

            var instruments = App.ServiceProvider?.GetService<IInstrumentRegistry>();
            if (instruments is null) return;
            try
            {
                pf.LoadProject(info.Create(instruments));
                History?.Clear();
            }
            catch (Exception ex)
            {
                await MessageDialog.Notify(this, "Couldn't open built-in project", ex.Message);
            }
        }

        private void ToggleRenderDiagnostics()
        {
            var on = RendererDiagnostics.DebugOverlays != Avalonia.Rendering.RendererDebugOverlays.None;
            RendererDiagnostics.DebugOverlays = on
                ? Avalonia.Rendering.RendererDebugOverlays.None
                : Avalonia.Rendering.RendererDebugOverlays.Fps
                  | Avalonia.Rendering.RendererDebugOverlays.RenderTimeGraph
                  | Avalonia.Rendering.RendererDebugOverlays.LayoutTimeGraph;
        }

        // Don't let the app exit while a save is still writing — that would truncate the file.
        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (ProjectFile?.IsBusy == true)
            {
                e.Cancel = true;
                _ = MessageDialog.Notify(this, "Please wait",
                    "A save is still in progress. Try closing again once it finishes.");
                return;
            }

            SaveWindowLayout();
        }

        // --- Collapsible panels (bottom / left / right). Each remembers its pre-collapse size. ---

        private GridLength _bottomSaved = new(432);
        private GridLength _leftSaved = new(240);
        private GridLength _rightSaved = new(260);
        private bool _bottomCollapsed, _leftCollapsed, _rightCollapsed;

        // Named ColumnDefinition/RowDefinition don't generate code-behind fields, so reach them via the grid.
        private RowDefinition BottomSplitterRow => WorkspaceGrid.RowDefinitions[1];
        private RowDefinition BottomRow => WorkspaceGrid.RowDefinitions[2];
        private ColumnDefinition LeftCol => WorkspaceGrid.ColumnDefinitions[0];
        private ColumnDefinition LeftSplitterCol => WorkspaceGrid.ColumnDefinitions[1];
        private ColumnDefinition RightSplitterCol => WorkspaceGrid.ColumnDefinitions[3];
        private ColumnDefinition RightCol => WorkspaceGrid.ColumnDefinitions[4];

        private void ToggleBottomPanel(object? sender, RoutedEventArgs e) => SetBottomCollapsed(!_bottomCollapsed);

        private void SetBottomCollapsed(bool collapsed)
        {
            if (collapsed == _bottomCollapsed) return;
            _bottomCollapsed = collapsed;
            // Hiding the tab contents lets the Auto-sized row shrink to just the tab strip.
            BottomFirstContent.IsVisible = BottomPianoContent.IsVisible =
                BottomEffectsContent.IsVisible = !collapsed;
            BottomSplitter.IsVisible = !collapsed;
            BottomSplitterRow.Height = new GridLength(collapsed ? 0 : 4);
            if (collapsed)
            {
                _bottomSaved = BottomRow.Height;
                BottomRow.Height = GridLength.Auto;
                BottomToggle.Content = "▴";
            }
            else
            {
                BottomRow.Height = _bottomSaved;
                BottomToggle.Content = "▾";
            }
        }

        // Clicking a tab on the collapsed bottom strip expands the panel (the click also selects the tab).
        private void OnBottomTabsPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_bottomCollapsed) SetBottomCollapsed(false);
        }

        private void ToggleLeftPanel(object? sender, RoutedEventArgs e)
        {
            _leftCollapsed = !_leftCollapsed;
            LeftContent.IsVisible = !_leftCollapsed;
            LeftSplitter.IsVisible = !_leftCollapsed;
            LeftSplitterCol.Width = new GridLength(_leftCollapsed ? 0 : 4);
            if (_leftCollapsed)
            {
                _leftSaved = LeftCol.Width;
                LeftCol.Width = GridLength.Auto;
                LeftToggle.Content = "▸";
            }
            else
            {
                LeftCol.Width = _leftSaved;
                LeftToggle.Content = "◂";
            }
        }

        private void ToggleRightPanel(object? sender, RoutedEventArgs e) => SetRightCollapsed(!_rightCollapsed);

        private void SetRightCollapsed(bool collapsed)
        {
            if (collapsed == _rightCollapsed) return;
            _rightCollapsed = collapsed;
            // Hide the content panel so the Auto-sized column shrinks to just the sideways tab strip.
            RightContent.IsVisible = !collapsed;
            RightSplitter.IsVisible = !collapsed;
            RightSplitterCol.Width = new GridLength(collapsed ? 0 : 4);
            if (collapsed)
            {
                _rightSaved = RightCol.Width;
                RightCol.Width = GridLength.Auto;
                RightToggle.Content = "◂";
            }
            else
            {
                RightCol.Width = _rightSaved;
                RightToggle.Content = "▸";
            }
        }

        // Clicking a sideways tab on the collapsed right strip expands the panel (the click also selects it).
        private void OnRightTabsPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_rightCollapsed) SetRightCollapsed(false);
        }

        private IPreviewService? Preview => App.ServiceProvider?.GetService<IPreviewService>();
        private ISelectionService? Selection => App.ServiceProvider?.GetService<ISelectionService>();
        private ITransportService? Transport => App.ServiceProvider?.GetService<ITransportService>();
        private Services.IHistoryService? History => App.ServiceProvider?.GetService<Services.IHistoryService>();
        private Services.KeyboardShortcutService? Shortcuts => App.ServiceProvider?.GetService<Services.KeyboardShortcutService>();

        private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
        {
            // Don't steal typing from text inputs (track rename, numeric fields, etc.).
            if (e.Source is TextBox) return;

            if (TryHandleAppShortcut(e)) return;

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
                case Key.F8:
                    // Toggle Avalonia's renderer diagnostics overlay (FPS + render/layout time graphs)
                    // to diagnose UI jank. Render graph high = render/GPU bound; layout graph high =
                    // layout bound; both low but FPS low = frame-scheduling/vsync bound.
                    ToggleRenderDiagnostics();
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
            if (Selection?.SelectedTrack is not { Kind: Core.Models.Audio.TrackKind.Instrument }) return;
            if (!ComputerKeyboard.TryGetNote(e.Key, out var note)) return;
            if (_heldKeys.ContainsKey(e.Key)) { e.Handled = true; return; }

            _heldKeys[e.Key] = note;
            Preview?.NoteOn(note);
            e.Handled = true;
        }

        private bool TryHandleAppShortcut(KeyEventArgs e)
        {
            if (Shortcuts is null) return false;
            if (DataContext is not MainViewModel vm) return false;

            if (Shortcuts.Matches(e, AppShortcutAction.RippleInsert))
            {
                vm.Timeline.RippleInsertCommand.Execute(null);
                e.Handled = true;
                return true;
            }
            if (Shortcuts.Matches(e, AppShortcutAction.RippleDelete))
            {
                vm.Timeline.RippleDeleteCommand.Execute(null);
                e.Handled = true;
                return true;
            }
            if (Shortcuts.Matches(e, AppShortcutAction.OpenTempoMap))
            {
                OpenTempoMap_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return true;
            }
            if (Shortcuts.Matches(e, AppShortcutAction.OpenSectionPlaylist))
            {
                OpenSectionPlaylist_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return true;
            }

            return false;
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

        private void OpenSettings_Click(object? sender, RoutedEventArgs e)
        {
            var viewModel = App.ServiceProvider?.GetService<SettingsViewModel>();
            if (viewModel is null) return;

            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.SetViewModel(viewModel);
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Show();
            }
            else
            {
                _settingsWindow.Activate();
            }
        }

        private void OpenGuide_Click(object? sender, RoutedEventArgs e)
        {
            if (_guideWindow is null)
            {
                _guideWindow = new GuideWindow();
                _guideWindow.Closed += (_, _) => _guideWindow = null;
                _guideWindow.Show(this);
            }
            else
            {
                _guideWindow.Activate();
            }
        }

        private void OpenHistory_Click(object? sender, RoutedEventArgs e)
        {
            var viewModel = App.ServiceProvider?.GetService<HistoryViewModel>();
            if (viewModel is null) return;

            if (_historyWindow is null)
            {
                _historyWindow = new HistoryWindow();
                _historyWindow.SetViewModel(viewModel);
                _historyWindow.Closed += (_, _) => _historyWindow = null;
                _historyWindow.Show();
            }
            else
            {
                _historyWindow.Activate();
            }
        }

        private void OpenTempoMap_Click(object? sender, RoutedEventArgs e)
        {
            if (_tempoMapWindow is not null) { _tempoMapWindow.Activate(); return; }
            var vm = App.ServiceProvider?.GetService<TempoMapViewModel>();
            if (vm is null) return;
            _tempoMapWindow = new TempoMapWindow { DataContext = vm };
            _tempoMapWindow.Closed += (_, _) => _tempoMapWindow = null;
            _tempoMapWindow.Show(this);
        }

        private void OpenSectionPlaylist_Click(object? sender, RoutedEventArgs e)
        {
            if (_sectionPlaylistWindow is not null) { _sectionPlaylistWindow.Activate(); return; }
            var vm = App.ServiceProvider?.GetService<SectionPlaylistViewModel>();
            if (vm is null) return;
            _sectionPlaylistWindow = new SectionPlaylistWindow { DataContext = vm };
            _sectionPlaylistWindow.Closed += (_, _) => _sectionPlaylistWindow = null;
            _sectionPlaylistWindow.Show(this);
        }

        private void OpenChordTrack_Click(object? sender, RoutedEventArgs e)
        {
            if (_chordTrackWindow is not null) { _chordTrackWindow.Activate(); return; }
            var vm = App.ServiceProvider?.GetService<ChordTrackViewModel>();
            if (vm is null) return;
            vm.Rebuild();
            _chordTrackWindow = new ChordTrackWindow { DataContext = vm };
            _chordTrackWindow.Closed += (_, _) => _chordTrackWindow = null;
            _chordTrackWindow.Show(this);
        }

        private void OpenExpressionMaps_Click(object? sender, RoutedEventArgs e)
        {
            if (_expressionMapWindow is not null) { _expressionMapWindow.Activate(); return; }
            var vm = App.ServiceProvider?.GetService<ExpressionMapViewModel>();
            if (vm is null) return;
            _expressionMapWindow = new ExpressionMapWindow { DataContext = vm };
            _expressionMapWindow.Closed += (_, _) => _expressionMapWindow = null;
            _expressionMapWindow.Show(this);
        }

        private void OpenAudioEditor_Click(object? sender, RoutedEventArgs e)
        {
            var editor = App.ServiceProvider?.GetService<Services.IAudioEditorService>();
            var selection = App.ServiceProvider?.GetService<ISelectionService>();
            if (editor is Services.AudioEditorService svc)
                svc.SetOwner(this);
            if (selection?.SelectedClip is { IsAudio: true } clip)
                editor?.OpenClip(clip);
            else
                editor?.Open();
        }

        // --- Window layout profiles ---

        private IAppSettingsService? AppSettings => App.ServiceProvider?.GetService<IAppSettingsService>();

        private void RestoreWindowLayout()
        {
            var settings = AppSettings?.Current;
            if (settings is null) return;

            var profile = settings.WindowLayouts.FirstOrDefault(l => l.Name == settings.ActiveWindowLayout)
                            ?? settings.WindowLayouts.FirstOrDefault();
            if (profile is null) return;

            if (profile.MainWindowMaximized)
                WindowState = WindowState.Maximized;
            else
            {
                Position = new PixelPoint((int)profile.MainWindowX, (int)profile.MainWindowY);
                Width = profile.MainWindowWidth > 100 ? profile.MainWindowWidth : Width;
                Height = profile.MainWindowHeight > 100 ? profile.MainWindowHeight : Height;
            }
        }

        private void SaveWindowLayout()
        {
            var svc = AppSettings;
            if (svc is null) return;

            var name = svc.Current.ActiveWindowLayout ?? "Default";
            var profile = svc.Current.WindowLayouts.FirstOrDefault(l => l.Name == name);
            if (profile is null)
            {
                profile = new WindowLayoutProfileDto { Name = name };
                svc.Current.WindowLayouts.Add(profile);
            }

            if (WindowState == WindowState.Maximized)
            {
                profile.MainWindowMaximized = true;
            }
            else
            {
                profile.MainWindowMaximized = false;
                profile.MainWindowX = Position.X;
                profile.MainWindowY = Position.Y;
                profile.MainWindowWidth = Width;
                profile.MainWindowHeight = Height;
            }

            svc.Current.ActiveWindowLayout = profile.Name;
            svc.CaptureAndSave();
        }

        private void OnSaveLayout_Click(object? sender, RoutedEventArgs e)
        {
            var svc = AppSettings;
            if (svc is null) return;
            var name = $"Layout {svc.Current.WindowLayouts.Count + 1}";
            var profile = new WindowLayoutProfileDto { Name = name };
            if (WindowState == WindowState.Maximized)
                profile.MainWindowMaximized = true;
            else
            {
                profile.MainWindowX = Position.X;
                profile.MainWindowY = Position.Y;
                profile.MainWindowWidth = Width;
                profile.MainWindowHeight = Height;
            }
            svc.Current.WindowLayouts.Add(profile);
            svc.Current.ActiveWindowLayout = name;
            svc.CaptureAndSave();
        }

        private void OnLoadLayout_Click(object? sender, RoutedEventArgs e)
        {
            var svc = AppSettings;
            if (svc is null || svc.Current.WindowLayouts.Count == 0) return;
            var profile = svc.Current.WindowLayouts[^1];
            svc.Current.ActiveWindowLayout = profile.Name;
            if (profile.MainWindowMaximized)
                WindowState = WindowState.Maximized;
            else
            {
                WindowState = WindowState.Normal;
                Position = new PixelPoint((int)profile.MainWindowX, (int)profile.MainWindowY);
                Width = profile.MainWindowWidth;
                Height = profile.MainWindowHeight;
            }
            svc.CaptureAndSave();
        }

        private async void OnPullCollaboration_Click(object? sender, RoutedEventArgs e)
        {
            var svc = AppSettings;
            var pf = ProjectFile;
            var project = App.ServiceProvider?.GetService<IProjectService>();
            if (svc is null || pf is null || project is null) return;

            var syncFolder = svc.Current.CollaborationSyncFolder;
            if (string.IsNullOrWhiteSpace(syncFolder))
            {
                var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Choose collaboration sync folder",
                    AllowMultiple = false
                });
                syncFolder = folder.FirstOrDefault()?.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(syncFolder)) return;
                svc.Current.CollaborationSyncFolder = syncFolder;
                svc.CaptureAndSave();
            }

            if (!CollaborationService.TryPullLatest(syncFolder, out var projectPath) || string.IsNullOrWhiteSpace(projectPath))
            {
                await MessageDialog.Notify(this, "Nothing to pull",
                    "No share manifest or project file was found in the collaboration folder.");
                return;
            }

            if (pf.IsDirty)
            {
                var manifest = CollaborationService.LoadManifest(syncFolder);
                var remoteTime = manifest?.ExportedUtc.ToLocalTime().ToString("g") ?? "unknown";
                var keepLocal = await MessageDialog.Confirm(this, "Collaboration conflict",
                    $"Your project has unsaved changes, but a newer copy exists in the sync folder (exported {remoteTime}).\n\n" +
                    "Discard local changes and load the shared copy?",
                    "Load shared copy", "Keep local");
                if (!keepLocal) return;
            }

            try
            {
                var result = await pf.LoadAsync(projectPath);
                History?.Clear();
                if (result.Warnings.Count > 0)
                    await MessageDialog.Notify(this, "Project pulled with warnings",
                        string.Join("\n", result.Warnings));
                else
                    await MessageDialog.Notify(this, "Pulled",
                        $"Loaded {System.IO.Path.GetFileName(projectPath)} from the collaboration folder.");
            }
            catch (Exception ex)
            {
                await MessageDialog.Notify(this, "Pull failed", ex.Message);
            }
        }

        private async void OnExportAudio_Click(object? sender, RoutedEventArgs e)
            => await ExportDialog.ShowAsync(this);

        private async void OnExportVideo_Click(object? sender, RoutedEventArgs e)
            => await ExportDialog.ShowForVideoAsync(this);

        private async void OnSyncCollaboration_Click(object? sender, RoutedEventArgs e)
        {
            var svc = AppSettings;
            var pf = ProjectFile;
            var project = App.ServiceProvider?.GetService<IProjectService>();
            if (svc is null || pf is null || project is null) return;

            var syncFolder = svc.Current.CollaborationSyncFolder;
            if (string.IsNullOrWhiteSpace(syncFolder))
            {
                var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Choose collaboration sync folder",
                    AllowMultiple = false
                });
                syncFolder = folder.FirstOrDefault()?.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(syncFolder)) return;
                svc.Current.CollaborationSyncFolder = syncFolder;
                svc.CaptureAndSave();
            }

            var path = pf.CurrentPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                await MessageDialog.Notify(this, "Save required",
                    "Save the project before syncing to the collaboration folder.");
                return;
            }

            try
            {
                CollaborationService.ExportShareManifest(project.Current, path, syncFolder);
                var versionPath = CollaborationService.PushVersion(path, syncFolder);
                var versions = CollaborationService.ListVersions(syncFolder);
                await MessageDialog.Notify(this, "Synced",
                    $"Project synced to {syncFolder}\nVersion snapshot: {System.IO.Path.GetFileName(versionPath)}\n({versions.Count} versions on file)");
            }
            catch (Exception ex)
            {
                await MessageDialog.Notify(this, "Sync failed", ex.Message);
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
