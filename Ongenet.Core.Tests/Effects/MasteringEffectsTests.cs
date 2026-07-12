using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;

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
        var fx = new ClipperEffect { DriveDb = 0, CeilingDb = -0.3 };
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
        }, cts.Token);

        var ui = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                fx.Prepare(new AudioFormat(48000, 2));
                fx.Prepare(new AudioFormat(44100, 2));
            }
        }, cts.Token);

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
        }, cts.Token);

        var ui = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                fx.Prepare(new AudioFormat(48000, 2));
                fx.Prepare(new AudioFormat(44100, 2));
            }
        }, cts.Token);

        await Task.WhenAll(audio, ui);
        Assert.Null(caught);
    }
}
