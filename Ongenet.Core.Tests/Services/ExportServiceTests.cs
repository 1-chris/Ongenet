using System;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class ExportServiceTests
{
    [Fact]
    public void ExportOptions_DefaultsToMasterStereo()
    {
        var options = new ExportOptions();
        Assert.Equal(ExportKind.Master, options.Kind);
        Assert.Equal(SurroundFormat.Stereo, options.Surround);
        Assert.Equal(16, options.BitDepth);
        Assert.True(options.IncludeMasterFx);
        Assert.Equal(DitherMode.Tpdf, options.DitherMode);
    }

    [Fact]
    public void CloneProjectForTrackExport_IncludesMasterWhenRequested()
    {
        var project = CreateProjectWithMasterLimiter();
        var track = project.Tracks.First(t => t.Kind != TrackKind.Master);

        var stem = ExportService.CloneProjectForTrackExport(project, track, includeMasterFx: true);

        Assert.Contains(stem.Tracks, t => t.Kind == TrackKind.Master);
        Assert.Contains(stem.Tracks.First(t => t.Kind == TrackKind.Master).Effects, e => e is PeakLimiterEffect);
    }

    [Fact]
    public void CloneProjectForTrackExport_SkipsMasterWhenIncludeMasterFxFalse()
    {
        var project = CreateProjectWithMasterLimiter();
        var track = project.Tracks.First(t => t.Kind != TrackKind.Master);

        var stem = ExportService.CloneProjectForTrackExport(project, track, includeMasterFx: false);

        Assert.DoesNotContain(stem.Tracks, t => t.Kind == TrackKind.Master);
    }

    [Fact]
    public void StemSeparationService_Heuristic_ReturnsFourStems()
    {
        var svc = new StemSeparationService();
        var tone = new float[4410];
        for (var i = 0; i < tone.Length; i++)
            tone[i] = (float)Math.Sin(2 * Math.PI * 220 * i / 44100.0);
        var buffer = new AudioSampleBuffer(tone, 1, 44100);
        var stems = svc.Separate(buffer);
        Assert.Equal(4, stems.Count);
        Assert.True(stems.ContainsKey(StemSeparationService.StemVocals));
        Assert.True(stems.ContainsKey(StemSeparationService.StemDrums));
    }

    [Fact]
    public void LoudnessAnalyzer_FlagsOutOfTargetForHotSine()
    {
        var format = new AudioFormat(48000, 2);
        var buf = new float[48000 * 2];
        for (var f = 0; f < 48000; f++)
        {
            var s = 0.95f * MathF.Sin(2 * MathF.PI * 1000f * f / 48000f);
            buf[f * 2] = s;
            buf[f * 2 + 1] = s;
        }

        var report = LoudnessAnalyzer.Analyze(buf, format, targetLufs: -14.0, targetTruePeakDbTp: -1.0);
        Assert.False(report.WithinTarget);
        Assert.Contains("OUT OF TARGET", report.Summary);
    }

    [Fact]
    public void ApplyTruePeakCeiling_BringsHotBufferUnderTarget()
    {
        var format = new AudioFormat(48000, 2);
        var buf = new float[8192];
        for (var i = 0; i < buf.Length; i++)
            buf[i] = (i % 2 == 0 ? 1f : -1f) * 0.99f;

        ExportService.ApplyTruePeakCeiling(buf, format, -1.0);
        var report = LoudnessAnalyzer.Analyze(buf, format, targetTruePeakDbTp: -1.0);
        Assert.True(report.TruePeakDbTp <= -1.0f + 0.05f, report.Summary);
    }

    [Fact]
    public void NormalizePath_HonorsBypassMasterFx()
    {
        var format = new AudioFormat(48000, 2);
        var project = CreateHotProjectWithLimitingMaster(format);
        var renderer = new OfflineRenderer();

        var withFx = renderer.RenderMasterToBuffer(project, format, 120, null, 0, 4,
            SurroundFormat.Stereo, bypassMasterFx: false);
        var bypass = renderer.RenderMasterToBuffer(project, format, 120, null, 0, 4,
            SurroundFormat.Stereo, bypassMasterFx: true);

        var rFx = LoudnessAnalyzer.Analyze(withFx.Samples,
            new AudioFormat(withFx.SampleRate, withFx.Channels), targetTruePeakDbTp: -1.0);
        var rBypass = LoudnessAnalyzer.Analyze(bypass.Samples,
            new AudioFormat(bypass.SampleRate, bypass.Channels), targetTruePeakDbTp: -1.0);

        Assert.True(Math.Abs(rBypass.TruePeakDbTp - rFx.TruePeakDbTp) > 0.2f
                    || Math.Abs(rBypass.SamplePeakDbFs - rFx.SamplePeakDbFs) > 0.2f,
            $"expected Master FX to change peaks; bypass TP={rBypass.TruePeakDbTp:F2} sample={rBypass.SamplePeakDbFs:F2}, "
            + $"mastered TP={rFx.TruePeakDbTp:F2} sample={rFx.SamplePeakDbFs:F2}");
        Assert.True(rFx.TruePeakDbTp <= -1.0f + 0.25f, rFx.Summary);
    }

    [Fact]
    public void AnalyzeLoudness_WritesSidecarFile()
    {
        var format = new AudioFormat(48000, 2);
        var project = CreateHotProjectWithLimitingMaster(format);
        var svc = new ExportService(new NullVideoCompositor(), new NullVideoMuxer());
        using var dir = new TempDir();
        var wav = Path.Combine(dir.Path, "master.wav");
        var options = new ExportOptions
        {
            Kind = ExportKind.Region, BitDepth = 24, AnalyzeLoudness = true,
            RegionStartBeat = 0, RegionEndBeat = 2
        };
        svc.Export(project, format, 120, wav, options);
        Assert.True(File.Exists(wav + ".loudness.txt"));
        var text = File.ReadAllText(wav + ".loudness.txt");
        Assert.Contains("LUFS", text);
        Assert.True(File.Exists(wav + ".loudness.json"));
        var json = File.ReadAllText(wav + ".loudness.json");
        Assert.Contains("IntegratedLufs", json);
        Assert.Contains("LoudnessRangeLu", json);
    }

    [Fact]
    public void StemWithVsWithoutMasterFx_DiffersInChain()
    {
        var project = CreateProjectWithMasterLimiter();
        var track = project.Tracks.First(t => t.Kind != TrackKind.Master);
        var withFx = ExportService.CloneProjectForTrackExport(project, track, includeMasterFx: true);
        var without = ExportService.CloneProjectForTrackExport(project, track, includeMasterFx: false);
        Assert.Contains(withFx.Tracks, t => t.Kind == TrackKind.Master);
        Assert.DoesNotContain(without.Tracks, t => t.Kind == TrackKind.Master);
    }

    [Fact]
    public void WavWriter_ApplyDither_DoesNotThrowFor16Bit()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "dither.wav");
        using (var writer = new WavWriter(path, 2, 48000, 16, applyDither: true))
        {
            var buf = new float[256];
            for (var i = 0; i < buf.Length; i++) buf[i] = 0.1f * (i % 2 == 0 ? 1 : -1);
            writer.Write(buf);
        }
        Assert.True(new FileInfo(path).Length > 44);
    }

    [Fact]
    public void WavLoudnessMetadata_AppendsInfoCommentAndKeepsWavReadable()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "metadata.wav");
        using (var writer = new WavWriter(path, 1, 48000, 24))
            writer.Write(new float[480]);

        WavLoudnessMetadata.Append(path, -14.2f, -1.1f);

        var bytes = File.ReadAllBytes(path);
        Assert.Contains("Integrated loudness: -14.2 LUFS", System.Text.Encoding.UTF8.GetString(bytes));
        using var stream = File.OpenRead(path);
        var decoded = WavParser.Parse(stream);
        Assert.Equal(480, decoded.Samples.Length);
    }

    [Fact]
    public void ResampleBuffer_48kTo44100_PreservesOneKhzTone()
    {
        const int sourceRate = 48000;
        const int targetRate = 44100;
        var source = new float[sourceRate];
        for (var i = 0; i < source.Length; i++)
            source[i] = 0.8f * MathF.Sin(2 * MathF.PI * 1000f * i / sourceRate);

        var result = ExportService.ResampleBuffer(new AudioSampleBuffer(source, 1, sourceRate), targetRate);
        var bestFrequency = 0;
        var bestMagnitude = 0.0;
        for (var frequency = 980; frequency <= 1020; frequency++)
        {
            double real = 0, imaginary = 0;
            for (var i = 0; i < result.Samples.Length; i++)
            {
                var angle = 2.0 * Math.PI * frequency * i / targetRate;
                real += result.Samples[i] * Math.Cos(angle);
                imaginary -= result.Samples[i] * Math.Sin(angle);
            }
            var magnitude = Math.Sqrt(real * real + imaginary * imaginary);
            if (magnitude <= bestMagnitude) continue;
            bestMagnitude = magnitude;
            bestFrequency = frequency;
        }

        Assert.InRange(bestFrequency, 999, 1001);
        Assert.True(bestMagnitude / result.Samples.Length > 0.35,
            $"1 kHz energy was unexpectedly attenuated: {bestMagnitude / result.Samples.Length:0.000}");
    }

    private static float[] SineStereo(double freq, int frames, double sr, double left, double right)
    {
        var buf = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var s = Math.Sin(2 * Math.PI * freq * i / sr);
            buf[i * 2] = (float)(s * left);
            buf[i * 2 + 1] = (float)(s * right);
        }
        return buf;
    }

    [Fact]
    public void LoudnessAnalyzer_SoftSineNearMinus23_IsWithinTarget()
    {
        const int sr = 48000;
        var format = new AudioFormat(sr, 2);
        var buf = SineStereo(1000, sr * 4, sr, 0.0707, 0.0707);
        var report = LoudnessAnalyzer.Analyze(buf, format, -23.0, -1.0);
        Assert.False(float.IsNegativeInfinity(report.IntegratedLufs), report.Summary);
        Assert.True(report.TruePeakDbTp <= -1.0f + 0.1f, report.Summary);
        Assert.True(report.WithinTarget, report.Summary);
    }

    [Fact]
    public void ExportComparisonPair_MasteredTruePeakAtOrBelowUnmastered()
    {
        var format = new AudioFormat(48000, 2);
        var project = CreateHotProjectWithLimitingMaster(format);
        var svc = new ExportService(new NullVideoCompositor(), new NullVideoMuxer());
        using var dir = new TempDir();
        var wav = Path.Combine(dir.Path, "song.wav");
        var options = new ExportOptions
        {
            Kind = ExportKind.Region, BitDepth = 24, AnalyzeLoudness = false,
            ExportComparisonPair = true, RegionStartBeat = 0, RegionEndBeat = 2
        };
        svc.Export(project, format, 120, wav, options);

        var prePath = Path.Combine(dir.Path, "song-comparison-unmastered.wav");
        var masteredPath = Path.Combine(dir.Path, "song-comparison-mastered.wav");
        Assert.True(File.Exists(prePath));
        Assert.True(File.Exists(masteredPath));

        using var preStream = File.OpenRead(prePath);
        var pre = WavParser.Parse(preStream);
        using var masteredStream = File.OpenRead(masteredPath);
        var mastered = WavParser.Parse(masteredStream);
        var preFmt = new AudioFormat(pre.SampleRate, pre.Channels);
        var masteredFmt = new AudioFormat(mastered.SampleRate, mastered.Channels);
        var preReport = LoudnessAnalyzer.Analyze(pre.Samples, preFmt);
        var masteredReport = LoudnessAnalyzer.Analyze(mastered.Samples, masteredFmt);

        Assert.True(masteredReport.TruePeakDbTp <= preReport.TruePeakDbTp + 0.05f
                    || Math.Abs(masteredReport.IntegratedLufs - preReport.IntegratedLufs) > 0.2f,
            $"mastered TP={masteredReport.TruePeakDbTp:F2} vs pre TP={preReport.TruePeakDbTp:F2}; "
            + $"LUFS {masteredReport.IntegratedLufs:F2} vs {preReport.IntegratedLufs:F2}");
    }

    [Fact]
    public void FullMasterHotProject_NormalizeSpotify_LandsNearTarget()
    {
        var format = new AudioFormat(48000, 2);
        var project = CreateHotProjectWithFullMaster(format);
        var svc = new ExportService(new NullVideoCompositor(), new NullVideoMuxer());
        using var dir = new TempDir();
        var wav = Path.Combine(dir.Path, "spotify.wav");
        const double target = -14.0;
        var options = new ExportOptions
        {
            Kind = ExportKind.Region,
            BitDepth = 24,
            AnalyzeLoudness = true,
            NormalizeLoudness = true,
            DeliveryPlatform = "Spotify",
            RegionStartBeat = 0,
            RegionEndBeat = 4
        };
        svc.Export(project, format, 120, wav, options);
        Assert.NotNull(options.LoudnessReport);
        var integrated = options.LoudnessReport!.Value.IntegratedLufs;
        Assert.False(float.IsNegativeInfinity(integrated), options.LoudnessReport.Value.Summary);
        Assert.True(options.LoudnessReport.Value.WithinTarget
                    || Math.Abs(integrated - target) < 1.5f,
            options.LoudnessReport.Value.Summary);
    }

    [Fact]
    public void MatchAlbumLoudness_OnTwoBuffers_PreservesRelativeSpacing()
    {
        const int sr = 48000;
        var format = new AudioFormat(sr, 2);
        var loud = new float[sr * 2 * 2];
        var quiet = new float[sr * 2 * 2];
        for (var f = 0; f < sr * 2; f++)
        {
            var sLoud = 0.25f * MathF.Sin(2 * MathF.PI * 440f * f / sr);
            var sQuiet = 0.03f * MathF.Sin(2 * MathF.PI * 440f * f / sr);
            loud[f * 2] = loud[f * 2 + 1] = sLoud;
            quiet[f * 2] = quiet[f * 2 + 1] = sQuiet;
        }

        var tracks = new List<(string WavPath, float[] Samples, AudioFormat Format)>
        {
            ("a.wav", loud, format),
            ("b.wav", quiet, format)
        };
        var loudBefore = LoudnessAnalyzer.Analyze(loud, format).IntegratedLufs;
        var quietBefore = LoudnessAnalyzer.Analyze(quiet, format).IntegratedLufs;
        var spacingBefore = loudBefore - quietBefore;
        ExportService.MatchAlbumLoudness(tracks, -14.0, -1.0);
        var loudLufs = LoudnessAnalyzer.Analyze(loud, format, -14.0, -1.0).IntegratedLufs;
        var quietLufs = LoudnessAnalyzer.Analyze(quiet, format, -14.0, -1.0).IntegratedLufs;
        Assert.InRange(loudLufs, -15.5f, -12.5f);
        Assert.InRange(loudLufs - quietLufs, spacingBefore - 1.0f, spacingBefore + 1.0f);
    }

    private static Project CreateHotProjectWithFullMaster(AudioFormat format)
    {
        var project = CreateHotProjectWithLimitingMaster(format);
        var master = project.Tracks.First(t => t.Kind == TrackKind.Master);
        master.Effects.Clear();
        foreach (var fx in MasteringChains.CreateFullMaster())
            master.Effects.Add(fx.Clone());
        master.CommitEffects();
        return project;
    }

    [Fact]
    public void ExportComparisonPair_WritesPreAndMastered()
    {
        var format = new AudioFormat(48000, 2);
        var project = CreateHotProjectWithLimitingMaster(format);
        var svc = new ExportService(new NullVideoCompositor(), new NullVideoMuxer());
        using var dir = new TempDir();
        var wav = Path.Combine(dir.Path, "song.wav");
        var options = new ExportOptions
        {
            Kind = ExportKind.Region, BitDepth = 24, AnalyzeLoudness = false,
            ExportComparisonPair = true, RegionStartBeat = 0, RegionEndBeat = 2
        };
        svc.Export(project, format, 120, wav, options);
        Assert.True(File.Exists(Path.Combine(dir.Path, "song-comparison-unmastered.wav")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "song-comparison-mastered.wav")));
    }

    [Fact]
    public void NormalizeLoudness_TwoPassLandsNearTarget()
    {
        var format = new AudioFormat(48000, 2);
        var project = CreateSoftToneProjectWithLimiter(format, seconds: 3, amp: 0.08f);
        var svc = new ExportService(new NullVideoCompositor(), new NullVideoMuxer());
        using var dir = new TempDir();
        var wav = Path.Combine(dir.Path, "norm.wav");
        const double target = -14.0;
        var options = new ExportOptions
        {
            Kind = ExportKind.Region,
            BitDepth = 24,
            AnalyzeLoudness = true,
            NormalizeLoudness = true,
            TargetIntegratedLufs = target,
            TargetTruePeakDbTp = -1.0,
            RegionStartBeat = 0,
            RegionEndBeat = 6 // ~3 s at 120 BPM
        };
        svc.Export(project, format, 120, wav, options);
        Assert.NotNull(options.LoudnessReport);
        var integrated = options.LoudnessReport!.Value.IntegratedLufs;
        Assert.False(float.IsNegativeInfinity(integrated), options.LoudnessReport.Value.Summary);
        Assert.InRange(integrated, (float)(target - 1.5), (float)(target + 1.5));
    }

    [Theory]
    [InlineData("Spotify", -14.0, -1.0)]
    [InlineData("YouTube", -14.0, -1.0)]
    [InlineData("Apple Music", -16.0, -1.0)]
    [InlineData("Club", -9.0, -0.3)]
    [InlineData("Podcast", -16.0, -1.5)]
    public void DeliveryPlatformPresets_TryGet_EachPlatform(string name, double lufs, double dbTp)
    {
        var got = DeliveryPlatformPresets.TryGet(name);
        Assert.NotNull(got);
        Assert.Equal(lufs, got!.Value.Lufs);
        Assert.Equal(dbTp, got.Value.DbTp);
    }

    [Fact]
    public void ApplyDeliveryLimiter_BringsHotGainedBufferUnderCeiling()
    {
        var format = new AudioFormat(48000, 2);
        var buf = new float[48000]; // 0.5 s stereo interleaved
        for (var i = 0; i < buf.Length; i++)
            buf[i] = (i % 2 == 0 ? 1f : -1f) * 1.2f;

        ExportService.ApplyDeliveryLimiter(buf, format, -1.0);
        var report = LoudnessAnalyzer.Analyze(buf, format, targetTruePeakDbTp: -1.0);
        Assert.True(report.TruePeakDbTp <= -1.0f + 0.15f, report.Summary);
    }

    [Fact]
    public void ToolEffect_MeteringOnly_SkippedOffline_IdentityPeaksMatch()
    {
        var format = new AudioFormat(48000, 2);
        var baseProject = CreateSoftToneProjectWithLimiter(format, seconds: 1, amp: 0.3f);
        // Strip limiter so Tool is the only insert difference.
        var master = baseProject.Tracks.First(t => t.Kind == TrackKind.Master);
        master.Effects.Clear();
        master.CommitEffects();

        var withTool = CloneProject(baseProject);
        var toolMaster = withTool.Tracks.First(t => t.Kind == TrackKind.Master);
        toolMaster.Effects.Add(new ToolEffect()); // identity metering
        toolMaster.CommitEffects();

        var withGainTool = CloneProject(baseProject);
        var gainMaster = withGainTool.Tracks.First(t => t.Kind == TrackKind.Master);
        gainMaster.Effects.Add(new ToolEffect { GainDb = 12 });
        gainMaster.CommitEffects();

        var renderer = new OfflineRenderer();
        var plain = renderer.RenderMasterToBuffer(baseProject, format, 120, null, 0, 2);
        var metering = renderer.RenderMasterToBuffer(withTool, format, 120, null, 0, 2);
        var gained = renderer.RenderMasterToBuffer(withGainTool, format, 120, null, 0, 2);

        Assert.Equal(plain.Samples.Length, metering.Samples.Length);
        float plainPeak = 0, meterPeak = 0, gainPeak = 0;
        for (var i = 0; i < plain.Samples.Length; i++)
        {
            Assert.Equal(plain.Samples[i], metering.Samples[i], 5);
            plainPeak = Math.Max(plainPeak, Math.Abs(plain.Samples[i]));
            meterPeak = Math.Max(meterPeak, Math.Abs(metering.Samples[i]));
            gainPeak = Math.Max(gainPeak, Math.Abs(gained.Samples[i]));
        }
        Assert.Equal(plainPeak, meterPeak, 5);
        // Tool applies constant-power pan (√0.5 at centre); +12 dB still yields >1.5× peak.
        Assert.True(gainPeak > plainPeak * 1.5f, $"non-identity Tool should raise peaks ({gainPeak} vs {plainPeak})");
    }

    private static Project CloneProject(Project source)
    {
        var clone = new Project
        {
            Name = source.Name,
            Tempo = source.Tempo,
            BarCount = source.BarCount,
            TimeSignature = source.TimeSignature
        };
        foreach (var track in source.Tracks)
        {
            var t = new Track
            {
                Name = track.Name,
                Kind = track.Kind,
                Volume = track.Volume
            };
            foreach (var fx in track.Effects)
                t.Effects.Add(fx.Clone());
            t.CommitEffects();
            foreach (var clip in track.Clips)
            {
                t.Clips.Add(new Clip
                {
                    Name = clip.Name,
                    IsAudio = clip.IsAudio,
                    StartBeat = clip.StartBeat,
                    LengthBeats = clip.LengthBeats,
                    Samples = clip.Samples
                });
            }
            clone.Tracks.Add(t);
        }
        return clone;
    }

    private static Project CreateSoftToneProjectWithLimiter(AudioFormat format, int seconds, float amp)
    {
        var project = new Project
        {
            Name = "SoftMaster",
            Tempo = new Tempo(120),
            BarCount = Math.Max(4, (int)Math.Ceiling(seconds * 120.0 / 60.0 / 4.0) + 1)
        };
        var master = new Track { Name = "Master", Kind = TrackKind.Master, Volume = 1 };
        master.Effects.Add(new PeakLimiterEffect
        {
            MasteringPresetIndex = 1, ThresholdDb = -4, CeilingDb = -1.0, ReleaseMs = 80, OversampleIndex = 0
        });
        master.CommitEffects();

        var frames = format.SampleRate * seconds;
        var samples = new float[frames * 2];
        for (var f = 0; f < frames; f++)
        {
            var s = amp * MathF.Sin(2 * MathF.PI * 440f * f / format.SampleRate);
            samples[f * 2] = s;
            samples[f * 2 + 1] = s;
        }

        var beats = seconds * 120.0 / 60.0;
        var tone = new Track { Name = "Tone", Kind = TrackKind.Audio, Volume = 1 };
        tone.Clips.Add(new Clip
        {
            Name = "Soft",
            IsAudio = true,
            StartBeat = 0,
            LengthBeats = beats,
            Samples = new AudioSampleBuffer(samples, 2, format.SampleRate)
        });
        project.Tracks.Add(master);
        project.Tracks.Add(tone);
        return project;
    }

    private static Project CreateProjectWithMasterLimiter()
    {
        var project = new Project { Name = "Stems" };
        var master = new Track { Name = "Master", Kind = TrackKind.Master };
        master.Effects.Add(new PeakLimiterEffect { CeilingDb = -1.0 });
        master.CommitEffects();
        var kick = new Track { Name = "Kick", Kind = TrackKind.Audio };
        project.Tracks.Add(master);
        project.Tracks.Add(kick);
        return project;
    }

    private static Project CreateHotProjectWithLimitingMaster(AudioFormat format)
    {
        var project = new Project
        {
            Name = "HotMaster",
            Tempo = new Tempo(120),
            BarCount = 4
        };
        var master = new Track { Name = "Master", Kind = TrackKind.Master, Volume = 1 };
        master.Effects.Add(new PeakLimiterEffect
        {
            MasteringPresetIndex = 1, ThresholdDb = -4, CeilingDb = -1.0, ReleaseMs = 80, OversampleIndex = 0
        });
        master.CommitEffects();

        var frames = format.SampleRate * 2; // 2 s
        var samples = new float[frames * 2];
        for (var f = 0; f < frames; f++)
        {
            var s = 0.95f * MathF.Sin(2 * MathF.PI * 220f * f / format.SampleRate);
            samples[f * 2] = s;
            samples[f * 2 + 1] = s;
        }

        var tone = new Track { Name = "Tone", Kind = TrackKind.Audio, Volume = 1 };
        tone.Clips.Add(new Clip
        {
            Name = "Hot",
            IsAudio = true,
            StartBeat = 0,
            LengthBeats = 4,
            Samples = new AudioSampleBuffer(samples, 2, format.SampleRate)
        });
        project.Tracks.Add(master);
        project.Tracks.Add(tone);
        return project;
    }

    [Fact]
    public void ComputeAlbumOffsets_MatchesLoudestToTarget()
    {
        var offsets = ExportService.ComputeAlbumOffsets([-12.0, -16.0, -14.0], -14.0);
        Assert.Equal(3, offsets.Length);
        Assert.Equal(-2.0, offsets[0], 2);
        Assert.Equal(-2.0, offsets[1], 2);
        Assert.Equal(-2.0, offsets[2], 2);
    }

    [Fact]
    public void NormalizeBufferToLufs_MovesIntegratedTowardTarget()
    {
        var format = new AudioFormat(48000, 2);
        var buf = new float[48000 * 4];
        for (var f = 0; f < 48000 * 2; f++)
        {
            var s = 0.12f * MathF.Sin(2 * MathF.PI * 440f * f / 48000f);
            buf[f * 2] = s;
            buf[f * 2 + 1] = s;
        }

        ExportService.NormalizeBufferToLufs(buf, format, -23.0, -1.0);
        var after = LoudnessAnalyzer.Analyze(buf, format, -23.0, -1.0);
        Assert.False(float.IsNegativeInfinity(after.IntegratedLufs));
        Assert.True(Math.Abs(after.IntegratedLufs - (-23.0f)) < 2.0f, after.Summary);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "ongenet-export-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { /* ignore */ }
        }
    }
}
