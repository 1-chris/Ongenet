using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Audio.Files;

/// <summary>Bakes video composition (layers + fades) to MP4 via ffmpeg filter_complex.</summary>
public static class FfmpegVideoCompositor
{
    public static void Export(Project project, string wavPath, string outputPath,
        double durationSeconds, IReadOnlyDictionary<Guid, double>? layerOpacities = null,
        IVideoWaveformCacheService? waveformCache = null, double bpm = 120)
    {
        var canvasW = Math.Clamp(project.VideoCanvasWidth, 320, 4096);
        var canvasH = Math.Clamp(project.VideoCanvasHeight, 320, 4096);
        var ordered = project.VideoLayers.OrderBy(l => l.ZOrder).ToList();

        VideoLayer? backgroundLayer = null;
        VideoLayerItem? backgroundItem = null;
        foreach (var layer in ordered)
        {
            foreach (var item in layer.Items.Where(i => i.Kind == VideoElementKind.Video))
            {
                if (IsFullCanvas(item) && !string.IsNullOrWhiteSpace(item.SourcePath) && File.Exists(item.SourcePath))
                {
                    backgroundLayer = layer;
                    backgroundItem = item;
                    break;
                }
            }

            if (backgroundLayer is not null) break;
        }

        var hasBgFile = backgroundItem is not null;

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");

        if (hasBgFile)
        {
            if (backgroundLayer!.InPointSeconds > 1e-6)
            {
                psi.ArgumentList.Add("-ss");
                psi.ArgumentList.Add(backgroundLayer.InPointSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            }

            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(backgroundItem!.SourcePath);
        }
        else
        {
            var dur = durationSeconds > 0
                ? durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                : "3600";
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add($"color=c=0x1e1e2e:s={canvasW}x{canvasH}:d={dur}");
        }

        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(wavPath);

        var filter = new StringBuilder();
        if (hasBgFile)
        {
            filter.Append(CultureInfo.InvariantCulture,
                $"[0:v]setpts=PTS-STARTPTS,scale={canvasW}:{canvasH}:force_original_aspect_ratio=decrease," +
                $"pad={canvasW}:{canvasH}:(ow-iw)/2:(oh-ih)/2[bg];");
        }
        else
        {
            filter.Append("[0:v]copy[bg];");
        }

        var lastLabel = "bg";
        var inputIndex = 2;
        var tempFiles = new List<string>();
        try
        {
            foreach (var layer in ordered)
            {
                var layerOpacity = layerOpacities?.TryGetValue(layer.Id, out var lo) == true ? lo : layer.Opacity;
                if (layerOpacity <= 0.01) continue;

                if (layer.IsWaveformLayer && layer.AudioSourceTrackId is { } srcId && waveformCache is not null)
                {
                    try
                    {
                        var wf = waveformCache.GetOrBuild(project, srcId, bpm);
                        var png = Path.Combine(Path.GetTempPath(), $"ongenet-wf-{Guid.NewGuid():N}.png");
                        tempFiles.Add(png);
                        var wfW = Math.Max(32, (int)(layer.WaveformWidth * canvasW));
                        var wfH = Math.Max(16, (int)(layer.WaveformHeight * canvasH));
                        VideoWaveformPngExporter.ExportWaveformPng(wf, png, wfW, wfH, layer.WaveformColorArgb, layer.WaveformStyle);
                        psi.ArgumentList.Add("-i");
                        psi.ArgumentList.Add(png);
                        var outLabel = $"v{inputIndex}";
                        var x = (int)(layer.WaveformX * canvasW);
                        var y = (int)(layer.WaveformY * canvasH);
                        filter.Append(CultureInfo.InvariantCulture,
                            $"[{inputIndex}:v]format=rgba,colorchannelmixer=aa={layerOpacity:0.###}[{outLabel}];");
                        filter.Append(CultureInfo.InvariantCulture, $"[{lastLabel}][{outLabel}]overlay={x}:{y}[o{inputIndex}];");
                        lastLabel = $"o{inputIndex}";
                        inputIndex++;
                    }
                    catch
                    {
                        // Skip waveform layers that fail to render.
                    }

                    continue;
                }

                foreach (var item in layer.Items)
                {
                    if (item.Kind is VideoElementKind.Waveform) continue;
                    if (backgroundLayer is not null && ReferenceEquals(item, backgroundItem)) continue;
                    if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath)) continue;

                    var itemOpacity = layerOpacity * item.Opacity;
                    if (itemOpacity <= 0.01) continue;

                    psi.ArgumentList.Add("-i");
                    psi.ArgumentList.Add(item.SourcePath);
                    var outLabel = $"v{inputIndex}";
                    var x = (int)(item.X * canvasW);
                    var y = (int)(item.Y * canvasH);
                    filter.Append(CultureInfo.InvariantCulture,
                        $"[{inputIndex}:v]format=rgba,colorchannelmixer=aa={itemOpacity:0.###}[{outLabel}];");
                    filter.Append(CultureInfo.InvariantCulture, $"[{lastLabel}][{outLabel}]overlay={x}:{y}[o{inputIndex}];");
                    lastLabel = $"o{inputIndex}";
                    inputIndex++;
                }
            }
        }
        catch
        {
            foreach (var temp in tempFiles)
            {
                if (File.Exists(temp)) File.Delete(temp);
            }

            throw;
        }

        filter.Append(CultureInfo.InvariantCulture, $"[{lastLabel}]format=yuv420p[vout]");
        psi.ArgumentList.Add("-filter_complex");
        psi.ArgumentList.Add(filter.ToString());
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("[vout]");
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("1:a");
        if (hasBgFile && Math.Abs(backgroundLayer!.OffsetSeconds) > 1e-6)
        {
            psi.ArgumentList.Add("-itsoffset");
            psi.ArgumentList.Add(backgroundLayer.OffsetSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }
        if (durationSeconds > 0)
        {
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(durationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-shortest");
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
        var err = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg compositor failed: {err.Trim()}");

        foreach (var temp in tempFiles)
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static bool IsFullCanvas(VideoLayerItem item) =>
        item.X <= 1e-6 && item.Y <= 1e-6 && item.Width >= 1 - 1e-6 && item.Height >= 1 - 1e-6;
}
