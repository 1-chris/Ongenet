using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Ongenet.Core.Models.Media;

namespace Ongenet.Core.Audio.Files;

/// <summary>Exports waveform peaks to PNG via ffmpeg for composited video export.</summary>
public static class VideoWaveformPngExporter
{
    public static void ExportWaveformPng(AudioWaveform waveform, string outputPath, int width, int height,
        uint colorArgb, VideoWaveformStyle style)
    {
        if (waveform.TotalFrames <= 0) throw new InvalidOperationException("Waveform has no frames.");

        var tempWav = Path.Combine(Path.GetTempPath(), $"ongenet-wf-{Guid.NewGuid():N}.wav");
        try
        {
            WriteMonoWavFromPeaks(waveform, tempWav);
            var color = colorArgb & 0x00FFFFFF;
            var mode = style == VideoWaveformStyle.Bars ? "p2p" : "line";
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(tempWav);
            psi.ArgumentList.Add("-filter_complex");
            psi.ArgumentList.Add(
                $"showwavespic=s={width}x{height}:colors=0x{color:X6}:scale=lin:mode={mode}");
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add(outputPath);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
            var err = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg waveform export failed: {err.Trim()}");
        }
        finally
        {
            if (File.Exists(tempWav)) File.Delete(tempWav);
        }
    }

    private static void WriteMonoWavFromPeaks(AudioWaveform waveform, string path)
    {
        var bucketCount = waveform.BucketCount;
        var samples = new float[bucketCount];
        for (var b = 0; b < bucketCount; b++)
        {
            var startFrame = (long)b * waveform.SamplesPerBucket;
            var endFrame = Math.Min(waveform.TotalFrames, startFrame + waveform.SamplesPerBucket);
            waveform.GetPeak(startFrame, endFrame, out var min, out var max);
            samples[b] = Math.Max(Math.Abs(min), Math.Abs(max));
        }

        using var writer = new WavWriter(path, 1, waveform.SampleRate, 16);
        writer.Write(samples);
    }
}
