using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Decodes the audio formats the built-in WAV decoder can't, by transcoding them to a temporary
/// 32-bit-float WAV with <c>ffmpeg</c> and parsing that via <see cref="WavParser"/>. Decoded PCM
/// is held in memory. Assumes <c>ffmpeg</c> is on the PATH.
/// </summary>
public sealed class FfmpegAudioDecoder : IAudioFileDecoder
{
    // Formats we hand to ffmpeg. Native PCM/float WAV is handled by WavFileDecoder; compressed
    // WAV containers (FL Edison Ogg-in-WAV, etc.) are accepted here via CanDecode.
    private static readonly HashSet<string> Convertible = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".mp4", ".aac",
        ".aif", ".aiff", ".aifc", ".wma", ".alac", ".caf", ".ape", ".wv"
    };

    public bool CanDecode(string path)
    {
        var ext = Path.GetExtension(path);
        if (Convertible.Contains(ext)) return true;

        if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".wave", StringComparison.OrdinalIgnoreCase))
        {
            return WavFormatProbe.TryGetFormatTag(path, out var tag) &&
                   !WavFormatProbe.IsNativePcmOrFloat(tag);
        }

        return false;
    }

    public AudioSampleBuffer Decode(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Quiet logs, float WAV on stdout (avoids per-file temp disk I/O on bulk import).
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-y");

        // FL Edison stores Ogg Vorbis inside a RIFF/WAVE shell (format 0x674F). ffmpeg's wav
        // demuxer does not attach a decoder; forcing the ogg demuxer on the same file works.
        if (WavFormatProbe.TryGetFormatTag(path, out var tag) && tag == WavFormatProbe.FormatOggVorbis)
        {
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("ogg");
        }

        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("wav");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("pcm_f32le");
        psi.ArgumentList.Add("pipe:1");

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
            using var ms = new MemoryStream();
            var errTask = process.StandardError.ReadToEndAsync();
            process.StandardOutput.BaseStream.CopyTo(ms);
            process.WaitForExit();
            var error = errTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffmpeg failed to decode '{Path.GetFileName(path)}' (exit {process.ExitCode}): {error.Trim()}");
            }

            ms.Position = 0;
            return WavParser.Parse(ms);
        }
    }
}
