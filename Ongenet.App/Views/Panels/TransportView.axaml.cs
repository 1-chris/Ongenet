using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Controls;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.App.Views.Windows;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Views.Panels
{
    /// <summary>Top-bar transport controls. Hosts the always-on UI heartbeat: pumps the shared
    /// <see cref="IPlaybackClock"/> so mixer meters, inspectors and the master meter keep updating
    /// even when the Arrangement tab (which also pumps during its own overlay work) is not visible.
    /// Stays on the shared idle timer — only the timeline switches to vsync cadence during playback.</summary>
    public partial class TransportView : UserControl
    {
        private readonly FrameTicker _ticker;
        private IPlaybackClock? _clock;

        public TransportView()
        {
            InitializeComponent();
            _clock = App.ServiceProvider?.GetService<IPlaybackClock>();
            if (_clock is not null)
                _clock.Tick += () => (DataContext as TransportViewModel)?.RefreshMeters();

            _ticker = new FrameTicker(this, OnTick);

            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        }

        private void OnTick() => _clock?.Pump();

        // Right-click the Tempo / Time editors → "Create automation track" on the master track, so tempo
        // and time signature automate through the same lane pipeline as any knob, fader or on/off switch.
        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;

            var editor = (e.Source as Visual)?.FindAncestorOfType<NumericUpDown>(includeSelf: true);
            if (editor is null) return;

            var project = App.ServiceProvider?.GetService<IProjectService>();
            var master = project?.Current.Master;
            if (project is null || master is null) return;

            if (editor.Name == "TempoEditor")
            {
                AutomationGesture.Offer(editor, master, AutomationGesture.ForTempo(project.Current));
                e.Handled = true;
            }
            else if (editor.Name == "TimeSigEditor")
            {
                AutomationGesture.Offer(editor, master, AutomationGesture.ForTimeSignature(project.Current));
                e.Handled = true;
            }
        }

        private static readonly FilePickerFileType MidiFileType =
            new("MIDI files") { Patterns = new[] { "*.mid", "*.midi" } };

        // Export all instrument-track notes to a single Standard MIDI File.
        private async void OnExportMidi(object? sender, RoutedEventArgs e)
        {
            var timeline = App.ServiceProvider?.GetService<TimelineViewModel>();
            var project = App.ServiceProvider?.GetService<IProjectService>();
            if (timeline is null || project is null) return;

            var (notes, length) = timeline.CollectProjectMidi();
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var owner = top as Window;

            if (notes.Count == 0)
            {
                if (owner is not null)
                    await MessageDialog.Notify(owner, "Nothing to export", "No MIDI notes on instrument tracks.");
                return;
            }

            var name = string.IsNullOrWhiteSpace(project.Current.Name) ? "project" : project.Current.Name;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export project MIDI",
                SuggestedFileName = $"{name}.mid",
                DefaultExtension = "mid",
                FileTypeChoices = new[] { MidiFileType }
            });
            if (file is null) return;

            try
            {
                await using var stream = await file.OpenWriteAsync();
                StandardMidiFile.Write(stream, notes, length,
                    project.Current.Tempo.BeatsPerMinute, project.Current.TimeSignature);
            }
            catch (Exception ex)
            {
                if (owner is not null)
                    await MessageDialog.Notify(owner, "Couldn't export MIDI", ex.Message);
            }
        }
    }
}
