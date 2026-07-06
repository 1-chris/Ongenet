using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Locates a system <c>ffmpeg</c> and encodes rendered WAVs to compressed formats (MP3 320 kbps,
/// FLAC). ffmpeg is an optional system dependency: when it isn't installed the app simply doesn't
/// offer the compressed formats. The search covers PATH plus the common install locations that GUI
/// apps don't inherit in their PATH (e.g. Homebrew on macOS).
/// </summary>
public static class FfmpegEncoder
{
    private static string? _path;
    private static bool _probed;
    private static readonly object Lock = new();

    /// <summary>True when an ffmpeg binary was found on this system (probed once, then cached).</summary>
    public static bool IsAvailable => Locate() is not null;

    /// <summary>The full path to the ffmpeg binary, or null if none was found.</summary>
    public static string? Locate()
    {
        lock (Lock)
        {
            if (_probed) return _path;
            _probed = true;
            _path = Probe();
            return _path;
        }
    }

    /// <summary>Encodes <paramref name="wavPath"/> to MP3 (libmp3lame, 320 kbps CBR) at <paramref name="outputPath"/>.</summary>
    public static void EncodeMp3(string wavPath, string outputPath)
        => Run(wavPath, outputPath, "libmp3lame", "-b:a", "320k");

    /// <summary>Encodes <paramref name="wavPath"/> to FLAC (lossless) at <paramref name="outputPath"/>.</summary>
    public static void EncodeFlac(string wavPath, string outputPath)
        => Run(wavPath, outputPath, "flac");

    private static void Run(string input, string output, string codec, params string[] extraArgs)
    {
        var ffmpeg = Locate() ?? throw new InvalidOperationException("ffmpeg was not found on this system.");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(input);
        psi.ArgumentList.Add("-codec:a");
        psi.ArgumentList.Add(codec);
        foreach (var a in extraArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(output);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        var stderr = process.StandardError.ReadToEnd(); // drain so ffmpeg can't block on a full pipe
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}: {Tail(stderr)}");
        }
    }

    private static string Tail(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[^500..];
    }

    private static string? Probe()
    {
        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            var candidate = SafeCombine(dir, exe);
            if (candidate is not null && File.Exists(candidate)) return candidate;
        }

        // GUI apps often launch without the user's shell PATH; probe the usual install spots too.
        string[] extra = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new[] { "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin" }
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { @"C:\ffmpeg\bin", @"C:\Program Files\ffmpeg\bin" }
                : new[] { "/usr/local/bin", "/usr/bin", "/snap/bin", "/var/lib/flatpak/exports/bin" };
        foreach (var dir in extra)
        {
            var candidate = SafeCombine(dir, exe);
            if (candidate is not null && File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string? SafeCombine(string dir, string file)
    {
        try { return Path.Combine(dir.Trim(), file); }
        catch { return null; }
    }
}
