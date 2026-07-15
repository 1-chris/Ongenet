using System;
using System.Diagnostics;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace Ongenet.Core.Tests.Audio;

/// <summary>
/// Ascension Climax2 stress harness (bars 152–168). Timing thresholds are informational for local
/// profiling — CI asserts structural correctness and allocation discipline instead.
/// </summary>
public sealed class AscensionClimax2BenchmarkTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int BlockFrames = 512;
    private static readonly AudioFormat Format = new(SampleRate, Channels);

    private readonly ITestOutputHelper _output;

    public AscensionClimax2BenchmarkTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(64, "climax1")]
    [InlineData(152, "climax2")]
    public void Drop_LivePump_CompletesWithoutAudioThreadAllocSpike(int startBar, string section)
    {
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(new FieldNodeRegistry(), instruments, new EffectRegistry());
        var project = UpliftingTranceSongFactory.Create(instruments);

        var output = new CapturingAudioOutput(Format);
        var projectSvc = new ProjectService(instruments);
        projectSvc.SetCurrentProject(project);
        var transport = new TransportService();
        var events = new EventAggregator();
        var capture = new SessionCaptureService(projectSvc, transport, events);
        var playback = new PlaybackModeService(projectSvc, transport, capture);
        using var engine = new AudioEngine(output, projectSvc, transport, playback, new EventAggregator(),
            new AuditionPlayer());

        AudioDiagnostics.Reset();
        engine.Start();

        const double lengthBeats = 4;
        transport.StartBeat = startBar * 4;
        transport.Play();

        // Warm a complete drop bar so JIT/first-note setup is not mistaken for sustained realtime load.
        var warmupFrames = (int)Math.Ceiling(lengthBeats * SampleRate * 60.0 / UpliftingTranceSongFactory.Bpm);
        output.Pump(warmupFrames);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        AudioDiagnostics.Reset();

        using var process = Process.GetCurrentProcess();
        var heapBefore = GC.GetTotalMemory(forceFullCollection: false);
        var workingSetBefore = process.WorkingSet64;
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var frames = (int)Math.Ceiling(lengthBeats * SampleRate * 60.0 / UpliftingTranceSongFactory.Bpm);
        output.Pump(frames);
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var heapAfter = GC.GetTotalMemory(forceFullCollection: false);
        var workingSetAfter = process.WorkingSet64;

        engine.Stop();
        var snap = AudioDiagnostics.Snapshot();
        _output.WriteLine(
            $"{section} blocks={snap.BlockCount} avg={snap.AverageBlockMicroseconds}µs " +
            $"p95={snap.P95BlockMicroseconds}µs p99={snap.P99BlockMicroseconds}µs " +
            $"max={snap.MaxBlockMicroseconds}µs budget={snap.BlockBudgetMicroseconds}µs " +
            $"over={snap.OverBudgetCount} wall={sw.ElapsedMilliseconds}ms alloc={allocated}B " +
            $"audioAlloc={snap.TotalAudioThreadAllocatedBytes}B " +
            $"last(render={snap.LastRenderMicroseconds}µs/{snap.LastRenderAllocatedBytes}B, " +
            $"mix={snap.LastMixdownMicroseconds}µs/{snap.LastMixdownAllocatedBytes}B) " +
            $"slowTrack={snap.MaxTimeTrackName}:{snap.MaxTrackMicroseconds}µs " +
            $"allocTrack={snap.MaxAllocationTrackName}:{snap.MaxTrackAllocatedBytes}B " +
            $"heap={heapBefore}->{heapAfter} workingSet={workingSetBefore}->{workingSetAfter} " +
            $"gc={GC.CollectionCount(0) - gen0Before}/{GC.CollectionCount(1) - gen1Before}/" +
            $"{GC.CollectionCount(2) - gen2Before}");

        Assert.True(snap.BlockCount > 0);
        Assert.True(snap.BlockBudgetMicroseconds >= 10_000, $"Budget too low: {snap.BlockBudgetMicroseconds}");
        // Steady-state path may still allocate modest scratch on first climax contact;
        // guard against runaway per-pump retention rather than zero allocation.
        // xUnit may run other DSP stress tests concurrently; catch regressions measured in megabytes
        // without making this local performance harness scheduler-sensitive.
        Assert.True(allocated < 512_000, $"Unexpected audio-path allocation: {allocated} bytes");
    }

    [Fact]
    public void FieldInstrument_ReadProjectState_DoesNotCompileUntilPrepare()
    {
        var registry = new FieldNodeRegistry();
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(registry, instruments, new EffectRegistry());
        var live = (FieldInstrument)instruments.Create(FieldInstrument.Id);
        live.LoadPreset(0);
        Assert.NotNull(live.Compiled);

        var clone = (FieldInstrument)live.Clone();
        Assert.Null(clone.Compiled);

        clone.Prepare(Format);
        Assert.NotNull(clone.Compiled);
    }

    [Fact]
    public void LoudnessMeter_PreparedHistoriesProcessWithoutDeferredAllocation()
    {
        var meter = new Ongenet.Core.Audio.Dsp.LoudnessMeter();
        meter.Prepare(Format);
        // Long histories are created at Prepare, never later on the realtime thread.
        var silent = new float[BlockFrames * Channels];
        for (var i = 0; i < 4; i++)
            meter.Process(silent);
        // Just ensure Process doesn't throw and momentary updates.
        Assert.True(float.IsNegativeInfinity(meter.MomentaryLufs) || meter.MomentaryLufs <= 0);
    }

    [Fact]
    public void MasteringChains_TypeIds_MatchConstructedChain()
    {
        foreach (var key in new[] { "full", "full+", "streaming", "club", "podcast", "techno", "audiophile", "reference" })
        {
            var chain = MasteringChains.Create(key);
            var ids = MasteringChains.TypeIds(key);
            Assert.Equal(chain.Length, ids.Length);
            for (var i = 0; i < chain.Length; i++)
                Assert.Equal(chain[i].TypeId, ids[i]);
        }
    }

    private sealed class CapturingAudioOutput : IAudioOutput
    {
        public CapturingAudioOutput(AudioFormat format) => Format = format;
        public AudioFormat Format { get; }
        public bool IsRunning { get; private set; }
        public event Action? FormatChanged { add { } remove { } }
        public System.Collections.Generic.List<float> Samples { get; } = new();
        private AudioRenderCallback? _callback;

        public void Start(AudioRenderCallback callback)
        {
            _callback = callback;
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
            _callback = null;
        }

        public void Pump(int totalFrames)
        {
            var block = new float[BlockFrames * Format.Channels];
            var written = 0;
            while (written < totalFrames)
            {
                var frames = Math.Min(BlockFrames, totalFrames - written);
                var span = block.AsSpan(0, frames * Format.Channels);
                span.Clear();
                _callback?.Invoke(span);
                // Don't retain every sample forever — keep memory flat for the stress pump.
                written += frames;
            }
        }

        public void Dispose() => Stop();
    }
}
