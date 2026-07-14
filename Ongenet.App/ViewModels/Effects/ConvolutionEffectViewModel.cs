using System;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.Effects;

/// <summary>Convolution reverb card with impulse loader UI (mirrors sample-load on samplers).</summary>
public sealed class ConvolutionEffectViewModel : EffectViewModel
{
    private readonly IHistoryService? _history;

    public ConvolutionEffectViewModel(ConvolutionEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
        _history = App.ServiceProvider?.GetService<IHistoryService>();
    }

    public ConvolutionEffect Convolution => (ConvolutionEffect)Effect;

    public string ImpulseName => Convolution.ImpulseName ?? "(synthetic IR)";

    public void LoadImpulseFromPath(string path)
    {
        var files = App.ServiceProvider?.GetService<IAudioFileService>();
        if (files is null) return;
        var loaded = files.Load(path);
        if (loaded is null) return;
        _history?.Capture("Load impulse");
        Convolution.LoadImpulse(loaded.Samples, System.IO.Path.GetFileName(path));
        OnPropertyChanged(nameof(ImpulseName));
    }
}
