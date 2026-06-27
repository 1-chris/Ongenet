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
using Ongenet.Engine3D;
using Ongenet.Engine3D.Abstractions;
using Ongenet.Lv2;
using Ongenet.Vst;
using Ongenet.Vst.Vst2;
using Ongenet.Vst.Vst3;

namespace Ongenet.Desktop;

/// <summary>
/// Desktop host integration: contributes the OS-native audio backend, the platform MIDI input service,
/// and CLAP/LV2/VST plugin hosting, and shows the classic <see cref="MainWindow"/>. This is the only
/// place the shared UI is tied to the native projects (Ongenet.Audio / Ongenet.Clap / Ongenet.Lv2 /
/// Ongenet.Vst).
/// </summary>
public sealed class DesktopPlatform : IPlatformServices
{
    public void RegisterServices(IServiceCollection services)
    {
        // Audio backend: the OS-native stack for this platform (ALSA/PipeWire/JACK/Pulse on Linux,
        // CoreAudio on macOS, WASAPI on Windows).
        if (OperatingSystem.IsLinux())
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.LinuxNativeBackend>();
        else if (OperatingSystem.IsMacOS())
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.Mac.MacNativeBackend>();
        else if (OperatingSystem.IsWindows())
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.Win.WinNativeBackend>();

        // External MIDI controller input (ALSA / winmm / CoreMIDI).
        services.AddSingleton<IMidiInputService, MidiInputService>();

        // GPU 3D engine for the embeddable 3D controls (Vulkan, natively on Windows/Linux and via MoltenVK
        // on macOS). It brings up the device lazily and reports IsAvailable=false instead of throwing if no
        // usable GPU is present, so 3D controls simply show a placeholder. Desktop-only: the shared UI
        // resolves it through the I3DEngineFactory seam and the Web/Android heads never register it.
        services.AddSingleton<I3DEngineFactory>(sp =>
            new VulkanEngineFactory(sp.GetService<ILoggerFactory>()?.CreateLogger("Engine3D")));

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

        // VST2 + VST3 plugin hosting: scans installed plugins and registers them as instruments + effects.
        services.AddSingleton(sp =>
        {
            var instruments = sp.GetRequiredService<IInstrumentRegistry>();
            var effects = sp.GetRequiredService<IEffectRegistry>();
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("Vst");
            return new VstPluginProvider(instruments, effects, msg => logger?.LogInformation("{Message}", msg));
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

        // Same for VST2 + VST3 (each format has its own static log sink). VST logs are also mirrored to
        // stderr so plugin-editor open steps interleave with the bridge's own console output (yabridge),
        // which makes diagnosing GUI-open hangs possible from a single terminal.
        var vstLogger = services.GetService<ILoggerFactory>()?.CreateLogger("Vst");
        Vst2PluginBase.Log = msg => { vstLogger?.LogInformation("{Message}", msg); Console.Error.WriteLine($"[Vst] {msg}"); };
        Vst3PluginBase.Log = msg => { vstLogger?.LogInformation("{Message}", msg); Console.Error.WriteLine($"[Vst] {msg}"); };
        services.GetRequiredService<VstPluginProvider>().ScanAsync();
    }
}
