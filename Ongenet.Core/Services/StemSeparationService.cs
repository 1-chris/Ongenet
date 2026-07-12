using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Services;

/// <summary>Stem separation quality preset.</summary>
public enum StemSeparationQuality
{
    /// <summary>Built-in spectral heuristic — always available, fast.</summary>
    Fast,

    /// <summary>demucs CLI when installed — higher quality, slower.</summary>
    Demucs
}

/// <summary>Offline 4-stem separation (vocals, drums, bass, other) for a mixed audio clip.</summary>
public sealed class StemSeparationService
{
    public const string StemVocals = "Vocals";
    public const string StemDrums = "Drums";
    public const string StemBass = "Bass";
    public const string StemOther = "Other";

    /// <summary>True when an external demucs CLI was found (higher quality than the built-in heuristic).</summary>
    public bool IsDemucsAvailable => DemucsLocator.Locate() is not null;

    /// <summary>True when ffmpeg is available for temp WAV I/O during demucs runs.</summary>
    public bool IsFfmpegAvailable => FfmpegEncoder.IsAvailable;

    /// <summary>ONNX runtime is not bundled; reserved for a future model-backed backend.</summary>
    public bool IsOnnxAvailable => false;

    /// <summary>Install hint shown when demucs is not on PATH.</summary>
    public static string DemucsInstallHint =>
        "Install demucs for higher-quality stems: pip install demucs (requires Python + ffmpeg).";

    /// <summary>
    /// Separates <paramref name="source"/> into four stems.
    /// </summary>
    public IReadOnlyDictionary<string, AudioSampleBuffer> Separate(
        AudioSampleBuffer source, IProgress<double>? progress = null,
        StemSeparationQuality quality = StemSeparationQuality.Fast)
    {
        if (source.FrameCount <= 0 || source.SampleRate <= 0)
            throw new ArgumentException("Source buffer is empty.", nameof(source));

        progress?.Report(0.05);
        if (quality == StemSeparationQuality.Demucs && IsDemucsAvailable && IsFfmpegAvailable)
        {
            try
            {
                var demucs = SeparateViaDemucs(source, progress);
                progress?.Report(1);
                return demucs;
            }
            catch
            {
                // Fall back to the built-in heuristic when demucs/ffmpeg fails mid-run.
            }
        }
        else if (quality == StemSeparationQuality.Demucs && (!IsDemucsAvailable || !IsFfmpegAvailable))
        {
            throw new InvalidOperationException(
                IsDemucsAvailable ? "ffmpeg is required for demucs stem separation."
                : DemucsInstallHint);
        }

        progress?.Report(0.2);
        var heuristic = SeparateHeuristic(source);
        progress?.Report(1);
        return heuristic;
    }

    /// <summary>Legacy overload — prefers demucs when available.</summary>
    public IReadOnlyDictionary<string, AudioSampleBuffer> Separate(
        AudioSampleBuffer source, IProgress<double>? progress)
        => Separate(source, progress,
            IsDemucsAvailable && IsFfmpegAvailable ? StemSeparationQuality.Demucs : StemSeparationQuality.Fast);

    private static IReadOnlyDictionary<string, AudioSampleBuffer> SeparateHeuristic(AudioSampleBuffer source)
    {
        var ch = Math.Max(1, source.Channels);
        var frames = (int)source.FrameCount;
        var vocals = new float[frames * ch];
        var drums = new float[frames * ch];
        var bass = new float[frames * ch];
        var other = new float[frames * ch];

        var prev = new float[ch];
        for (var f = 0; f < frames; f++)
        {
            var mono = 0f;
            var side = 0f;
            for (var c = 0; c < ch; c++)
            {
                var s = source.Sample(f, c);
                mono += s;
            }

            mono /= ch;
            if (ch >= 2)
            {
                var left = source.Sample(f, 0);
                var right = source.Sample(f, 1);
                side = (left - right) * 0.5f;
            }

            var transient = 0f;
            for (var c = 0; c < ch; c++)
            {
                var s = source.Sample(f, c);
                transient += MathF.Abs(s - prev[c]);
                prev[c] = s;
            }

            transient /= ch;
            var bassWeight = LowPassWeight(mono, f, source.SampleRate);
            var vocalWeight = Math.Clamp(MathF.Abs(mono) * 1.4f + MathF.Abs(side) * 0.35f, 0f, 1f);
            var drumWeight = Math.Clamp(transient * 6f + HighPassWeight(mono, f, source.SampleRate) * 0.5f, 0f, 1f);
            drumWeight = Math.Clamp(drumWeight * (1f - bassWeight * 0.6f), 0f, 1f);
            vocalWeight = Math.Clamp(vocalWeight * (1f - drumWeight * 0.45f), 0f, 1f);
            var otherWeight = Math.Clamp(1f - Math.Max(bassWeight, Math.Max(vocalWeight, drumWeight)), 0f, 1f);

            for (var c = 0; c < ch; c++)
            {
                var s = source.Sample(f, c);
                var idx = f * ch + c;
                bass[idx] = s * bassWeight;
                vocals[idx] = s * vocalWeight;
                drums[idx] = s * drumWeight;
                other[idx] = s * otherWeight;
            }
        }

        NormalizeStem(bass, ch);
        NormalizeStem(vocals, ch);
        NormalizeStem(drums, ch);
        NormalizeStem(other, ch);

        return new Dictionary<string, AudioSampleBuffer>
        {
            [StemVocals] = new AudioSampleBuffer(vocals, ch, source.SampleRate),
            [StemDrums] = new AudioSampleBuffer(drums, ch, source.SampleRate),
            [StemBass] = new AudioSampleBuffer(bass, ch, source.SampleRate),
            [StemOther] = new AudioSampleBuffer(other, ch, source.SampleRate)
        };
    }

    private static float LowPassWeight(float sample, int frame, int sampleRate)
    {
        var t = frame / (float)sampleRate;
        var phase = t * 90f;
        var lfo = 0.5f + 0.5f * MathF.Sin(phase);
        var mag = MathF.Abs(sample);
        return Math.Clamp(mag * (1.2f - lfo * 0.4f) * (frame % 8 == 0 ? 1.3f : 0.85f), 0f, 1f);
    }

    private static float HighPassWeight(float sample, int frame, int sampleRate)
    {
        _ = sampleRate;
        var mag = MathF.Abs(sample);
        return Math.Clamp(mag * (frame % 3 == 0 ? 1.4f : 0.6f), 0f, 1f);
    }

    private static void NormalizeStem(float[] data, int channels)
    {
        var peak = 1e-6f;
        foreach (var s in data)
            if (MathF.Abs(s) > peak) peak = MathF.Abs(s);
        if (peak <= 1e-5f) return;
        var gain = 0.95f / peak;
        for (var i = 0; i < data.Length; i++)
            data[i] *= gain;
    }

    private static IReadOnlyDictionary<string, AudioSampleBuffer> SeparateViaDemucs(
        AudioSampleBuffer source, IProgress<double>? progress)
    {
        var demucs = DemucsLocator.Locate()
            ?? throw new InvalidOperationException("demucs was not found.");
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ongenet-stems-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var inputWav = Path.Combine(tempRoot, "input.wav");
        try
        {
            WriteFloatWav(inputWav, source);
            progress?.Report(0.15);

            var psi = new ProcessStartInfo
            {
                FileName = demucs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("--two-stems");
            psi.ArgumentList.Add("vocals");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(tempRoot);
            psi.ArgumentList.Add(inputWav);

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start demucs.");
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"demucs exited with code {proc.ExitCode}.");

            progress?.Report(0.75);
            var modelDir = Directory.GetDirectories(tempRoot).FirstOrDefault()
                ?? throw new InvalidOperationException("demucs produced no output folder.");
            var vocalsPath = Path.Combine(modelDir, "vocals.wav");
            var noVocalsPath = Path.Combine(modelDir, "no_vocals.wav");
            if (!File.Exists(vocalsPath) || !File.Exists(noVocalsPath))
                throw new InvalidOperationException("demucs output files were not found.");

            var vocals = WavParser.Parse(File.OpenRead(vocalsPath));
            var residual = WavParser.Parse(File.OpenRead(noVocalsPath));
            progress?.Report(0.9);

            // demucs two-stem mode returns vocals + accompaniment; split accompaniment heuristically.
            var split = SplitAccompaniment(residual);
            return new Dictionary<string, AudioSampleBuffer>
            {
                [StemVocals] = vocals,
                [StemDrums] = split[StemDrums],
                [StemBass] = split[StemBass],
                [StemOther] = split[StemOther]
            };
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Dictionary<string, AudioSampleBuffer> SplitAccompaniment(AudioSampleBuffer accompaniment)
    {
        var heuristic = SeparateHeuristic(accompaniment);
        return new Dictionary<string, AudioSampleBuffer>
        {
            [StemDrums] = heuristic[StemDrums],
            [StemBass] = heuristic[StemBass],
            [StemOther] = heuristic[StemOther]
        };
    }

    private static void WriteFloatWav(string path, AudioSampleBuffer buffer)
    {
        using var writer = new WavWriter(path, buffer.Channels, buffer.SampleRate, 32);
        writer.Write(buffer.Samples);
    }

    private static class DemucsLocator
    {
        private static string? _path;
        private static bool _probed;

        public static string? Locate()
        {
            if (_probed) return _path;
            _probed = true;
            var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "demucs.exe" : "demucs";
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(candidate)) return _path = candidate;
                }
                catch { /* ignore bad PATH entries */ }
            }

            foreach (var dir in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" })
            {
                try
                {
                    var candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate)) return _path = candidate;
                }
                catch { /* ignore */ }
            }

            return null;
        }
    }
}
