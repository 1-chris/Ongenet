using System;
using System.Diagnostics;
using System.IO;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.VideoComposition.Ffmpeg;

/// <summary>Muxes a rendered WAV master with a project video track via ffmpeg.</summary>
public sealed class FfmpegVideoMuxer : IVideoMuxer
{
    public bool IsAvailable => FfmpegEncoder.IsAvailable;

    public void Mux(string wavPath, string videoPath, double videoOffsetSeconds, string outputPath,
        double inPointSeconds = 0, double outPointSeconds = 0)
    {
        var ffmpeg = FfmpegEncoder.Locate()
            ?? throw new InvalidOperationException("ffmpeg was not found on this system.");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");

        if (inPointSeconds > 1e-6)
        {
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(inPointSeconds.ToString("0.###"));
        }

        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(videoPath);

        if (outPointSeconds > inPointSeconds + 1e-6)
        {
            psi.ArgumentList.Add("-to");
            psi.ArgumentList.Add(outPointSeconds.ToString("0.###"));
        }

        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(wavPath);
        if (Math.Abs(videoOffsetSeconds) > 1e-6)
        {
            psi.ArgumentList.Add("-itsoffset");
            psi.ArgumentList.Add(videoOffsetSeconds.ToString("0.###"));
        }
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("copy");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-shortest");
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
        var err = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg mux failed: {err.Trim()}");
    }
}
