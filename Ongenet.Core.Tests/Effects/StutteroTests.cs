using System.IO;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Modulation;
using Ongenet.Core.Audio.Modules;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Tests.Effects;

public class ModulationCurveTests
{
    [Fact]
    public void EmptyCurveIsFullOn() => Assert.Equal(1.0, new ModulationCurve().Evaluate(0.5));

    [Fact]
    public void LinearRampInterpolates()
    {
        var c = new ModulationCurve(new[] { new AutomationPoint(0, 0), new AutomationPoint(1, 1) });
        Assert.Equal(0.0, c.Evaluate(0), 3);
        Assert.Equal(0.5, c.Evaluate(0.5), 3);
        Assert.Equal(1.0, c.Evaluate(1), 3);
    }

    [Fact]
    public void PalindromeFoldsToPeakAtCentre()
    {
        var c = new ModulationCurve(new[] { new AutomationPoint(0, 0), new AutomationPoint(1, 1) }) { Palindrome = true };
        Assert.Equal(0.0, c.Evaluate(0), 3);
        Assert.Equal(1.0, c.Evaluate(0.5), 3);
        Assert.Equal(0.0, c.Evaluate(1), 3);
        Assert.Equal(c.Evaluate(0.25), c.Evaluate(0.75), 3); // symmetric
    }

    [Fact]
    public void QuantizeSnapsPhaseToSteps()
    {
        var c = new ModulationCurve(new[] { new AutomationPoint(0, 0), new AutomationPoint(1, 1) }) { QuantizeSteps = 4 };
        Assert.Equal(0.25, c.Evaluate(0.30), 3); // 0.30 → floor(1.2)/4 = 0.25
        Assert.Equal(0.0, c.Evaluate(0.20), 3);  // 0.20 → floor(0.8)/4 = 0
    }

    [Fact]
    public void PresetsAllProducePoints()
    {
        foreach (var shape in CurveShapes.All)
            Assert.NotEmpty(shape.Build());
    }
}

public class CaptureBufferTests
{
    [Fact]
    public void ReadsAbsolutePositionsExactly()
    {
        var buf = new CaptureBuffer();
        buf.Resize(16);
        for (var i = 0; i < 10; i++) buf.Write(i);

        Assert.Equal(10, buf.WritePos);
        Assert.Equal(5f, buf.ReadAbs(5), 3);
        Assert.Equal(4.5f, buf.ReadAbs(4.5), 3); // linear interpolation
    }

    [Fact]
    public void SnapshotCopiesMostRecentSamples()
    {
        var buf = new CaptureBuffer();
        buf.Resize(16);
        for (var i = 0; i < 10; i++) buf.Write(i);

        var dest = new float[4];
        buf.Snapshot(dest, 4);
        Assert.Equal(new[] { 6f, 7f, 8f, 9f }, dest);
    }
}

public class FxModuleRackTests
{
    [Fact]
    public void DefaultRackHasAllBuiltInModulesDisabled()
    {
        var rack = FxModuleCatalog.DefaultRack();
        Assert.Equal(FxModuleCatalog.All.Count, rack.Modules.Count);
        Assert.All(rack.Modules, m => Assert.False(m.Enabled));
    }

    [Fact]
    public void MoveReordersAndCommitsToActiveSnapshot()
    {
        var rack = FxModuleCatalog.DefaultRack();
        var firstId = rack.Modules[0].Id;
        rack.Move(0, 2);
        Assert.Equal(firstId, rack.Modules[2].Id);
        Assert.Equal(firstId, rack.Active[2].Id); // Move() committed the new order
        Assert.Equal(rack.Modules.Count, rack.Active.Length);
    }
}

public class StutteroPersistenceTests
{
    [Fact]
    public void RoundTripsGesturesKeyMapAndRack()
    {
        var fx = new StutteroEffect { AutoGestureIndex = 2 };
        fx.Gestures[0].Name = "Edited";
        fx.Gestures[0].RateIndex = 5;
        fx.Gestures[0].Buffer = BufferMode.Random;
        fx.Gestures[0].Cutoff = new ModulationCurve(new[] { new AutomationPoint(0, 0.2), new AutomationPoint(1, 0.9) });
        fx.KeyMap[64] = 1;
        fx.Rack.Move(0, 3);
        fx.Rack.Modules[0].Enabled = true;
        var reorderedFirstId = fx.Rack.Modules[0].Id;

        var clone = new StutteroEffect();
        using (var ms = new MemoryStream())
        {
            using (var w = new OngenWriter(ms)) fx.WriteProjectState(w);
            ms.Position = 0;
            using var r = new OngenReader(ms);
            clone.ReadProjectState(r);
        }

        Assert.Equal(2, clone.AutoGestureIndex);
        Assert.Equal("Edited", clone.Gestures[0].Name);
        Assert.Equal(5, clone.Gestures[0].RateIndex);
        Assert.Equal(BufferMode.Random, clone.Gestures[0].Buffer);
        Assert.NotNull(clone.Gestures[0].Cutoff);
        Assert.Equal(0.9, clone.Gestures[0].Cutoff!.Evaluate(1), 3);
        Assert.Equal(1, clone.KeyMap[64]);
        Assert.Equal(reorderedFirstId, clone.Rack.Modules[0].Id);
        Assert.True(clone.Rack.Modules[0].Enabled);
    }
}

public class StutteroEngineTests
{
    private static AudioFormat Fmt => new(44100, 2);

    [Fact]
    public void MidiNoteTriggersWetProcessing()
    {
        var fx = new StutteroEffect { ModeIndex = 1, Mix = 1.0 }; // MIDI mode, full wet
        fx.Prepare(Fmt);
        fx.SetContext(new EffectContext { Format = Fmt, Bpm = 120, Playing = true });

        // Default key map sends note 60 → gesture 0 (a Lock stutter).
        fx.HandleMidi(new MidiMessage(MidiMessageKind.NoteOn, 0, 60, 100));

        var buffer = new float[512 * 2];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = 0.5f;
        fx.Process(buffer);

        // The gesture is active, so the dry 0.5 signal must have been replaced by the wet path.
        var changed = false;
        foreach (var s in buffer) if (System.Math.Abs(s - 0.5f) > 1e-4f) { changed = true; break; }
        Assert.True(changed);
    }

    [Fact]
    public void IdleEffectIsTransparent()
    {
        // MIDI mode with no key held → no gesture active → the signal passes through untouched.
        var fx = new StutteroEffect { ModeIndex = 1, Mix = 1.0 };
        fx.Prepare(Fmt);
        fx.SetContext(new EffectContext { Format = Fmt, Bpm = 120, Playing = true });

        var buffer = new float[256 * 2];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = 0.25f;
        fx.Process(buffer);

        Assert.All(buffer, s => Assert.Equal(0.25f, s, 4));
    }

    [Fact]
    public void AutoModeEngagesWithoutTransportPlaying()
    {
        // Auto mode is continuously engaged while enabled, so it processes even when stopped/auditioning.
        var fx = new StutteroEffect { ModeIndex = 0, Mix = 1.0 };
        fx.Prepare(Fmt);
        fx.SetContext(new EffectContext { Format = Fmt, Bpm = 120, Playing = false });

        // Prime the capture ring with audio across a few blocks, then a Slide gesture repeats it.
        fx.Gestures[0].Buffer = BufferMode.Slide;
        var rnd = new System.Random(1);
        var changed = false;
        for (var block = 0; block < 4; block++)
        {
            var buffer = new float[256 * 2];
            for (var i = 0; i < buffer.Length; i++) buffer[i] = (float)(rnd.NextDouble() * 2 - 1) * 0.5f;
            var dry = (float[])buffer.Clone();
            fx.Process(buffer);
            for (var i = 0; i < buffer.Length; i++)
                if (System.Math.Abs(buffer[i] - dry[i]) > 1e-3f) { changed = true; break; }
        }

        Assert.True(changed);
    }
}
