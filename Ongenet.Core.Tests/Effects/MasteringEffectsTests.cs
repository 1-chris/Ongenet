using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Services;

namespace Ongenet.Core.Tests.Effects;

/// <summary>
/// Verifies the mastering tooling built for the trance master chain: the mid/side EQ (mono-folds the
/// sub-bass, brightens the sides) and the soft clipper (peaks asymptote toward the ceiling).
/// </summary>
public class MasteringEffectsTests
{
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

    private static double Rms(ReadOnlySpan<float> buf)
    {
        double sum = 0;
        for (var i = 0; i < buf.Length; i++) sum += buf[i] * (double)buf[i];
        return Math.Sqrt(sum / buf.Length);
    }

    [Fact]
    public void MidSideEqMonoFoldsLowSideEnergy()
    {
        const double sr = 48000;
        var fx = new MidSideEqEffect { SideLowCutHz = 120, SideAirDb = 0 };
        fx.Prepare(new AudioFormat((int)sr, 2));

        // A pure side signal (L = +x, R = -x) at 50 Hz — entirely below the 120 Hz cut.
        var buf = SineStereo(50, 4096, sr, 1.0, -1.0);
        var before = Rms(buf);
        fx.Process(buf);
        var after = Rms(buf);

        Assert.True(after < before * 0.5, "low side-channel energy should be strongly attenuated (folded to mono)");
    }

    [Fact]
    public void MidSideEqLeavesCentreLowEndIntact()
    {
        const double sr = 48000;
        var fx = new MidSideEqEffect { SideLowCutHz = 120, SideAirDb = 0 };
        fx.Prepare(new AudioFormat((int)sr, 2));

        // A pure mid signal (L = R) at 50 Hz — the side channel is silent, so nothing is touched.
        var buf = SineStereo(50, 4096, sr, 1.0, 1.0);
        var before = Rms(buf);
        fx.Process(buf);
        var after = Rms(buf);

        Assert.Equal(before, after, 3);
    }

    [Fact]
    public void ClipperKeepsPeaksUnderTheCeiling()
    {
        var fx = new ClipperEffect { DriveDb = 6, CeilingDb = -1.0 };
        fx.Prepare(new AudioFormat(48000, 2));

        var buf = new float[512];
        for (var i = 0; i < buf.Length; i++) buf[i] = i % 2 == 0 ? 1.5f : -1.5f; // hot, over 0 dBFS

        fx.Process(buf);

        var ceiling = (float)Math.Pow(10, -1.0 / 20.0);
        foreach (var s in buf)
            Assert.True(Math.Abs(s) <= ceiling + 1e-4f, "soft clip must hold the signal below the ceiling");
    }

    [Fact]
    public void ClipperIsNearTransparentForQuietSignal()
    {
        var fx = new ClipperEffect { DriveDb = 0, CeilingDb = -0.3, OversampleIndex = 0 };
        fx.Prepare(new AudioFormat(48000, 2));

        var buf = new float[256];
        for (var i = 0; i < buf.Length; i++) buf[i] = 0.05f * (i % 2 == 0 ? 1 : -1);
        var copy = (float[])buf.Clone();

        fx.Process(buf);
        for (var i = 0; i < buf.Length; i++)
            Assert.Equal(copy[i], buf[i], 2); // small signals pass essentially untouched
    }

    [Fact]
    public void BothEffectsAreInTheRegistry()
    {
        var reg = new EffectRegistry();
        Assert.IsType<MidSideEqEffect>(reg.Create(MidSideEqEffect.TypeId));
        Assert.IsType<ClipperEffect>(reg.Create(ClipperEffect.TypeId));
        Assert.IsType<MultibandCompressorEffect>(reg.Create(MultibandCompressorEffect.TypeId));
    }

    [Fact]
    public void MultibandCompressorInflatesQuietSignals()
    {
        const double sr = 48000;
        var fx = new MultibandCompressorEffect { Depth = 1.0, HighBoostDb = 0 };
        fx.Prepare(new AudioFormat((int)sr, 2));
        // Consume the mastering preset once so Depth/HighBoost are not overwritten mid-test.
        fx.Process(new float[4]);
        fx.Depth = 1.0;
        fx.HighBoostDb = 0;

        // A steady mid-band tone well below the lower threshold — upward compression should lift it.
        var buf = SineStereo(700, 16384, sr, 0.01, 0.01); // ~-40 dBFS
        var before = Rms(buf);
        fx.Process(buf);
        var after = Rms(buf);

        Assert.True(after > before * 1.5, "upward compression should inflate quiet detail");
    }

    [Fact]
    public async Task MultibandCompressor_ConcurrentPrepareAndProcess_DoesNotThrow()
    {
        var fx = new MultibandCompressorEffect { Depth = 0.8, HighBoostDb = 3.0 };
        fx.Prepare(new AudioFormat(44100, 2));
        var buf = SineStereo(700, 512, 44100, 0.05, 0.05);
        Exception? caught = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var audio = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try { fx.Process(buf); }
                catch (Exception ex) { caught = ex; return; }
            }
        });

        var ui = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                fx.Prepare(new AudioFormat(48000, 2));
                fx.Prepare(new AudioFormat(44100, 2));
            }
        });

        await Task.WhenAll(audio, ui);
        Assert.Null(caught);
    }

    [Fact]
    public void PingPongDelayBouncesEnergyToTheOppositeChannel()
    {
        const double sr = 48000;
        var frames = 16384; // long enough to catch the bounce (delay = 4800 samples)

        float[] Run(bool pingPong)
        {
            var fx = new DelayEffect { TimeMs = 100, Feedback = 0.5, Mix = 1.0, PingPong = pingPong };
            fx.Prepare(new AudioFormat((int)sr, 2));
            var buf = new float[frames * 2];
            buf[0] = 1.0f; // a single impulse on the LEFT channel only
            fx.Process(buf);
            return buf;
        }

        double RightTail(float[] buf)
        {
            double sum = 0;
            for (var i = 0; i < frames; i++) sum += Math.Abs(buf[i * 2 + 1]);
            return sum;
        }

        // A plain stereo delay leaves the right channel silent for a left-only input.
        Assert.True(RightTail(Run(pingPong: false)) < 1e-4);

        // Ping-pong cross-feeds the echo, so the right channel now receives energy.
        Assert.True(RightTail(Run(pingPong: true)) > 0.1, "ping-pong should bounce the echo across channels");
    }

    [Fact]
    public async Task DelayEffect_ConcurrentPrepareAndProcess_DoesNotThrow()
    {
        var fx = new DelayEffect { TimeMs = 100, Feedback = 0.4, Mix = 0.5, PingPong = true };
        fx.Prepare(new AudioFormat(44100, 2));
        var buf = new float[512 * 2];
        buf[0] = 1f;
        Exception? caught = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var audio = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try { fx.Process(buf); }
                catch (Exception ex) { caught = ex; return; }
            }
        });

        var ui = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                fx.Prepare(new AudioFormat(48000, 2));
                fx.Prepare(new AudioFormat(44100, 2));
            }
        });

        await Task.WhenAll(audio, ui);
        Assert.Null(caught);
    }

    [Fact]
    public void FullMasterChainHasCanonicalTypeOrder()
    {
        var chain = MasteringChains.CreateFullMaster();
        Assert.Equal(7, chain.Length);
        Assert.IsType<EqEffect>(chain[0]);
        Assert.IsType<MidSideEqEffect>(chain[1]);
        Assert.IsType<CompressorEffect>(chain[2]);
        Assert.IsType<StereoWidthEffect>(chain[3]);
        Assert.IsType<ClipperEffect>(chain[4]);
        Assert.IsType<PeakLimiterEffect>(chain[5]);
        Assert.IsType<SpectrumEffect>(chain[6]);

        var lim = (PeakLimiterEffect)chain[5];
        Assert.Equal(-1.0, lim.CeilingDb);
        Assert.Equal(1, lim.MasteringPresetIndex); // Streaming
    }

    [Fact]
    public void PeakLimiterKeepsPeaksAtOrUnderCeiling()
    {
        // Streaming preset (−4 / −1.0 / 80 ms) — ApplyPresetIfChanged runs on first Process.
        var fx = new PeakLimiterEffect { MasteringPresetIndex = 1 };
        fx.Prepare(new AudioFormat(48000, 2));
        var buf = new float[512];
        for (var i = 0; i < buf.Length; i++) buf[i] = i % 2 == 0 ? 0.95f : -0.95f;

        fx.Process(buf);

        var ceiling = (float)AudioMath.Db2Lin(fx.CeilingDb);
        Assert.Equal(-1.0, fx.CeilingDb);
        for (var i = 0; i < buf.Length; i++)
            Assert.True(Math.Abs(buf[i]) <= ceiling + 1e-5f, $"sample {i}={buf[i]} over ceiling {ceiling}");
    }

    [Fact]
    public void PeakLimiterReportsGainReductionWhenDriven()
    {
        var fx = new PeakLimiterEffect { MasteringPresetIndex = 1 };
        fx.Prepare(new AudioFormat(48000, 1));
        var buf = new float[256];
        for (var i = 0; i < buf.Length; i++) buf[i] = 1.0f;

        fx.Process(buf);

        Assert.True(fx.GainReductionDb < -0.5, $"expected significant GR, got {fx.GainReductionDb}");
    }

    [Fact]
    public void PeakLimiterSpectralModeChangesGainReductionForTransientTail()
    {
        static double Run(bool spectral)
        {
            var fx = new PeakLimiterEffect
            {
                MasteringPresetIndex = 0, ThresholdDb = -6, CeilingDb = -1,
                ReleaseMs = 40, SpectralLimiter = spectral, OversampleIndex = 0
            };
            fx.Prepare(new AudioFormat(48000, 2));
            fx.Process(new float[16]); // apply the preset once, then compare identical explicit settings
            fx.ThresholdDb = -6;
            fx.CeilingDb = -1;
            fx.ReleaseMs = 40;
            fx.SpectralLimiter = spectral;
            var transient = new float[4096];
            Array.Fill(transient, 1f);
            fx.Process(transient);
            var tail = new float[512];
            for (var block = 0; block < 12; block++) fx.Process(tail);
            return fx.GainReductionDb;
        }

        var normal = Run(false);
        var spectral = Run(true);
        Assert.NotEqual(normal, spectral);
        Assert.True(spectral < normal - 0.1,
            $"spectral follower should hold more GR into a transient tail ({spectral:0.00} vs {normal:0.00})");
    }

    [Fact]
    public void PeakLimiterAppliesMasteringPreset()
    {
        var streaming = MasteringPresetBank.GetLimiter(1);
        var fx = new PeakLimiterEffect { MasteringPresetIndex = 1 };
        fx.Prepare(new AudioFormat(48000, 2));
        // Process once so ApplyPresetIfChanged runs.
        fx.Process(new float[64]);

        Assert.Equal(streaming.ThresholdDb, fx.ThresholdDb);
        Assert.Equal(streaming.CeilingDb, fx.CeilingDb);
        Assert.Equal(streaming.ReleaseMs, fx.ReleaseMs);
        Assert.Equal(streaming.Spectral, fx.SpectralLimiter);
    }

    [Fact]
    public void CompressorReportsGainReductionWhenAboveThreshold()
    {
        var fx = new CompressorEffect
        {
            ThresholdDb = -24, Ratio = 4.0, AttackMs = 0.1, ReleaseMs = 50, MakeupDb = 0
        };
        fx.Prepare(new AudioFormat(48000, 1));
        var buf = new float[512];
        for (var i = 0; i < buf.Length; i++) buf[i] = 0.8f;

        fx.Process(buf);

        Assert.True(fx.GainReductionDb < -1.0, $"expected GR, got {fx.GainReductionDb}");
    }

    [Fact]
    public void MultibandPresetSetsDepthAndDescription()
    {
        var ott = MasteringPresetBank.GetMultiband(2);
        var fx = new MultibandCompressorEffect { MasteringPresetIndex = 2 };
        fx.Prepare(new AudioFormat(48000, 2));
        fx.Process(new float[128]);
        Assert.Equal(ott.Depth, fx.Depth, 3);
        Assert.False(string.IsNullOrWhiteSpace(ott.Description));
        Assert.False(string.IsNullOrWhiteSpace(MasteringPresetBank.GetLimiter(1).Description));
    }

    [Fact]
    public void StereoWidthNarrowsAntiPhaseSignal()
    {
        var fx = new StereoWidthEffect { Width = 0 };
        fx.Prepare(new AudioFormat(48000, 2));
        var buf = SineStereo(440, 512, 48000, 0.5, -0.5);
        fx.Process(buf);
        // Width 0 collapses to mid → L≈R
        for (var i = 0; i < 20; i++)
            Assert.Equal(buf[i * 2], buf[i * 2 + 1], 3);
    }

    [Fact]
    public void TruePeakMeterReportsIspAboveSamplePeak()
    {
        // Classic ISP: fs/4 sine sampled at π/4 → sample peak ≈ 0.707, continuous peak ≈ 1.0.
        var format = new AudioFormat(48000, 2);
        var tp = new TruePeakMeter();
        tp.Prepare(format);

        var frames = 4096;
        var buf = new float[frames * 2];
        float samplePeak = 0;
        for (var f = 0; f < frames; f++)
        {
            // s[n] = sin(π/2·n + π/4) → ±√2/2 at sample points, continuous peak = 1.
            var s = MathF.Sin(MathF.PI * 0.5f * f + MathF.PI * 0.25f);
            var a = MathF.Abs(s);
            if (a > samplePeak) samplePeak = a;
            buf[f * 2] = s;
            buf[f * 2 + 1] = s;
        }

        tp.Process(buf);
        var samplePeakDb = TruePeakMeter.ToDbTp(samplePeak);
        Assert.True(tp.MaxDbTp > samplePeakDb + 1.0f,
            $"expected ISP (dBTP {tp.MaxDbTp:F2}) > sample peak ({samplePeakDb:F2} dBFS) by >1 dB");
        Assert.True(tp.MaxDbTp > -1.5f, $"expected near-0 dBTP after ISP, got {tp.MaxDbTp}");
    }

    [Fact]
    public void LoudnessMeterSteadyToneNearMinus23Lufs()
    {
        // Stereo 1 kHz at a level chosen so gated integrated ≈ −23 LUFS (±0.5 LU).
        // Empirically: K-weighting at 1 kHz ≈ +0.7 dB vs unweighted; stereo sum-of-z formula.
        var format = new AudioFormat(48000, 2);
        var loud = new LoudnessMeter();
        loud.Prepare(format);

        // Target: −23 LUFS stereo → mean (zL+zR) ≈ 10^((-23+0.691)/10).
        // With equal channels zL=zR=z → 2z = that mean → z = half.
        // Unweighted RMS for sine = amp/√2; after K-weight ≈ amp/√2 * 1.08 at 1kHz.
        // Tune amp so integrated settles near −23.
        var amp = 0.0707f; // ≈ −23 dBFS unweighted stereo sine → ≈ −23 LUFS after K @ 1 kHz
        var block = new float[4800]; // 100 ms
        var phase = 0.0;
        var inc = 2 * Math.PI * 1000.0 / 48000.0;
        for (var n = 0; n < 50; n++) // 5 s
        {
            for (var i = 0; i < block.Length; i += 2)
            {
                var s = amp * (float)Math.Sin(phase);
                phase += inc;
                block[i] = s;
                block[i + 1] = s;
            }
            loud.Process(block);
        }

        Assert.InRange(loud.IntegratedLufs, -23.5f, -22.5f);
        Assert.InRange(loud.MomentaryLufs, -24.0f, -21.5f);
        Assert.InRange(loud.ShortTermLufs, -24.0f, -21.5f);
    }

    [Fact]
    public void LoudnessMeterSteadyToneNearMinus18Lufs()
    {
        var format = new AudioFormat(48000, 2);
        var loud = new LoudnessMeter();
        loud.Prepare(format);
        var amp = 0.1259f; // ≈ −18 dBFS unweighted stereo → ≈ −18 LUFS
        var block = new float[4800];
        var phase = 0.0;
        var inc = 2 * Math.PI * 1000.0 / 48000.0;
        for (var n = 0; n < 50; n++)
        {
            for (var i = 0; i < block.Length; i += 2)
            {
                var s = amp * (float)Math.Sin(phase);
                phase += inc;
                block[i] = s;
                block[i + 1] = s;
            }
            loud.Process(block);
        }

        Assert.InRange(loud.IntegratedLufs, -18.5f, -17.5f);
    }

    [Fact]
    public void FullMasterOfflineRenderKeepsPeaksUnderStreamingCeiling()
    {
        // Hot stereo tone through Full Master → Peak Limiter Streaming (−1.0 dBFS).
        var format = new AudioFormat(48000, 2);
        var chain = MasteringChains.CreateFullMaster();
        foreach (var fx in chain) fx.Prepare(format);

        var buf = new float[48000]; // 0.5 s mono interleaved as stereo
        for (var i = 0; i < buf.Length; i++)
            buf[i] = 0.95f * (float)Math.Sin(2 * Math.PI * 220 * (i / 2) / 48000.0);

        // Process in blocks
        for (var off = 0; off < buf.Length; off += 1024)
        {
            var len = Math.Min(1024, buf.Length - off);
            var slice = buf.AsSpan(off, len);
            foreach (var fx in chain)
                fx.Process(slice);
        }

        float peak = 0;
        foreach (var s in buf)
        {
            var a = Math.Abs(s);
            if (a > peak) peak = a;
        }
        var ceiling = (float)Math.Pow(10, -1.0 / 20.0);
        Assert.True(peak <= ceiling + 0.02f, $"peak {peak} exceeds Streaming ceiling {ceiling}");

        var tp = new TruePeakMeter();
        tp.Prepare(format);
        for (var off = 0; off < buf.Length; off += 2048)
            tp.Process(buf.AsSpan(off, Math.Min(2048, buf.Length - off)));
        Assert.True(tp.MaxDbTp <= -1.0f + 0.15f,
            $"true peak {tp.MaxDbTp:F2} dBTP exceeds Streaming ceiling −1.0 dBTP");
    }

    [Fact]
    public void FullMasterHotSineTruePeakWithinStreamingCeiling()
    {
        var format = new AudioFormat(48000, 2);
        var chain = MasteringChains.CreateFullMaster();
        foreach (var fx in chain) fx.Prepare(format);

        var frames = 48000;
        var buf = new float[frames * 2];
        for (var f = 0; f < frames; f++)
        {
            var s = 0.98f * MathF.Sin(2 * MathF.PI * 440f * f / 48000f);
            buf[f * 2] = s;
            buf[f * 2 + 1] = s;
        }

        for (var off = 0; off < buf.Length; off += 1024)
        {
            var len = Math.Min(1024, buf.Length - off);
            var slice = buf.AsSpan(off, len);
            foreach (var fx in chain)
                if (fx is not IAnalyserOnlyEffect)
                    fx.Process(slice);
        }

        var report = LoudnessAnalyzer.Analyze(buf, format, targetTruePeakDbTp: -1.0);
        Assert.True(report.TruePeakDbTp <= -1.0f + 0.15f,
            $"Full Master true peak {report.TruePeakDbTp:F2} dBTP exceeds −1.0 ({report.Summary})");
    }

    [Fact]
    public void FullMasterPlusInsertsMultiband()
    {
        var chain = MasteringChains.CreateFullMasterPlus();
        Assert.Contains(chain, fx => fx is MultibandCompressorEffect);
    }

    [Fact]
    public void SpectrumImplementsAnalyserOnlyMarker()
    {
        Assert.IsAssignableFrom<IAnalyserOnlyEffect>(new SpectrumEffect());
        Assert.IsAssignableFrom<IAnalyserOnlyEffect>(new WaveformVisualizerEffect());
    }

    [Fact]
    public void ClipperOversampleDoesNotThrow()
    {
        var fx = new ClipperEffect { OversampleIndex = 2, DriveDb = 3, CeilingDb = -0.5 };
        fx.Prepare(new AudioFormat(48000, 2));
        var buf = new float[256];
        for (var i = 0; i < buf.Length; i++) buf[i] = 0.9f;
        fx.Process(buf);
        foreach (var s in buf)
            Assert.True(Math.Abs(s) <= 1.01f);
    }

    public static IEnumerable<object[]> MasteringChainTypeOrders()
    {
        yield return new object[]
        {
            "full",
            new Type[]
            {
                typeof(EqEffect), typeof(MidSideEqEffect), typeof(CompressorEffect), typeof(StereoWidthEffect),
                typeof(ClipperEffect), typeof(PeakLimiterEffect), typeof(SpectrumEffect)
            }
        };
        yield return new object[]
        {
            "full+",
            new Type[]
            {
                typeof(EqEffect), typeof(MidSideEqEffect), typeof(CompressorEffect), typeof(MultibandCompressorEffect),
                typeof(StereoWidthEffect), typeof(ClipperEffect), typeof(PeakLimiterEffect), typeof(SpectrumEffect)
            }
        };
        yield return new object[]
        {
            "streaming",
            new Type[] { typeof(EqEffect), typeof(CompressorEffect), typeof(PeakLimiterEffect), typeof(SpectrumEffect) }
        };
        yield return new object[]
        {
            "premaster",
            new Type[] { typeof(DcOffsetEffect), typeof(EqEffect), typeof(MidSideEqEffect), typeof(CompressorEffect) }
        };
        yield return new object[]
        {
            "club",
            new Type[]
            {
                typeof(MultibandCompressorEffect), typeof(StereoWidthEffect), typeof(OverEffect),
                typeof(ClipperEffect), typeof(PeakLimiterEffect)
            }
        };
        yield return new object[]
        {
            "podcast",
            new Type[] { typeof(EqEffect), typeof(DeEsserEffect), typeof(CompressorEffect), typeof(PeakLimiterEffect) }
        };
        yield return new object[]
        {
            "glue",
            new Type[] { typeof(CompressorEffect), typeof(StereoWidthEffect), typeof(PeakLimiterEffect) }
        };
        yield return new object[]
        {
            "techno",
            new Type[]
            {
                typeof(FilterEffect), typeof(MultibandCompressorEffect), typeof(StereoWidthEffect),
                typeof(ExciterEffect), typeof(PeakLimiterEffect)
            }
        };
        yield return new object[]
        {
            "audiophile",
            new Type[]
            {
                typeof(LinearPhaseEqEffect), typeof(MidSideEqEffect), typeof(CompressorEffect),
                typeof(StereoWidthEffect), typeof(ClipperEffect), typeof(PeakLimiterEffect), typeof(SpectrumEffect)
            }
        };
        yield return new object[]
        {
            "reference",
            new Type[]
            {
                typeof(EqEffect), typeof(MatchEqEffect), typeof(CompressorEffect),
                typeof(PeakLimiterEffect), typeof(SpectrumEffect)
            }
        };
    }

    [Theory]
    [MemberData(nameof(MasteringChainTypeOrders))]
    public void MasteringChainsCreate_TypeOrder(string name, Type[] expected)
    {
        var chain = MasteringChains.Create(name);
        Assert.Equal(expected.Length, chain.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.IsType(expected[i], chain[i]);
    }

    [Fact]
    public void ReferenceMaster_ContainsMatchEqAndLimiterIsLastProcessingInsert()
    {
        var chain = MasteringChains.Create("reference");

        Assert.Contains(chain, effect => effect is MatchEqEffect);
        Assert.IsType<PeakLimiterEffect>(chain.Last(effect => effect is not IAnalyserOnlyEffect));
    }

    [Theory]
    [InlineData("Spotify", -1.0)]
    [InlineData("Apple Music", -1.0)]
    [InlineData("Club", -0.3)]
    [InlineData("Podcast", -1.5)]
    public void PeakLimiterCeiling_CanSyncToDeliveryTarget(string platform, double expectedCeiling)
    {
        var target = new MasteringDeliveryTarget();
        target.ApplyPlatform(platform);
        var limiter = new PeakLimiterEffect { CeilingDb = target.TargetTruePeakDbTp };

        Assert.Equal(expectedCeiling, limiter.CeilingDb);
    }

    [Theory]
    [InlineData("full")]
    [InlineData("full+")]
    [InlineData("streaming")]
    [InlineData("premaster")]
    [InlineData("club")]
    [InlineData("podcast")]
    [InlineData("glue")]
    [InlineData("techno")]
    [InlineData("audiophile")]
    [InlineData("reference")]
    public void MasteringChains_DoNotContainTemporaryCompareTools(string name)
    {
        Assert.DoesNotContain(MasteringChains.Create(name), effect => effect is ToolEffect);
    }

    [Theory]
    [InlineData(0, -0.3)] // Transparent
    [InlineData(2, -0.5)] // Loud
    [InlineData(3, -0.3)] // Master
    [InlineData(4, -1.5)] // Safety
    public void LimiterPresets_ApplyCeilings(int presetIndex, double expectedCeiling)
    {
        var preset = MasteringPresetBank.GetLimiter(presetIndex);
        Assert.Equal(expectedCeiling, preset.CeilingDb);
        var fx = new PeakLimiterEffect { MasteringPresetIndex = presetIndex };
        fx.Prepare(new AudioFormat(48000, 2));
        fx.Process(new float[64]);
        Assert.Equal(expectedCeiling, fx.CeilingDb);
    }

    [Theory]
    [InlineData(0, 0.15)] // Transparent
    [InlineData(1, 0.35)] // Glue
    [InlineData(3, 0.75)] // Aggressive
    [InlineData(4, 1.0)]  // Max
    public void MultibandPresets_SetDepth(int presetIndex, double expectedDepth)
    {
        var preset = MasteringPresetBank.GetMultiband(presetIndex);
        Assert.Equal(expectedDepth, preset.Depth, 3);
        var fx = new MultibandCompressorEffect { MasteringPresetIndex = presetIndex };
        fx.Prepare(new AudioFormat(48000, 2));
        fx.Process(new float[128]);
        Assert.Equal(expectedDepth, fx.Depth, 3);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void FirOversampler_PeakSmoke(int factor)
    {
        var os = new FirOversampler();
        os.Prepare(factor, 256);
        var input = new float[64];
        input[8] = 1f;
        var up = new float[64 * factor];
        var down = new float[64];
        var upLen = os.Upsample(input, up);
        Assert.Equal(64 * factor, upLen);
        float upPeak = 0;
        for (var i = 0; i < upLen; i++)
            if (Math.Abs(up[i]) > upPeak) upPeak = Math.Abs(up[i]);
        Assert.True(upPeak > 0.2f, $"expected upsampled peak energy, got {upPeak}");

        var dnLen = os.Downsample(up.AsSpan(0, upLen), down);
        Assert.Equal(64, dnLen);
        float dnPeak = 0;
        for (var i = 0; i < dnLen; i++)
            if (Math.Abs(down[i]) > dnPeak) dnPeak = Math.Abs(down[i]);
        Assert.True(dnPeak > 0.15f, $"expected downsampled peak, got {dnPeak}");
    }

    [Fact]
    public void PcmDither_TpdfChangesQuietSample()
    {
        var dither = new PcmDither { Mode = DitherMode.Tpdf };
        dither.Reset(42);
        var a = dither.Process(0.0001f, 16);
        dither.Reset(42);
        var b = dither.Process(0.0001f, 16);
        Assert.Equal(a, b); // deterministic for same seed
        Assert.NotEqual(0.0001f, a);
    }

    [Fact]
    public void PcmDither_NoiseShaped_RunsWithoutThrow()
    {
        var dither = new PcmDither { Mode = DitherMode.NoiseShaped };
        dither.Reset(7);
        for (var i = 0; i < 256; i++)
            _ = dither.Process(0.01f * (i % 2 == 0 ? 1 : -1), 16);
    }

    [Fact]
    public void Multiband_LowCrossoverHz_ChangesFiltersSmoke()
    {
        var format = new AudioFormat(48000, 2);
        var fx = new MultibandCompressorEffect { Depth = 0.5, MasteringPresetIndex = 0 };
        fx.LowCrossoverHz = 80;
        fx.Prepare(format);
        var a = SineStereo(100, 2048, 48000, 0.2, 0.2);
        fx.Process(a);

        fx.LowCrossoverHz = 400;
        fx.Prepare(format);
        var b = SineStereo(100, 2048, 48000, 0.2, 0.2);
        fx.Process(b);

        var diff = 0.0;
        for (var i = 0; i < a.Length; i++)
            diff += Math.Abs(a[i] - b[i]);
        Assert.True(diff > 1e-3, "changing LowCrossoverHz should alter processed output");
    }

    [Fact]
    public void MidSideEq_SoloMidAndSoloSide_InParameters()
    {
        var fx = new MidSideEqEffect();
        var names = fx.Parameters.Select(p => p.Name).ToArray();
        Assert.Contains("Solo Mid", names);
        Assert.Contains("Solo Side", names);
    }

    [Fact]
    public void MatchEq_CaptureAndBlend_ChangesOutput()
    {
        const int sr = 48000;
        var format = new AudioFormat(sr, 2);
        var bright = SineStereo(4000, 8192, sr, 0.4, 0.4);
        var dull = SineStereo(200, 8192, sr, 0.4, 0.4);

        var fx = new MatchEqEffect { Blend = 0.8, Smoothness = 0.9 };
        fx.Prepare(format);
        fx.CaptureTargetFrom(bright, 2, sr);

        var buf = (float[])dull.Clone();
        var before = Rms(buf);
        fx.Process(buf);
        var after = Rms(buf);
        Assert.NotEqual(before, after, 3);
    }

    [Fact]
    public void ClubLoudChain_IncludesSoftOverBeforeLimiter()
    {
        var chain = MasteringChains.CreateClubLoud();
        var types = chain.Select(fx => fx.GetType()).ToList();
        Assert.Contains(typeof(OverEffect), types);
        Assert.True(types.IndexOf(typeof(OverEffect)) < types.IndexOf(typeof(PeakLimiterEffect)));
    }

    private static float[] SineMultichannel(double freq, int frames, double sr, int channels, float amp = 0.07f)
    {
        var buf = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * freq * f / sr);
            for (var c = 0; c < channels; c++)
                buf[f * channels + c] = s;
        }
        return buf;
    }

    private static void ProcessChunked(LoudnessAnalyzer analyzer, ReadOnlySpan<float> interleaved, int channels,
        int blockSamples)
    {
        channels = Math.Max(1, channels);
        for (var i = 0; i < interleaved.Length; i += blockSamples)
        {
            var len = Math.Min(blockSamples, interleaved.Length - i);
            len -= len % channels;
            if (len <= 0) break;
            analyzer.Process(interleaved.Slice(i, len));
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void LoudnessAnalyzer_SurroundSine_HasFiniteIntegrated(int channels)
    {
        const int sr = 48000;
        const int seconds = 3;
        var format = new AudioFormat(sr, channels);
        var buf = SineMultichannel(1000, sr * seconds, sr, channels);

        var oneShot = LoudnessAnalyzer.Analyze(buf, format);
        Assert.False(float.IsNegativeInfinity(oneShot.IntegratedLufs), oneShot.Summary);

        var chunkedA = new LoudnessAnalyzer();
        chunkedA.Prepare(format);
        ProcessChunked(chunkedA, buf, channels, 480 * channels);
        var reportA = chunkedA.Finish();

        var chunkedB = new LoudnessAnalyzer();
        chunkedB.Prepare(format);
        ProcessChunked(chunkedB, buf, channels, 960 * channels);
        var reportB = chunkedB.Finish();

        Assert.InRange(Math.Abs(oneShot.IntegratedLufs - reportA.IntegratedLufs), 0, 0.2f);
        Assert.InRange(Math.Abs(oneShot.IntegratedLufs - reportB.IntegratedLufs), 0, 0.2f);
    }

    [Fact]
    public void ToolEffect_Default_IsMeteringOnly_AndUnityPeak()
    {
        var fx = new ToolEffect();
        Assert.True(fx.IsMeteringOnly);
        var format = new AudioFormat(48000, 2);
        fx.Prepare(format);
        var buf = SineStereo(1000, 4800, 48000, 0.5, 0.5);
        var copy = (float[])buf.Clone();
        fx.Process(buf);
        float peakBefore = 0, peakAfter = 0;
        for (var i = 0; i < buf.Length; i++)
        {
            peakBefore = Math.Max(peakBefore, Math.Abs(copy[i]));
            peakAfter = Math.Max(peakAfter, Math.Abs(buf[i]));
            Assert.Equal(copy[i], buf[i], 5);
        }
        Assert.InRange(peakBefore, 0.45, 0.55);
        Assert.InRange(peakAfter, 0.45, 0.55);
    }

    [Fact]
    public void LoudnessMeter_AmplitudeModulatedSignal_ReportsPositiveLra()
    {
        const int sr = 48000;
        const int seconds = 12;
        var format = new AudioFormat(sr, 2);
        var meter = new LoudnessMeter();
        meter.Prepare(format);
        var block = new float[4800];
        var phase = 0.0;
        var carrierInc = 2 * Math.PI * 1000.0 / sr;
        for (var n = 0; n < seconds * 10; n++)
        {
            for (var i = 0; i < block.Length; i += 2)
            {
                var t = (n * block.Length + i) / (2.0 * sr);
                var env = 0.12 + 0.88 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.25 * t));
                var s = (float)(env * 0.08 * Math.Sin(phase));
                phase += carrierInc;
                block[i] = s;
                block[i + 1] = s;
            }
            meter.Process(block);
        }

        Assert.False(float.IsNegativeInfinity(meter.IntegratedLufs));
        Assert.True(meter.LoudnessRangeLu > 0.1f, $"expected LRA > 0, got {meter.LoudnessRangeLu}");
    }

    [Fact]
    public void LinearPhaseEqEffect_ReportsLatency_AndDelaysImpulsePeak()
    {
        var fx = new LinearPhaseEqEffect();
        Assert.Equal(128, fx.ReportedLatencySamples);
        fx.Prepare(new AudioFormat(48000, 1));
        var impulse = new float[512];
        impulse[0] = 1f;
        fx.Process(impulse);

        var peakIndex = 0;
        var peak = 0f;
        for (var i = 0; i < impulse.Length; i++)
        {
            var a = Math.Abs(impulse[i]);
            if (a <= peak) continue;
            peak = a;
            peakIndex = i;
        }

        Assert.InRange(peakIndex, 120, 136);
    }

    [Fact]
    public void MatchAlbumLoudness_MovesLouderTrackTowardTarget()
    {
        const int sr = 48000;
        var format = new AudioFormat(sr, 2);
        var loud = SineStereo(440, sr * 4, sr, 0.35, 0.35);
        var quiet = SineStereo(440, sr * 4, sr, 0.04, 0.04);
        var tracks = new List<(string WavPath, float[] Samples, AudioFormat Format)>
        {
            ("loud.wav", loud, format),
            ("quiet.wav", quiet, format)
        };

        var loudBefore = LoudnessAnalyzer.Analyze(loud, format).IntegratedLufs;
        var quietBefore = LoudnessAnalyzer.Analyze(quiet, format).IntegratedLufs;
        Assert.True(loudBefore > quietBefore + 6);
        var spacingBefore = loudBefore - quietBefore;

        ExportService.MatchAlbumLoudness(tracks, -14.0, -1.0);

        var loudAfter = LoudnessAnalyzer.Analyze(loud, format, -14.0, -1.0).IntegratedLufs;
        var quietAfter = LoudnessAnalyzer.Analyze(quiet, format, -14.0, -1.0).IntegratedLufs;
        Assert.InRange(loudAfter, -15.5f, -12.5f);
        Assert.InRange(loudAfter - quietAfter, spacingBefore - 1.0f, spacingBefore + 1.0f);
    }

    [Fact]
    public void Multiband_BandIsolation_LowToneMostlyInLowBandEnergy()
    {
        const double sr = 48000;
        var fx = new MultibandCompressorEffect { Depth = 0.01, LowCrossoverHz = 200, HighCrossoverHz = 2500 };
        fx.Prepare(new AudioFormat((int)sr, 2));
        var buf = SineStereo(80, 8192, sr, 0.5, 0.5);
        fx.Process(buf);
        Assert.True(fx.LowEnergy > fx.MidEnergy);
        Assert.True(fx.LowEnergy > fx.HighEnergy);
    }

    [Fact]
    public void Multiband_BandIsolation_HighToneMostlyInHighBandEnergy()
    {
        const double sr = 48000;
        var fx = new MultibandCompressorEffect { Depth = 0.01, LowCrossoverHz = 200, HighCrossoverHz = 2500 };
        fx.Prepare(new AudioFormat((int)sr, 2));
        var buf = SineStereo(6000, 8192, sr, 0.5, 0.5);
        fx.Process(buf);
        Assert.True(fx.HighEnergy > fx.LowEnergy);
        Assert.True(fx.HighEnergy > fx.MidEnergy * 0.5);
    }

    [Fact]
    public void Multiband_CompressionProducesGainReductionOnLoudBands()
    {
        const double sr = 48000;
        var fx = new MultibandCompressorEffect { Depth = 1.0, ThresholdDb = -40, DownRatio = 8, MaxUpwardDb = 0 };
        fx.Prepare(new AudioFormat((int)sr, 2));
        var buf = SineStereo(1000, 16384, sr, 0.9, 0.9);
        fx.Process(buf);
        var totalGr = Math.Abs(fx.LowGainReductionDb) + Math.Abs(fx.MidGainReductionDb) + Math.Abs(fx.HighGainReductionDb);
        Assert.True(totalGr > 0.1, $"expected multiband GR, got L={fx.LowGainReductionDb} M={fx.MidGainReductionDb} H={fx.HighGainReductionDb}");
    }

    [Fact]
    public void LoudnessMeter_Bs1770_Minus23ReferenceAmplitude_WithinHalfLu()
    {
        // Conformance-style check using the same reference amplitude as the calibrated −23 test.
        var format = new AudioFormat(48000, 2);
        var loud = new LoudnessMeter();
        loud.Prepare(format);
        var amp = 0.0707f;
        var block = new float[4800];
        var phase = 0.0;
        var inc = 2 * Math.PI * 1000.0 / 48000.0;
        for (var n = 0; n < 50; n++)
        {
            for (var i = 0; i < block.Length; i += 2)
            {
                var s = amp * (float)Math.Sin(phase);
                phase += inc;
                block[i] = s;
                block[i + 1] = s;
            }
            loud.Process(block);
        }
        Assert.InRange(loud.IntegratedLufs, -23.5f, -22.5f);
    }

    [Fact]
    public void LoudnessMeter_Bs1770_HalfAmplitudeReference_IsSixLuLower()
    {
        // A 6.02 dB amplitude reduction of the documented −23 LUFS stereo reference should read −29 LUFS.
        var loud = new LoudnessMeter();
        loud.Prepare(new AudioFormat(48000, 2));
        var block = new float[4800];
        var phase = 0.0;
        var increment = 2 * Math.PI * 1000.0 / 48000.0;
        for (var n = 0; n < 50; n++)
        {
            for (var i = 0; i < block.Length; i += 2)
            {
                var sample = 0.03535f * (float)Math.Sin(phase);
                phase += increment;
                block[i] = sample;
                block[i + 1] = sample;
            }
            loud.Process(block);
        }

        Assert.InRange(loud.IntegratedLufs, -29.6f, -28.4f);
    }

    [Fact]
    public void TruePeakMeter_FullScaleSine_ReportsNearZeroDbTp()
    {
        const int sr = 48000;
        var buf = SineStereo(1000, sr, sr, 1.0, 1.0);
        var tp = new TruePeakMeter();
        tp.Prepare(new AudioFormat(sr, 2));
        tp.Process(buf);
        Assert.InRange(tp.MaxDbTp, -0.5f, 0.5f);
    }

    [Fact]
    public void MatchEq_HasTargetAndAppliesForcedSpectrumCorrection()
    {
        const int sr = 48000;
        var format = new AudioFormat(sr, 2);
        var target = new float[MatchEqEffect.TargetBandCount];
        for (var i = 0; i < target.Length; i++)
            target[i] = i > target.Length / 2 ? 12f : -6f; // bright target profile

        var fx = new MatchEqEffect { Blend = 1.0, Smoothness = 1.0 };
        fx.Prepare(format);
        fx.SetTargetSpectrum(target);
        Assert.True(fx.HasTarget);

        var program = SineStereo(200, 8192, sr, 0.4, 0.4);
        for (var n = 0; n < 128; n++)
            fx.Process(program);

        var gains = new float[MatchEqEffect.EqBandCount];
        fx.CopyBandGainsDb(gains);
        var span = gains.Max() - gains.Min();
        Assert.True(span > 2.0f, $"expected Match EQ band spread > 2 dB, got {span:F2} ([{string.Join(", ", gains.Select(g => g.ToString("0.0")))}])");
    }

    [Fact]
    public void LinearPhaseEq_BoostChangesMagnitudeRelativeToFlat()
    {
        const int sr = 48000;
        var flat = new LinearPhaseEqEffect();
        var boost = new LinearPhaseEqEffect { HighMidFreq = 3000, HighMidGainDb = 6 };
        flat.Prepare(new AudioFormat(sr, 1));
        boost.Prepare(new AudioFormat(sr, 1));

        float Measure(LinearPhaseEqEffect fx)
        {
            var buf = new float[8192];
            for (var i = 0; i < buf.Length; i++)
                buf[i] = (float)Math.Sin(2 * Math.PI * 3000 * i / sr) * 0.25f;
            // Flush latency
            fx.Process(buf);
            buf = new float[8192];
            for (var i = 0; i < buf.Length; i++)
                buf[i] = (float)Math.Sin(2 * Math.PI * 3000 * i / sr) * 0.25f;
            fx.Process(buf);
            double sum = 0;
            for (var i = 256; i < buf.Length; i++) sum += buf[i] * buf[i];
            return (float)Math.Sqrt(sum / (buf.Length - 256));
        }

        Assert.True(Measure(boost) > Measure(flat) * 1.2f);
    }

    [Fact]
    public void NormalizeBufferToLufs_IteratesTowardTarget()
    {
        const int sr = 48000;
        var format = new AudioFormat(sr, 2);
        var buf = SineStereo(1000, sr * 3, sr, 0.02, 0.02);
        ExportService.NormalizeBufferToLufs(buf, format, -14.0, -1.0);
        var report = LoudnessAnalyzer.Analyze(buf, format, -14.0, -1.0);
        Assert.InRange(report.IntegratedLufs, -15.0f, -13.0f);
    }

    [Fact]
    public void Multiband_SoloLow_IsolatesLowBand()
    {
        const int sr = 48000;
        var low = new MultibandCompressorEffect { Depth = 1, SoloLow = true, HighBoostDb = 0 };
        var high = new MultibandCompressorEffect { Depth = 1, SoloHigh = true, HighBoostDb = 0 };
        low.Prepare(new AudioFormat(sr, 2));
        high.Prepare(new AudioFormat(sr, 2));
        low.Process(new float[4]);
        high.Process(new float[4]);
        low.Depth = high.Depth = 1;
        low.HighBoostDb = high.HighBoostDb = 0;

        var lowOutput = SineStereo(80, 8192, sr, 0.2, 0.2);
        var highOutput = (float[])lowOutput.Clone();
        low.Process(lowOutput);
        high.Process(highOutput);

        Assert.True(Rms(lowOutput) > Rms(highOutput) * 2,
            "soloing low should retain substantially more of an 80 Hz tone than soloing high");
        Assert.Contains(low.Parameters, p => p.Name == "Solo Low");
        Assert.Contains(low.Parameters, p => p.Name == "Mute High");
    }

    [Fact]
    public void LoudnessMeterEffect_IsPassThroughAndRegistered()
    {
        var fx = new LoudnessMeterEffect();
        fx.Prepare(new AudioFormat(48000, 2));
        var buffer = SineStereo(1000, 4800, 48000, 0.2, 0.2);
        var original = (float[])buffer.Clone();

        fx.Process(buffer);

        Assert.Equal(original, buffer);
        Assert.False(float.IsNegativeInfinity(fx.MomentaryLufs));
        Assert.IsAssignableFrom<IAnalyserOnlyEffect>(fx);
        Assert.IsType<LoudnessMeterEffect>(new EffectRegistry().Create(LoudnessMeterEffect.TypeId));
    }

    [Fact]
    public void ClipperOversample_ProcessesEverySurroundChannel()
    {
        var fx = new ClipperEffect { OversampleIndex = 2, DriveDb = 6, CeilingDb = -1 };
        fx.Prepare(new AudioFormat(48000, 6));
        var buffer = new float[256 * 6];
        Array.Fill(buffer, 1.5f);

        fx.Process(buffer);

        var ceiling = (float)AudioMath.Db2Lin(-1);
        for (var i = 0; i < buffer.Length; i++)
            Assert.True(Math.Abs(buffer[i]) <= ceiling + 1e-4f, $"channel sample {i} exceeded ceiling");
    }

    [Fact]
    public void PeakLimiterOversample_ProcessesEverySurroundChannel()
    {
        var fx = new PeakLimiterEffect
        {
            MasteringPresetIndex = 0, OversampleIndex = 2, ThresholdDb = 0, CeilingDb = -1
        };
        fx.Prepare(new AudioFormat(48000, 6));
        fx.Process(new float[6]); // consume preset selection
        fx.ThresholdDb = 0;
        fx.CeilingDb = -1;
        var buffer = new float[256 * 6];
        Array.Fill(buffer, 1.5f);

        fx.Process(buffer);

        var ceiling = (float)AudioMath.Db2Lin(-1);
        for (var i = 0; i < buffer.Length; i++)
            Assert.True(Math.Abs(buffer[i]) <= ceiling + 1e-4f, $"channel sample {i} exceeded ceiling");
    }

    [Fact]
    public void MatchEq_CaptureArmed_CanRecaptureLiveTarget()
    {
        const int sr = 48000;
        var fx = new MatchEqEffect();
        fx.Prepare(new AudioFormat(sr, 2));

        fx.CaptureArmed = true;
        fx.Process(SineStereo(5000, 4096, sr, 0.3, 0.3));
        fx.CaptureArmed = false;
        var brightTarget = new float[MatchEqEffect.TargetBandCount];
        fx.CopyTargetSpectrum(brightTarget);

        fx.CaptureArmed = true;
        fx.Process(SineStereo(120, 4096, sr, 0.3, 0.3));
        fx.CaptureArmed = false;
        var bassTarget = new float[MatchEqEffect.TargetBandCount];
        fx.CopyTargetSpectrum(bassTarget);

        Assert.True(fx.HasTarget);
        Assert.True(brightTarget.Zip(bassTarget, (a, b) => Math.Abs(a - b)).Sum() > 1,
            "re-arming capture should replace the live target without removing the insert");
    }
}
