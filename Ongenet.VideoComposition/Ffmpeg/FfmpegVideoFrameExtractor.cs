using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.VideoComposition.Ffmpeg;

/// <summary>Extracts a single video frame at a given timestamp via ffmpeg.</summary>
public sealed class FfmpegVideoFrameExtractor : IVideoFrameExtractor
{
    public bool IsAvailable => FfmpegEncoder.IsAvailable;

    public byte[]? ExtractFramePng(string videoPath, double timeSeconds)
    {
        var ffmpeg = FfmpegEncoder.Locate();
        if (ffmpeg is null || string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return null;

        var temp = Path.Combine(Path.GetTempPath(), $"ongenet-vframe-{Guid.NewGuid():N}.png");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(timeSeconds.ToString("F3", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(videoPath);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("image2");
            psi.ArgumentList.Add(temp);

            using var process = Process.Start(psi);
            if (process is null) return null;
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(temp)) return null;
            return File.ReadAllBytes(temp);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { /* best effort */ }
        }
    }
}
