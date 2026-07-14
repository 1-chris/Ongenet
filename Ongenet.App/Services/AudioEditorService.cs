using System;
using System.Linq;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.ViewModels;
using Ongenet.App.Views.Windows;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;

namespace Ongenet.App.Services;

public sealed class AudioEditorService : IAudioEditorService
{
    private Window? _owner;
    private AudioEditorWindow? _window;

    public void SetOwner(Window owner) => _owner = owner;

    public void Open() => EnsureWindow();

    public void OpenClip(Clip clip)
    {
        if (!clip.IsAudio) return;
        var vm = EnsureWindow();
        vm.OpenClip(clip);
        _window!.Activate();
    }

    /// <summary>Returns the first sampler on an instrument track, or creates one when none exists.</summary>
    public static SamplerInstrument? FindTargetSampler(Project project)
    {
        foreach (var track in project.Tracks)
        {
            if (track.Kind != TrackKind.Instrument) continue;
            foreach (var slot in track.Instruments)
            {
                if (slot.Instrument is SamplerInstrument sampler)
                    return sampler;
            }
        }

        var created = new SamplerInstrument();
        var newTrack = new Track
        {
            Name = "Sampler",
            Kind = TrackKind.Instrument
        };
        newTrack.Instruments.Add(new InstrumentSlot(created));
        project.Tracks.Add(newTrack);
        return created;
    }

    private AudioEditorViewModel EnsureWindow()
    {
        if (_window?.DataContext is AudioEditorViewModel existing)
        {
            _window.Activate();
            return existing;
        }

        var viewModel = App.ServiceProvider?.GetRequiredService<AudioEditorViewModel>()
                        ?? throw new InvalidOperationException("AudioEditorViewModel not registered.");
        _window = new AudioEditorWindow { DataContext = viewModel };
        _window.Closed += (_, _) => _window = null;
        if (_owner is not null)
            _window.Show(_owner);
        else
            _window.Show();
        return viewModel;
    }
}
