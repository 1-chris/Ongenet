using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Input;
using Ongenet.App.ViewModels;
// Alias the shared Application type so the bare name `App` doesn't bind to the `Ongenet.App` namespace.
using SharedApp = Ongenet.App.App;

namespace Ongenet.Web.Views;

/// <summary>
/// The in-canvas root view for the browser build — the single-view counterpart of the desktop
/// MainWindow. It hosts the same panel views bound to the same <see cref="MainViewModel"/>, minus the
/// OS window chrome and the secondary tool windows (which the browser can't show). It keeps the handy
/// transport + typing-keyboard shortcuts.
/// </summary>
public partial class MainView : UserControl
{
    // Which physical keys are currently held → which MIDI notes are sounding (so key auto-repeat
    // doesn't retrigger).
    private readonly Dictionary<Key, int> _heldKeys = new();

    public MainView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private static IPreviewService? Preview => SharedApp.ServiceProvider?.GetService<IPreviewService>();
    private static ISelectionService? Selection => SharedApp.ServiceProvider?.GetService<ISelectionService>();

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return; // don't steal typing from text inputs

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
