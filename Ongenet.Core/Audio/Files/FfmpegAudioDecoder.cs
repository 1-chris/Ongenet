using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Decodes the audio formats the built-in WAV decoder can't, by transcoding them to a temporary
/// 32-bit-float WAV with <c>ffmpeg</c> and parsing that via <see cref="WavParser"/>. The decoded PCM
/// is held in memory; the temp file is deleted immediately. Assumes <c>ffmpeg</c> is on the PATH.
/// </summary>
public sealed class FfmpegAudioDecoder : IAudioFileDecoder
{
    // Formats we hand to ffmpeg. WAV is intentionally excluded — the native decoder handles it.
    private static readonly HashSet<string> Convertible = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".mp4", ".aac",
        ".aif", ".aiff", ".aifc", ".wma", ".alac", ".caf", ".ape", ".wv"
    };

    public bool CanDecode(string path) => Convertible.Contains(Path.GetExtension(path));

    public AudioSampleBuffer Decode(string path)
    {
        // Avalonia/HW-agnostic: write to a unique temp WAV, parse it, then clean up.
        var temp = Path.Combine(Path.GetTempPath(), $"ongenet-{Guid.NewGuid():N}.wav");
        try
        {
            Transcode(path, temp);
            using var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
            return WavParser.Parse(stream);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static void Transcode(string input, string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // -y overwrite, quiet logs, decode to float WAV (full precision, lossless for our purposes).
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(input);
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("wav");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("pcm_f32le");
        psi.ArgumentList.Add(output);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not run ffmpeg — is it installed and on the PATH?", ex);
        }

        using (process)
        {
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffmpeg failed to decode '{Path.GetFileName(input)}' (exit {process.ExitCode}): {error.Trim()}");
            }
        }
    }
}
