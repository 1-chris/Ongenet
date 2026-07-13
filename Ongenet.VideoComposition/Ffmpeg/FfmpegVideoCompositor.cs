using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.VideoComposition.Rendering;
using SkiaSharp;

namespace Ongenet.VideoComposition.Ffmpeg;

/// <summary>Bakes the in-app video composition frame-by-frame and encodes to MP4 via ffmpeg.</summary>
public sealed class FfmpegVideoCompositor : IVideoCompositor
{
    private readonly IVideoFrameExtractor _frameExtractor;
    private readonly IVideoEngine3DLayerRenderer? _engine3D;

    public FfmpegVideoCompositor(IVideoFrameExtractor frameExtractor, IVideoEngine3DLayerRenderer? engine3D = null)
    {
        _frameExtractor = frameExtractor;
        _engine3D = engine3D;
    }

    public bool IsAvailable => FfmpegEncoder.IsAvailable;

    public void Export(Project project, string wavPath, string outputPath, double durationSeconds,
        IReadOnlyDictionary<Guid, double>? layerOpacities = null,
        IVideoWaveformCacheService? waveformCache = null, double bpm = 120,
        double startBeat = 0, IProgress<double>? progress = null)
    {
        if (waveformCache is null)
            throw new InvalidOperationException("Video export requires waveform cache for visualiser stems.");

        var canvasW = Math.Clamp(project.VideoCanvasWidth, 320, 4096);
        var canvasH = Math.Clamp(project.VideoCanvasHeight, 320, 4096);
        var fps = VideoCompositionTimeMapper.ResolveExportFps(project);
        var frameDuration = 1.0 / fps;
        var totalFrames = Math.Max(1, (int)Math.Ceiling(durationSeconds * fps));

        var stemBuffers = new Dictionary<Guid, AudioSampleBuffer>();
        var waveforms = new Dictionary<Guid, AudioWaveform>();
        foreach (var layer in project.VideoLayers.Where(l =>
            (l.IsWaveformLayer && l.AudioSourceTrackId is not null)
            || (l.IsEngine3DLayer && l.Engine3DAudioSourceTrackId is not null)))
        {
            var id = layer.IsEngine3DLayer ? layer.Engine3DAudioSourceTrackId!.Value : layer.AudioSourceTrackId!.Value;
            if (stemBuffers.ContainsKey(id)) continue;
            stemBuffers[id] = waveformCache.GetOrBuildStemBuffer(project, id, bpm, progress);
            if (!layer.WaveformFollowPlayhead && layer.IsWaveformLayer)
                waveforms[id] = waveformCache.GetOrBuild(project, id, bpm, progress);
        }

        using var assets = new VideoCompositionExportAssets(_frameExtractor)
        {
            StemBuffers = stemBuffers,
            Waveforms = waveforms,
            Engine3DRenderer = _engine3D,
            Engine3DFrameDt = frameDuration
        };
        var scope = new OfflineVideoAudioScope(stemBuffers);
        var triggers = new VideoTriggerEngine();
        triggers.Seek(project, startBeat);

        var ffmpeg = FfmpegEncoder.Locate()
            ?? throw new InvalidOperationException("ffmpeg was not found on this system.");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("rawvideo");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("bgra");
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add($"{canvasW}x{canvasH}");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add(fps.ToString("0.##", CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("-");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(wavPath);
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("0:v:0");
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("1:a:0");
        if (durationSeconds > 0)
        {
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(durationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add("192k");
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
        var stdin = process.StandardInput.BaseStream;
        var frameBytes = canvasW * canvasH * 4;
        var buffer = new byte[frameBytes];
        var imageInfo = new SKImageInfo(canvasW, canvasH, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        var prevBeat = startBeat;

        double BeatsToSeconds(Project p, double beats) => beats * 60.0 / Math.Max(1, bpm);

        try
        {
            for (var frame = 0; frame < totalFrames; frame++)
            {
                var timeSeconds = frame * frameDuration;
                var beat = startBeat + timeSeconds * bpm / 60.0;
                triggers.Tick(project, prevBeat, beat, frameDuration);
                prevBeat = beat;

                VideoCompositionFrameRenderer.Render(canvas, project, timeSeconds, beat,
                    triggers.Runtime, scope, assets, canvasW, canvasH, BeatsToSeconds);

                canvas.Flush();
                var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    if (!surface.ReadPixels(imageInfo, handle.AddrOfPinnedObject(), canvasW * 4, 0, 0))
                        throw new InvalidOperationException("Failed to read composed export frame.");
                }
                finally
                {
                    handle.Free();
                }

                stdin.Write(buffer);
                progress?.Report((frame + 1) / (double)totalFrames);
            }

            stdin.Flush();
            stdin.Close();
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        var err = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg compositor failed: {err.Trim()}");
    }
}
