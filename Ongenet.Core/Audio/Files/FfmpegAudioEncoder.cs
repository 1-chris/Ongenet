using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Files;

/// <summary>Encodes a WAV file to FLAC/MP3/OGG via ffmpeg.</summary>
public static class FfmpegAudioEncoder
{
    public static bool CanEncode(string extension) =>
        extension.Equals(".flac", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase);

    public static void EncodeWav(string wavPath, string outputPath,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var ext = Path.GetExtension(outputPath).ToLowerInvariant();
        var codec = ext switch
        {
            ".flac" => new[] { "-c:a", "flac" },
            ".mp3" => new[] { "-c:a", "libmp3lame", "-q:a", "2" },
            ".ogg" => new[] { "-c:a", "libvorbis", "-q:a", "5" },
            _ => throw new NotSupportedException($"Export format '{ext}' is not supported.")
        };

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
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(wavPath);
        foreach (var arg in codec) psi.ArgumentList.Add(arg);
        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                psi.ArgumentList.Add("-metadata");
                psi.ArgumentList.Add($"{key}={value}");
            }
        }
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
        var err = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg encode failed: {err.Trim()}");
    }

    /// <summary>
    /// Renders to a temp WAV via <paramref name="renderWav"/>, then copies or encodes to
    /// <paramref name="finalPath"/>. Return <c>true</c> from the callback when the final file is
    /// already written (e.g. composited MP4) to skip audio-only encoding.
    /// </summary>
    public static void ExportViaWav(Func<string, bool> renderWav, string finalPath,
        Func<IReadOnlyDictionary<string, string>?>? metadataProvider = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"ongenet-export-{Guid.NewGuid():N}.wav");
        try
        {
            var handled = renderWav(temp);
            if (handled) return;
            if (finalPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(temp, finalPath, overwrite: true);
                return;
            }

            EncodeWav(temp, finalPath, metadataProvider?.Invoke());
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }
}
