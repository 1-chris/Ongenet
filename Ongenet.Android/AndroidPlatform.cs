using System;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Platform;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.App.Views;
using Ongenet.Audio.Native.Android;
using Ongenet.Android.Services;

namespace Ongenet.Android;

/// <summary>
/// Android host integration: contributes the AAudio native backend and Android-safe service stubs (these
/// are registered after the shared defaults, so they win), and shows the shared single-view
/// <see cref="MainView"/> — the same in-canvas shell the browser head uses, under Android's single-view
/// lifetime. No CLAP/LV2/VST: native desktop plugin formats don't exist on Android.
/// </summary>
public sealed class AndroidPlatform : IPlatformServices
{
    public void RegisterServices(IServiceCollection services)
    {
        // Audio backend: the OS-native stack for Android (AAudio), living in Ongenet.Audio next to the
        // desktop backends.
        services.AddSingleton<IAudioBackend, AndroidNativeBackend>();

        // Override the filesystem/native-backed shared defaults with sandbox-safe versions, mirroring the
        // browser head. Real persistence / scoped-storage indexing / MIDI input are follow-ups.
        services.AddSingleton<IMidiInputService, AndroidMidiInputService>();
        services.AddSingleton<IAppSettingsService, AndroidAppSettingsService>();
        services.AddSingleton<ILibraryScanService, AndroidLibraryScanService>();
        services.AddSingleton<IPresetLibrary, AndroidPresetLibrary>();
    }

    public object CreateShell(IServiceProvider services) => new MainView
    {
        DataContext = services.GetRequiredService<MainViewModel>()
    };

    public void OnStarted(IServiceProvider services) { /* no background plugin scan on Android */ }
}
