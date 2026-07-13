using System;
using System.Diagnostics;
using System.IO;

namespace Ongenet.Core.Audio.Files;

/// <summary>Muxes a rendered WAV master with a project video track via ffmpeg.</summary>
public static class FfmpegVideoMuxer
{
    public static void Mux(string wavPath, string videoPath, double videoOffsetSeconds, string outputPath,
        double inPointSeconds = 0, double outPointSeconds = 0)
    {
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

    public static bool IsAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("ffmpeg", "-version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch
        {
            return false;
        }
    }
}
