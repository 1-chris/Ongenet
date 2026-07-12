using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.ViewModels;
using Ongenet.App.Views.Windows;
using Ongenet.Core.Models.Audio;

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
