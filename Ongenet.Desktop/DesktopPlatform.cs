using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ongenet.Ara;
using Ongenet.Au;
using Ongenet.Audio;
using Ongenet.Clap;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Core.Services.Implementation;
using Ongenet.App.Platform;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.App.Views.Windows;
using Ongenet.Desktop.Services;
using Ongenet.Engine3D;
using Ongenet.Engine3D.Abstractions;
using Ongenet.Link;
using Ongenet.Lv2;
using Ongenet.App.Theming;
using Ongenet.Scripting;
using Ongenet.Scripting.Export;
using Ongenet.Scripting.Editor;
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
    private ProjectAutosaveService? _autosave;

    public void RegisterServices(IServiceCollection services)
    {
        // Audio backend: the OS-native stack for this platform (ALSA/PipeWire/JACK/Pulse on Linux,
        // CoreAudio on macOS, WASAPI on Windows).
        if (OperatingSystem.IsLinux())
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.LinuxNativeBackend>();
        else if (OperatingSystem.IsMacOS())
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.Mac.MacNativeBackend>();
        else if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.Win.WinNativeBackend>();
            services.AddSingleton<IAudioBackend, Ongenet.Audio.Native.Win.AsioNativeBackend>();
        }

        // External MIDI controller input (ALSA / winmm / CoreMIDI).
        services.AddSingleton<IMidiInputService, MidiInputService>();
        services.AddSingleton<IMidiOutputService, MidiOutputService>();
        services.AddSingleton<MidiClockOutputService>();

        // Transport-bar process CPU/RAM indicators (overrides the shared null default).
        services.AddSingleton<ISystemMetricsSampler, ProcessSystemMetricsSampler>();

        // Ableton Link tempo/phase sync (GPL — isolated in Ongenet.Link; native when libabl-link is present).
        services.AddSingleton<ILinkSession>(sp =>
        {
            var transport = sp.GetRequiredService<ITransportService>();
            return LinkSessionFactory.Create(transport.Tempo.BeatsPerMinute);
        });

        // Celemony ARA2 hosting seam (stub without ENABLE_ARA + SDK).
        services.AddSingleton<IAraHost, AraHost>();

        // Roslyn user scripting (replaces NullScriptingHost from the shared App composition root).
        services.AddSingleton<IProjectScriptExporter, ProjectScriptExporter>();
        services.AddSingleton<IPresetScriptExporter, PresetScriptExporter>();
        services.AddSingleton<ScriptingApi>();
        services.AddSingleton<Core.Services.IScriptingApi>(sp => sp.GetRequiredService<ScriptingApi>());
        services.AddSingleton<Core.Services.IScriptingHost, RoslynScriptingHost>();
        services.AddSingleton<IScriptEditorFactory, ScriptEditorFactory>();
        services.AddSingleton<IVideoCompositionEditorFactory, VideoCompositionEditorFactory>();
        Ongenet.VideoComposition.DependencyInjection.VideoCompositionServiceCollectionExtensions.AddVideoComposition(services);
        services.AddSingleton<IVideoEngine3DRenderService, Ongenet.App.Services.VideoEngine3DRenderService>();
        services.AddSingleton<IVideoEngine3DLayerRenderer, Ongenet.App.Services.VideoEngine3DLayerRenderer>();

        // Plugin crash isolation bridge (scaffold — out-of-process host when enabled).
        services.AddSingleton<Core.Services.IPluginProcessHost, OutOfProcessPluginHost>();

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
            var pluginHost = sp.GetRequiredService<Core.Services.IPluginProcessHost>();
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("Vst");
            return new VstPluginProvider(instruments, effects, pluginHost,
                msg => logger?.LogInformation("{Message}", msg));
        });

        // Apple Audio Unit hosting (macOS only): scans the Component Manager and registers music
        // devices as instruments and effects as effects.
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton(sp =>
            {
                var instruments = sp.GetRequiredService<IInstrumentRegistry>();
                var effects = sp.GetRequiredService<IEffectRegistry>();
                var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("Au");
                return new AuPluginProvider(instruments, effects, msg => logger?.LogInformation("{Message}", msg));
            });
        }
    }

    public object CreateShell(IServiceProvider services) => new MainWindow
    {
        DataContext = services.GetRequiredService<MainViewModel>()
    };

    public void OnStarted(IServiceProvider services)
    {
        services.GetRequiredService<ISystemMetricsSampler>().Start();
        _ = services.GetRequiredService<ControlSurfaceService>();
        _ = services.GetRequiredService<MidiClockOutputService>();
        _ = services.GetRequiredService<Core.Services.TimecodeSyncService>();
        services.GetRequiredService<OscControlService>().Start();

        ThemePalette.Changed += ScriptEditorTheme.NotifyThemeChanged;

        // Verify out-of-process plugin host when isolation is enabled.
        _ = services.GetRequiredService<Core.Services.IPluginProcessHost>().TryLaunchPluginHostAsync();

        var settings = services.GetRequiredService<IAppSettingsService>();
        var files = services.GetRequiredService<IProjectFileService>();
        _autosave = new ProjectAutosaveService(
            files,
            () => settings.Current.AutosaveEnabled,
            () => TimeSpan.FromMinutes(Math.Max(1, settings.Current.AutosaveIntervalMinutes)));

        // Wire VST3 ARA factory discovery into the ARA seam (avoids Ongenet.Ara → Ongenet.Vst reference).
        Vst3AraDiscoveryImpl.Vst3AraScanner = Vst3Module.ReadAraFactoryNames;

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

        // Apple Audio Units (macOS only).
        if (OperatingSystem.IsMacOS())
        {
            var auLogger = services.GetService<ILoggerFactory>()?.CreateLogger("Au");
            AuPluginBase.Log = msg => auLogger?.LogInformation("{Message}", msg);
            services.GetRequiredService<AuPluginProvider>().ScanAsync();
        }
    }
}
