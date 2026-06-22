using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ongenet.Core.Audio;
using Ongenet.Core.DependencyInjection;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Platform;
using Ongenet.App.ViewModels;

namespace Ongenet.App
{
    /// <summary>
    /// The shared application class. Owns the portable composition root (engine wiring, view-models,
    /// theming, automation, history) and defers everything host-specific to <see cref="Platform"/>:
    /// the audio backend(s), MIDI source, optional plugin hosts, and the shell to display. It supports
    /// both a classic-desktop lifetime (a top-level <see cref="Window"/>) and a single-view lifetime
    /// (an in-canvas <see cref="Control"/>, used by the browser head).
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider? ServiceProvider { get; private set; }

        /// <summary>
        /// The host integration. Each head sets this before starting Avalonia (it cannot be a constructor
        /// argument because Avalonia instantiates <see cref="App"/> itself).
        /// </summary>
        public static IPlatformServices? Platform { get; set; }

        /// <inheritdoc/>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <inheritdoc/>
        public override void OnFrameworkInitializationCompleted()
        {
            var services = new ServiceCollection();

            // Logging surfaced to the in-app Log window.
            var logProvider = new ObservableCollectionLoggerProvider();
            logProvider.SetDispatcher(action => Dispatcher.UIThread.Post(action));
            services.AddLogging(builder =>
            {
                builder.AddProvider(logProvider);
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            services.AddSingleton(logProvider);

            // Core services.
            services.AddOngenetCore();

            // The single switchable audio backend. The engine depends only on the IAudioOutput / IAudioInput
            // / IAudioDeviceService seams; the concrete IAudioBackend(s) are contributed by the platform
            // (native stacks on desktop, Web Audio in the browser). AudioBackendManager presents the active
            // one through those three seams, so the backend can be swapped without touching the engine.
            services.AddSingleton<AudioBackendManager>();
            services.AddSingleton<IAudioBackendManager>(sp => sp.GetRequiredService<AudioBackendManager>());
            services.AddSingleton<IAudioOutput>(sp => sp.GetRequiredService<AudioBackendManager>());
            services.AddSingleton<IAudioInput>(sp => sp.GetRequiredService<AudioBackendManager>());
            services.AddSingleton<IAudioDeviceService>(sp => sp.GetRequiredService<AudioBackendManager>());

            // UI-thread marshalling seam (lets Core services hand UI notifications back safely) and
            // MIDI mapping/transport routing. The MIDI *input source* itself is platform-specific.
            services.AddSingleton<IUiThreadDispatcher, Services.AvaloniaUiDispatcher>();
            services.AddSingleton<IMidiMappingService, Services.MidiMappingService>();
            services.AddSingleton<ITransportMapService, Services.TransportMapService>();

            // App-wide settings persisted to the per-user config file (device/theme/quantize/transport).
            services.AddSingleton<Services.IAppSettingsService, Services.AppSettingsService>();

            // Library: filesystem scan (samples/soundfonts) + preset aggregation (factory + user presets).
            services.AddSingleton<Services.ILibraryScanService, Services.LibraryScanService>();
            services.AddSingleton<Services.IPresetLibrary, Services.PresetLibrary>();

            // Parameter automation: creates lanes from the "Create automation track" right-click.
            services.AddSingleton<Services.IAutomationService, Services.AutomationService>();

            // Undo/redo history (project-snapshot based).
            services.AddSingleton<Services.IHistoryService, Services.HistoryService>();

            // ~30fps UI heartbeat so automated controls visibly move during playback.
            services.AddSingleton<Services.IPlaybackClock, Services.PlaybackClock>();

            // ViewModels. Panel view models are singletons: they share the one transport,
            // selection, and project for the lifetime of the single main window.
            services.AddSingleton<AudioDevicesViewModel>();
            services.AddSingleton<TransportViewModel>();
            services.AddSingleton<TimelineViewModel>();
            services.AddSingleton<TrackInspectorViewModel>();
            services.AddSingleton<ClipInspectorViewModel>();
            services.AddSingleton<InstrumentInspectorViewModel>();
            services.AddSingleton<SampleInspectorViewModel>();
            services.AddSingleton<PianoRollViewModel>();
            services.AddSingleton<EffectsViewModel>();
            services.AddSingleton<BottomPanelViewModel>();
            services.AddSingleton<FileBrowserViewModel>();

            // Library tabs + shared audio preview.
            services.AddSingleton<AudioPreviewViewModel>();
            services.AddSingleton<LibraryOptionsViewModel>();
            services.AddSingleton<ViewModels.Library.EverythingLibraryViewModel>();
            services.AddSingleton<ViewModels.Library.EffectsLibraryViewModel>();
            services.AddSingleton<ViewModels.Library.SampleLibraryViewModel>();
            services.AddSingleton<ViewModels.Library.SoundFontLibraryViewModel>();
            services.AddSingleton<ViewModels.Library.InstrumentLibraryViewModel>();
            services.AddSingleton<ViewModels.Library.InstrumentPresetLibraryViewModel>();
            services.AddSingleton<ViewModels.Library.EffectPresetLibraryViewModel>();
            services.AddSingleton<ViewModels.Library.EffectChainPresetLibraryViewModel>();

            services.AddSingleton<MainViewModel>();

            // Live theming (Catppuccin variants + custom themes).
            services.AddSingleton<Theming.IThemeService, Theming.ThemeService>();
            services.AddSingleton<ThemeEditorViewModel>();
            services.AddSingleton<HistoryViewModel>();

            // Unified settings window (Audio / MIDI / Theme tabs).
            services.AddSingleton<MidiSettingsViewModel>();
            services.AddSingleton<LibrarySettingsViewModel>();
            services.AddSingleton<SettingsViewModel>();

            // Platform-specific services last, so a head can override a shared default (e.g. a browser-safe
            // settings store) and contribute its audio backend(s), MIDI source and any plugin hosts.
            Platform?.RegisterServices(services);

            ServiceProvider = services.BuildServiceProvider();

            // The "Sampler" rebuilds itself from persisted state on project load; give it the loader
            // (persistence runs without DI, so it needs a static handle to the decoders).
            Core.Audio.Instruments.Sampler.SamplerInstrument.Loader =
                ServiceProvider.GetService<Core.Audio.Instruments.Sampler.ISamplerLoadService>();

            // Establish the font-size resources used across the app.
            ApplyFontScale(1.0);

            // Capture the palette brushes and apply the default theme.
            ServiceProvider.GetRequiredService<Theming.IThemeService>().Initialize();

            // Apply persisted preferences (theme, audio/MIDI device, input quantize, transport maps) over
            // the defaults, before the engine and MIDI input start, so they open on the saved devices.
            TryApplySettings();

            var engine = ServiceProvider.GetRequiredService<IAudioEngine>();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = (Window)Platform!.CreateShell(ServiceProvider);

                TryStartAudio(engine);
                var midi = TryStartMidi();
                Platform.OnStarted(ServiceProvider);

                desktop.ShutdownRequested += (_, _) =>
                {
                    midi?.Dispose();
                    engine.Dispose();
                    DisposeTrackPlugins();
                };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                singleView.MainView = (Control)Platform!.CreateShell(ServiceProvider);

                TryStartAudio(engine);
                TryStartMidi();
                Platform.OnStarted(ServiceProvider);
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>Disposes any plugin instruments + effects on the project's tracks (frees native modules).</summary>
        private static void DisposeTrackPlugins()
        {
            var project = ServiceProvider?.GetService<IProjectService>()?.Current;
            if (project is null) return;
            foreach (var track in project.Tracks)
            {
                foreach (var slot in track.Instruments)
                {
                    if (slot.Instrument is IDisposable inst) inst.Dispose();
                    foreach (var fx in slot.Effects)
                        if (fx is IDisposable d) d.Dispose();
                }

                foreach (var fx in track.Effects)
                    if (fx is IDisposable d) d.Dispose();
            }
        }

        /// <summary>
        /// Starts the audio engine, logging (rather than crashing) if no device is available —
        /// the rest of the app still works without sound.
        /// </summary>
        private static void TryStartAudio(IAudioEngine engine)
        {
            try
            {
                engine.Start();
            }
            catch (Exception ex)
            {
                var logger = ServiceProvider?.GetService<ILoggerFactory>()?.CreateLogger("Audio");
                logger?.LogError(ex, "Failed to start the audio engine; continuing without audio output.");
            }
        }

        /// <summary>Applies persisted app settings to the live services, logging on failure.</summary>
        private static void TryApplySettings()
        {
            try
            {
                ServiceProvider!.GetRequiredService<Services.IAppSettingsService>().ApplyToServices();
            }
            catch (Exception ex)
            {
                var logger = ServiceProvider?.GetService<ILoggerFactory>()?.CreateLogger("Settings");
                logger?.LogError(ex, "Failed to apply saved settings; continuing with defaults.");
            }
        }

        /// <summary>
        /// Resolves and starts MIDI input, logging (rather than crashing) on any backend failure.
        /// Returns the service so it can be disposed on shutdown, or null if it could not be created.
        /// </summary>
        private static IMidiInputService? TryStartMidi()
        {
            try
            {
                var midi = ServiceProvider!.GetRequiredService<IMidiInputService>();
                var logger = ServiceProvider!.GetService<ILoggerFactory>()?.CreateLogger("Midi");
                logger?.LogInformation(
                    midi.SelectedDevice is { } d
                        ? $"MIDI input: {midi.Devices.Count} device(s); listening on \"{d.DisplayName}\"."
                        : $"MIDI input: {midi.Devices.Count} device(s); none selected.");
                return midi;
            }
            catch (Exception ex)
            {
                var logger = ServiceProvider?.GetService<ILoggerFactory>()?.CreateLogger("Midi");
                logger?.LogError(ex, "Failed to initialise MIDI input; continuing without it.");
                return null;
            }
        }

        /// <summary>
        /// Applies the font scale to the application by modifying the ControlContentThemeFontSize resource.
        /// </summary>
        /// <param name="scale">The scale factor (1.0 = 100%, 0.5 = 50%, 2.0 = 200%, etc.)</param>
        public static void ApplyFontScale(double scale)
        {
            if (Current?.Resources == null) return;

            // Update the ControlContentThemeFontSize resource which is used by Fluent theme
            Current.Resources["ControlContentThemeFontSize"] = 14.0 * scale;

            // Create additional font size resources for all the explicit sizes used in the app
            Current.Resources["FontSize8"] = 8.0 * scale;
            Current.Resources["FontSize9"] = 9.0 * scale;
            Current.Resources["FontSize10"] = 10.0 * scale;
            Current.Resources["FontSize11"] = 11.0 * scale;
            Current.Resources["FontSize12"] = 12.0 * scale;
            Current.Resources["FontSize13"] = 13.0 * scale;
            Current.Resources["FontSize14"] = 14.0 * scale;
            Current.Resources["FontSize15"] = 15.0 * scale;
            Current.Resources["FontSize16"] = 16.0 * scale;
            Current.Resources["FontSize18"] = 18.0 * scale;
            Current.Resources["FontSize20"] = 20.0 * scale;
            Current.Resources["FontSize24"] = 24.0 * scale;
            Current.Resources["FontSize22"] = 22.0 * scale;
            Current.Resources["FontSize32"] = 32.0 * scale;
        }
    }
}
