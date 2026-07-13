using System;
using System.Collections.Generic;
using System.Numerics;
using Ongenet.App.Controls.Engine3D;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Engine3D.Abstractions;
using SkiaSharp;

namespace Ongenet.App.Services;

/// <summary>Stateful per-layer Engine3D visualizations for video preview and export.</summary>
public sealed class VideoEngine3DLayerRenderer : IVideoEngine3DLayerRenderer, IDisposable
{
    private readonly IVideoEngine3DRenderService _render;
    private readonly Dictionary<Guid, LayerVizState> _states = new();

    public VideoEngine3DLayerRenderer(IVideoEngine3DRenderService render) => _render = render;

    public bool IsAvailable => _render.IsAvailable;

    public SKBitmap? RenderWaveformLayer(VideoLayer layer, IVideoAudioScopeService scope, int width, int height, double dt)
    {
        if (!IsAvailable || layer.WaveformStyle != VideoWaveformStyle.Scope3D
            || layer.AudioSourceTrackId is not { } trackId)
            return null;

        var state = GetOrCreate(layer.Id, $"waveform:scope3d:{trackId}", () =>
        {
            var scene = new Scene();
            var viz = new VideoScope3DVisualization(trackId, scope, layer);
            viz.Build(scene);
            viz.ApplyTheme(scene);
            return new LayerVizState(scene, viz);
        });

        state.Viz.Update(state.Scene, dt);
        ApplyScopeCamera(state.Scene, layer);
        return RenderScaled(state.Scene, width, height, previewQuality: width <= 960);
    }

    public SKBitmap? RenderEngine3DLayer(VideoLayer layer, IVideoAudioScopeService scope, int width, int height, double dt)
    {
        if (!IsAvailable || !layer.IsEngine3DLayer || layer.Engine3DEffectKind is not { } kind)
            return null;

        var state = GetOrCreate(layer.Id, BuildEngine3DCacheTag(layer, kind), () =>
        {
            var scene = new Scene();
            IEngine3DVisualization viz = kind switch
            {
                VideoEngine3DEffectKind.TexturedCube => new TexturedCubeVisualization(layer),
                VideoEngine3DEffectKind.Particles => new AudioParticleVisualization(layer, scope),
                _ => new TexturedCubeVisualization(layer)
            };
            viz.Build(scene);
            viz.ApplyTheme(scene);
            return new LayerVizState(scene, viz);
        });

        state.Viz.Update(state.Scene, dt);
        ApplyEngine3DCamera(state.Scene, layer);
        var previewQuality = width <= 960;
        return RenderScaled(state.Scene, width, height, previewQuality);
    }

    public void ClearLayerState(Guid layerId) => _states.Remove(layerId);

    public void ClearAll() => _states.Clear();

    private LayerVizState GetOrCreate(Guid layerId, string cacheTag, Func<LayerVizState> factory)
    {
        if (_states.TryGetValue(layerId, out var existing)
            && string.Equals(existing.CacheTag, cacheTag, StringComparison.Ordinal))
            return existing;

        _states.Remove(layerId);
        var created = factory();
        created.CacheTag = cacheTag;
        _states[layerId] = created;
        return created;
    }

    private static string BuildEngine3DCacheTag(VideoLayer layer, VideoEngine3DEffectKind kind) => kind switch
    {
        VideoEngine3DEffectKind.TexturedCube => $"engine3d:cube:{layer.Engine3DImagePath ?? ""}",
        VideoEngine3DEffectKind.Particles => $"engine3d:particles:{layer.Engine3DParticleShape}:{layer.Engine3DParticleCount}",
        _ => $"engine3d:{kind}"
    };

    private SKBitmap? RenderScaled(Scene scene, int width, int height, bool previewQuality = true)
    {
        var maxW = previewQuality ? 640 : 960;
        var internalW = Math.Min(width, maxW);
        var internalH = Math.Max(16, (int)Math.Round(height * (internalW / (double)Math.Max(1, width))));
        var rendered = _render.RenderScene(scene, internalW, internalH);
        if (rendered is null) return null;
        if (internalW == width && internalH == height) return rendered;

        var scaled = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(scaled);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
        canvas.DrawBitmap(rendered, new SKRect(0, 0, width, height), paint);
        rendered.Dispose();
        return scaled;
    }

    private static void ApplyScopeCamera(Scene scene, VideoLayer layer)
    {
        scene.Camera.Yaw = (float)layer.Scope3DCameraYaw;
        scene.Camera.Pitch = (float)layer.Scope3DCameraPitch;
        scene.Camera.Distance = (float)layer.Scope3DCameraDistance;
        scene.TransparentBackground = layer.Scope3DTransparentBackground;
    }

    private static void ApplyEngine3DCamera(Scene scene, VideoLayer layer)
    {
        scene.Camera.Yaw = (float)layer.Engine3DCameraYaw;
        scene.Camera.Pitch = (float)layer.Engine3DCameraPitch;
        scene.Camera.Distance = (float)layer.Engine3DCameraDistance;
        scene.TransparentBackground = layer.Engine3DTransparentBackground;
    }

    public void Dispose() => ClearAll();

    private sealed class LayerVizState(Scene scene, IEngine3DVisualization viz)
    {
        public string CacheTag { get; set; } = "";
        public Scene Scene { get; } = scene;
        public IEngine3DVisualization Viz { get; } = viz;
    }
}
