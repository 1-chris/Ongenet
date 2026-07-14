using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Input;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.App.Views.Windows;
// Alias the shared Application type so the bare name `App` doesn't bind to the `Ongenet.App` namespace.
using SharedApp = Ongenet.App.App;

namespace Ongenet.App.Views;

/// <summary>
/// The shared single-view root view — the in-canvas counterpart of the desktop MainWindow, used by the
/// browser and Android heads. Hosts the same panel views bound to the same <see cref="MainViewModel"/>,
/// with vertical side tabs, centre Arrangement/Mixer/Session/Notation tabs, and stream-based project I/O.
/// </summary>
public partial class MainView : UserControl
{
    // Which physical keys are currently held → which MIDI notes are sounding (so key auto-repeat
    // doesn't retrigger).
    private readonly Dictionary<Key, int> _heldKeys = new();

    private GridLength _leftSaved = new(240);
    private GridLength _rightSaved = new(300);
    private bool _leftCollapsed, _rightCollapsed;

    private static readonly FilePickerFileType OngenFileType =
        new("Ongenet project") { Patterns = ["*.ongen"] };

    public MainView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel);
        // Clicking a tab on a collapsed panel expands it (and selects that tab).
        LeftTabStrip.AddHandler(PointerPressedEvent, OnLeftTabsPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        RightTabStrip.AddHandler(PointerPressedEvent, OnRightTabsPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Start with the library sidebar collapsed to its sideways tab strip (matches desktop).
        SetRightCollapsed(true);
    }

    private ColumnDefinition LeftCol => WorkspaceGrid.ColumnDefinitions[0];
    private ColumnDefinition LeftSplitterCol => WorkspaceGrid.ColumnDefinitions[1];
    private ColumnDefinition RightSplitterCol => WorkspaceGrid.ColumnDefinitions[3];
    private ColumnDefinition RightCol => WorkspaceGrid.ColumnDefinitions[4];

    private static IPreviewService? Preview => SharedApp.ServiceProvider?.GetService<IPreviewService>();
    private static ISelectionService? Selection => SharedApp.ServiceProvider?.GetService<ISelectionService>();
    private static IProjectFileService? ProjectFile => SharedApp.ServiceProvider?.GetService<IProjectFileService>();
    private static IHistoryService? History => SharedApp.ServiceProvider?.GetService<IHistoryService>();

    private void ToggleLeftPanel(object? sender, RoutedEventArgs e) => SetLeftCollapsed(!_leftCollapsed);

    private void SetLeftCollapsed(bool collapsed)
    {
        if (collapsed == _leftCollapsed) return;
        _leftCollapsed = collapsed;
        LeftContent.IsVisible = !collapsed;
        LeftSplitter.IsVisible = !collapsed;
        LeftSplitterCol.Width = new GridLength(collapsed ? 0 : 4);
        if (collapsed)
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

    private void OnLeftTabsPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_leftCollapsed) SetLeftCollapsed(false);
    }

    private void ToggleRightPanel(object? sender, RoutedEventArgs e) => SetRightCollapsed(!_rightCollapsed);

    private void SetRightCollapsed(bool collapsed)
    {
        if (collapsed == _rightCollapsed) return;
        _rightCollapsed = collapsed;
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

    private void OnRightTabsPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_rightCollapsed) SetRightCollapsed(false);
    }

    private void OnUndo_Click(object? sender, RoutedEventArgs e) => History?.Undo();
    private void OnRedo_Click(object? sender, RoutedEventArgs e) => History?.Redo();

    private void OnNew_Click(object? sender, RoutedEventArgs e) => _ = NewAsync();
    private void OnOpen_Click(object? sender, RoutedEventArgs e) => _ = OpenAsync();
    private void OnSave_Click(object? sender, RoutedEventArgs e) => _ = SaveAsync();

    private async Task NewAsync()
    {
        if (ProjectFile is not { } pf) return;
        if (!await ConfirmDiscardAsync(pf)) return;
        pf.NewProject();
        History?.Clear();
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
            FileTypeFilter = [OngenFileType]
        });
        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            var result = await pf.LoadAsync(stream, files[0].Name);
            History?.Clear();
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

        if (pf.OpenedFromNewerVersion && !await MessageDialog.Confirm(this, "Overwrite newer project?",
                "This project was created by a newer version of Ongenet. Saving now may discard data " +
                "this version couldn't read. Continue?", "Save anyway"))
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        // Browser (and Android content URIs) have no durable path — always use the save picker / download.
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save project as",
            SuggestedFileName = pf.DisplayName + ".ongen",
            DefaultExtension = "ongen",
            FileTypeChoices = [OngenFileType]
        });
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await pf.SaveAsync(stream, file.Name);
        }
        catch (Exception ex)
        {
            await MessageDialog.Notify(this, "Couldn't save project", ex.Message);
        }
    }

    private async Task<bool> ConfirmDiscardAsync(IProjectFileService pf)
    {
        if (!pf.IsDirty) return true;
        return await MessageDialog.Confirm(this, "Discard changes?",
            "You have unsaved changes that will be lost. Continue?", "Discard", "Cancel");
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return; // don't steal typing from text inputs

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            switch (e.Key)
            {
                case Key.S:
                    _ = SaveAsync();
                    e.Handled = true;
                    return;
                case Key.O:
                    _ = OpenAsync();
                    e.Handled = true;
                    return;
                case Key.N:
                    _ = NewAsync();
                    e.Handled = true;
                    return;
                case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    History?.Redo();
                    e.Handled = true;
                    return;
                case Key.Z:
                    History?.Undo();
                    e.Handled = true;
                    return;
                case Key.Y:
                    History?.Redo();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Space)
        {
            TogglePlayStop();
            e.Handled = true;
            return;
        }

        // FL-style typing-keyboard note input — only on an unmodified key.
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (Selection?.SelectedTrack is not { Kind: Core.Models.Audio.TrackKind.Instrument }) return;
        if (!ComputerKeyboard.TryGetNote(e.Key, out var note)) return;
        if (_heldKeys.ContainsKey(e.Key)) { e.Handled = true; return; }

        _heldKeys[e.Key] = note;
        Preview?.NoteOn(note);
        e.Handled = true;
    }

    private void OnGlobalKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_heldKeys.Remove(e.Key, out var note)) return;
        Preview?.NoteOff(note);
        e.Handled = true;
    }

    private void TogglePlayStop()
    {
        if (DataContext is not MainViewModel vm) return;
        var transport = vm.Transport;
        if (transport.IsPlaying || transport.IsRecording) transport.StopCommand.Execute(null);
        else if (transport.CanPlay) transport.PlayCommand.Execute(null);
    }
}
