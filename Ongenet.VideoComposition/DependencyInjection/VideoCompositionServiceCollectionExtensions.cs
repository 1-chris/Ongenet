using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Services.Interfaces;
using Ongenet.VideoComposition.Ffmpeg;
using Ongenet.VideoComposition.Services;

namespace Ongenet.VideoComposition.DependencyInjection;

public static class VideoCompositionServiceCollectionExtensions
{
    public static IServiceCollection AddVideoComposition(this IServiceCollection services)
    {
        services.AddSingleton<IVideoFrameExtractor, FfmpegVideoFrameExtractor>();
        services.AddTransient<ILiveVideoDecoder, LiveVideoDecoder>();
        services.AddSingleton<IVideoMuxer, FfmpegVideoMuxer>();
        services.AddSingleton<IVideoCompositor>(sp => new Ffmpeg.FfmpegVideoCompositor(
            sp.GetRequiredService<IVideoFrameExtractor>(),
            sp.GetService<IVideoEngine3DLayerRenderer>()));
        services.AddSingleton<IVideoWaveformCacheService, VideoWaveformCacheService>();
        services.AddSingleton<IVideoAudioScopeService, VideoAudioScopeService>();
        services.AddSingleton<IVideoProxyCacheService, VideoProxyCacheService>();
        services.AddSingleton<IVideoRenderQueueService, VideoRenderQueueService>();
        return services;
    }
}
