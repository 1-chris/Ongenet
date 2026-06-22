using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ongenet.Audio;
using Ongenet.Clap;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Platform;
using Ongenet.App.ViewModels;
using Ongenet.App.Views.Windows;
using Ongenet.Desktop.Services;
using Ongenet.Lv2;

namespace Ongenet.Desktop;

/// <summary>
/// Desktop host integration: contributes the native audio backends (PortAudio + the OS-native stack),
/// the platform MIDI input service, and CLAP/LV2 plugin hosting, and shows the classic
/// <see cref="MainWindow"/>. This is the only place the shared UI is tied to the native projects
/// (Ongenet.Audio / Ongenet.Clap / Ongenet.Lv2).
/// </summary>
public sealed class DesktopPlatform : IPlatformServices
{
    public void RegisterServices(IServiceCollection services)
    {
        // Audio backends: PortAudio everywhere, plus the OS-native backend where one exists.
        services.AddSingleton<IAudioBackend, PortAudioBackend>();
        if (OperatingSystem.IsLinux())
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.LinuxNativeBackend>();
        else if (OperatingSystem.IsMacOS())
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.Mac.MacNativeBackend>();
        else if (OperatingSystem.IsWindows())
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.Win.WinNativeBackend>();

        // External MIDI controller input (ALSA / winmm / CoreMIDI).
        services.AddSingleton<IMidiInputService, MidiInputService>();

        // CLAP plugin hosting: scans for installed plugins and registers them as instruments + effects.
        services.AddSingleton(sp =>
        {
            var instruments = sp.GetRequiredService<IInstrumentRegistry>();
            var effects = sp.GetRequiredService<IEffectRegistry>();
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("Clap");
            return new ClapPluginProvider(instruments, effects, msg => logger?.LogInformation("{Message}", msg));
        });

        // LV2 plugin hosting: scans installed *.lv2 bundles and registers them as instruments + effects.
        services.AddSingleton(sp =>
        {
            var instruments = sp.GetRequiredService<IInstrumentRegistry>();
            var effects = sp.GetRequiredService<IEffectRegistry>();
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("Lv2");
            return new Lv2PluginProvider(instruments, effects, msg => logger?.LogInformation("{Message}", msg));
        });
    }

    public object CreateShell(IServiceProvider services) => new MainWindow
    {
        DataContext = services.GetRequiredService<MainViewModel>()
    };

    public void OnStarted(IServiceProvider services)
    {
        // Route CLAP host/plugin diagnostics (incl. GUI open steps) to the in-app log, then scan in the
        // background; plugins appear in the Instruments tab + effects menu as they are found.
        var clapLogger = services.GetService<ILoggerFactory>()?.CreateLogger("Clap");
        ClapInstrument.Log = msg => clapLogger?.LogInformation("{Message}", msg);
        services.GetRequiredService<ClapPluginProvider>().ScanAsync();

        // Same for LV2.
        var lv2Logger = services.GetService<ILoggerFactory>()?.CreateLogger("Lv2");
        Lv2PluginBase.Log = msg => lv2Logger?.LogInformation("{Message}", msg);
        services.GetRequiredService<Lv2PluginProvider>().ScanAsync();
    }
}
