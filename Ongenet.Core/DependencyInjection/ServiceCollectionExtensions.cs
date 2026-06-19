using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.DependencyInjection;

/// <summary>
/// Extension methods for registering Ongenet.Core services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core Ongenet services: logging support and the in-process event aggregator.
    /// Application features are layered on top of this as the DAW grows.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOngenetCore(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<IEventAggregator, EventAggregator>();

        // DAW session services. Singletons: there is one current project, one transport,
        // and one selection shared across the whole application.
        services.AddSingleton<IInstrumentRegistry, InstrumentRegistry>();
        services.AddSingleton<IEffectRegistry, EffectRegistry>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IProjectFileService, ProjectFileService>();
        services.AddSingleton<ITransportService, TransportService>();
        services.AddSingleton<ISelectionService, SelectionService>();
        services.AddSingleton<IEditModeService, EditModeService>();
        services.AddSingleton<IPreviewService, PreviewService>();
        services.AddSingleton<IRecordingService, RecordingService>();

        // Audio engine. The concrete IAudioOutput device is registered by the host app
        // (Ongenet.Desktop references the PortAudio backend); the engine depends only on the seam.
        services.AddSingleton<IAudioEngine, AudioEngine>();

        // Audio file decoding. One IAudioFileDecoder per strategy: native WAV first, then ffmpeg for
        // everything else (transcoded to WAV on the fly). AudioFileService picks the first that matches.
        services.AddSingleton<IAudioFileDecoder, WavFileDecoder>();
        services.AddSingleton<IAudioFileDecoder, FfmpegAudioDecoder>();
        services.AddSingleton<IAudioFileService, AudioFileService>();

        // Offline render (export to WAV).
        services.AddSingleton<OfflineRenderer>();
        return services;
    }
}
