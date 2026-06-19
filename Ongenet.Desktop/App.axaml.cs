using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ongenet.Audio;
using Ongenet.Clap;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.DependencyInjection;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.ViewModels;
using Ongenet.Desktop.Views.Windows;

namespace Ongenet.Desktop
{
    /// <summary>
    /// The main application class.
    /// Handles dependency injection setup and main window initialization.
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider? ServiceProvider { get; private set; }

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

            // Audio device backend (PortAudio). The engine in Core depends only on the IAudioOutput /
            // IAudioInput / IAudioDeviceService seams; the concrete devices live in Ongenet.Audio.
            services.AddSingleton<IAudioDeviceService, PortAudioDeviceService>();
            services.AddSingleton<IAudioOutput, PortAudioOutput>();
            services.AddSingleton<IAudioInput, PortAudioInput>();

            // Parameter automation: creates lanes from the "Create automation track" right-click.
            services.AddSingleton<Services.IAutomationService, Services.AutomationService>();

            // Undo/redo history (project-snapshot based).
            services.AddSingleton<Services.IHistoryService, Services.HistoryService>();

            // ~30fps UI heartbeat so automated controls visibly move during playback.
            services.AddSingleton<Services.IPlaybackClock, Services.PlaybackClock>();

            // CLAP plugin hosting: scans for installed plugins and registers them as instruments + effects.
            services.AddSingleton(sp =>
            {
                var instruments = sp.GetRequiredService<IInstrumentRegistry>();
                var effects = sp.GetRequiredService<IEffectRegistry>();
                var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("Clap");
                return new ClapPluginProvider(instruments, effects, msg => logger?.LogInformation("{Message}", msg));
            });

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
            services.AddSingleton<InstrumentsViewModel>();
            services.AddSingleton<MainViewModel>();

            // Live theming (Catppuccin variants + custom themes).
            services.AddSingleton<Theming.IThemeService, Theming.ThemeService>();
            services.AddSingleton<ThemeEditorViewModel>();
            services.AddSingleton<HistoryViewModel>();

            ServiceProvider = services.BuildServiceProvider();

            // Establish the font-size resources used across the app.
            ApplyFontScale(1.0);

            // Capture the palette brushes and apply the default theme.
            ServiceProvider.GetRequiredService<Theming.IThemeService>().Initialize();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = ServiceProvider.GetRequiredService<MainViewModel>()
                };

                // Start the audio engine once the UI is up; stop it cleanly on exit.
                var engine = ServiceProvider.GetRequiredService<IAudioEngine>();
                TryStartAudio(engine);

                // Route CLAP host/plugin diagnostics (incl. GUI open steps) to the in-app log.
                var clapLogger = ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Clap");
                ClapInstrument.Log = msg => clapLogger?.LogInformation("{Message}", msg);

                // Scan for CLAP plugins in the background; they appear in the Instruments tab + effects menu as found.
                ServiceProvider.GetRequiredService<ClapPluginProvider>().ScanAsync();

                desktop.ShutdownRequested += (_, _) =>
                {
                    engine.Dispose();
                    DisposeTrackPlugins();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>Disposes any CLAP plugin instruments + effects on the project's tracks (frees native modules).</summary>
        private static void DisposeTrackPlugins()
        {
            var project = ServiceProvider?.GetService<IProjectService>()?.Current;
            if (project is null) return;
            foreach (var track in project.Tracks)
            {
                if (track.Instrument is IDisposable inst) inst.Dispose();
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
