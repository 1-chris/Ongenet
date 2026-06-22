using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Platform;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.Web.Audio;
using Ongenet.Web.Services;
using Ongenet.Web.Views;

namespace Ongenet.Web;

/// <summary>
/// Browser host integration: contributes the Web Audio backend and browser-safe service stubs (these are
/// registered after the shared defaults, so they win), and shows the in-canvas <see cref="MainView"/>
/// instead of a desktop window. No CLAP/LV2 — native plugins cannot run in the browser.
/// </summary>
public sealed class WebPlatform : IPlatformServices
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IAudioBackend, WebAudioBackend>();

        // Override the filesystem/native-backed shared defaults with sandbox-safe versions.
        services.AddSingleton<IMidiInputService, BrowserMidiInputService>();
        services.AddSingleton<IAppSettingsService, BrowserAppSettingsService>();
        services.AddSingleton<ILibraryScanService, BrowserLibraryScanService>();
        services.AddSingleton<IPresetLibrary, BrowserPresetLibrary>();
    }

    public object CreateShell(IServiceProvider services) => new MainView
    {
        DataContext = services.GetRequiredService<MainViewModel>()
    };

    public void OnStarted(IServiceProvider services) { /* no background plugin scan in the browser */ }
}
