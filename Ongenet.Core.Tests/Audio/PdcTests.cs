using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Audio.Parameters;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class PdcTests
{
    [Fact]
    public void LatencyCompensator_AlignsParallelPaths()
    {
        var fxA = new LatencyStubEffect(100);
        var fxB = new LatencyStubEffect(400);
        var trackA = new Track { Name = "A", Kind = TrackKind.Audio };
        trackA.Effects.Add(fxA);
        trackA.CommitEffects();
        var trackB = new Track { Name = "B", Kind = TrackKind.Audio };
        trackB.Effects.Add(fxB);
        trackB.CommitEffects();
        var master = new Track { Name = "Master", Kind = TrackKind.Master };

        var pdc = LatencyCompensator.Compute(new[] { trackA, trackB, master });
        Assert.Equal(300, pdc[trackA.Id].DelaySamples);
        Assert.Equal(0, pdc[trackB.Id].DelaySamples);
    }

    [Fact]
    public void PdcDelayLine_DelaysByConfiguredSamples()
    {
        var line = new PdcDelayLine();
        line.Configure(2, 1, 8);
        var buf = new float[8];
        buf[0] = 1f;
        line.Process(buf.AsSpan(0, 4), 4);
        Assert.Equal(0f, buf[0]);
        Assert.Equal(0f, buf[1]);
        Assert.Equal(1f, buf[2]);
    }

    private sealed class LatencyStubEffect : IAudioEffect, ILatencyProvider
    {
        public LatencyStubEffect(int latency) => ReportedLatencySamples = latency;
        public string Name => "Latency Stub";
        public string TypeId => "test.latency";
        public bool Enabled { get; set; } = true;
        public int ReportedLatencySamples { get; }
        public IReadOnlyList<Parameter> Parameters => Array.Empty<Parameter>();
        public void Prepare(AudioFormat format) { }
        public void Process(Span<float> buffer) { }
        public IAudioEffect Clone() => new LatencyStubEffect(ReportedLatencySamples);
    }
}
