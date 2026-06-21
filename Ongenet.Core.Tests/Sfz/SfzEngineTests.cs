using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Instruments.Sampler.Sfz;

namespace Ongenet.Core.Tests.Sfz;

public class SfzEngineTests
{
    private static AudioSampleBuffer Const(float v, int frames)
        => new(Enumerable.Repeat(v, frames).ToArray(), 1, 44100);

    private static SamplerInstrument Make(string sfz, params (string name, AudioSampleBuffer buf)[] samples)
    {
        var doc = SfzParser.Parse(sfz);
        var dict = new Dictionary<string, SamplerSample>();
        foreach (var (name, buf) in samples) dict[name] = SamplerSample.FromResident(buf);

        var inst = new SamplerInstrument();
        inst.Prepare(new AudioFormat(44100, 1)); // mono keeps pan gain = 1
        var lib = new SamplerSampleLibrary(dict);
        inst.ApplyLoad(new SamplerLoadResult
        {
            Regions = SfzLoader.BuildRegions(doc, lib),
            Library = lib,
            Path = "t.sfz",
            DisplayName = "t",
            Format = SamplerFormat.Sfz,
            SourceText = sfz
        });
        return inst;
    }

    private static float[] Render(SamplerInstrument inst, int frames)
    {
        var buffer = new float[frames];
        inst.Render(buffer);
        return buffer;
    }

    [Fact]
    public void MatchingRegionSoundsAndOthersAreSilent()
    {
        var inst = Make("<region> sample=a.wav key=60", ("a.wav", Const(0.5f, 200)));

        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 16)[0] > 0.4f);

        var inst2 = Make("<region> sample=a.wav key=60", ("a.wav", Const(0.5f, 200)));
        inst2.NoteOn(61, 1f); // out of range
        Assert.True(Render(inst2, 16).All(s => s == 0f));
    }

    [Fact]
    public void VelocityLayersSelectDifferentSamples()
    {
        // amp_veltrack=0 isolates layer *selection* from velocity-dependent amplitude.
        const string sfz = @"
<region> sample=soft.wav key=60 lovel=0 hivel=63 amp_veltrack=0
<region> sample=loud.wav key=60 lovel=64 hivel=127 amp_veltrack=0";

        var soft = Make(sfz, ("soft.wav", Const(0.5f, 200)), ("loud.wav", Const(-0.5f, 200)));
        soft.NoteOn(60, 0.3f); // ~vel 38 -> soft layer (positive)
        Assert.True(Render(soft, 16)[0] > 0.4f);

        var loud = Make(sfz, ("soft.wav", Const(0.5f, 200)), ("loud.wav", Const(-0.5f, 200)));
        loud.NoteOn(60, 1f);  // vel 127 -> loud layer (negative)
        Assert.True(Render(loud, 16)[0] < -0.4f);
    }

    [Fact]
    public void AmpVeltrackScalesLevelWithVelocity()
    {
        const string sfz = "<region> sample=a.wav key=60 amp_veltrack=100";

        var loud = Make(sfz, ("a.wav", Const(0.5f, 200)));
        loud.NoteOn(60, 1f);
        var loudLevel = Render(loud, 16)[0];

        var soft = Make(sfz, ("a.wav", Const(0.5f, 200)));
        soft.NoteOn(60, 0.3f);
        var softLevel = Render(soft, 16)[0];

        Assert.True(loudLevel > softLevel * 3f); // higher velocity is markedly louder
    }

    [Fact]
    public void RoundRobinAlternatesBetweenSamples()
    {
        const string sfz = @"
<group>
<region> sample=rr1.wav key=60 seq_length=2 seq_position=1
<region> sample=rr2.wav key=60 seq_length=2 seq_position=2";

        var inst = Make(sfz, ("rr1.wav", Const(0.5f, 100)), ("rr2.wav", Const(-0.5f, 100)));

        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 128)[0] > 0.4f);  // first hit -> rr1 (positive), voice finishes within 128

        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 128)[0] < -0.4f); // second hit -> rr2 (negative)
    }

    [Fact]
    public void LoopContinuousKeepsPlayingPastSampleEnd()
    {
        var looped = Make("<region> sample=a.wav key=60 loop_mode=loop_continuous loop_start=0 loop_end=3",
            ("a.wav", Const(0.5f, 4)));
        looped.NoteOn(60, 1f);
        var b = Render(looped, 64);
        Assert.True(b[60] > 0.4f); // still sounding well past the 4-frame sample

        var oneShot = Make("<region> sample=a.wav key=60", ("a.wav", Const(0.5f, 4)));
        oneShot.NoteOn(60, 1f);
        var b2 = Render(oneShot, 64);
        Assert.True(b2[0] > 0.4f);  // sounds at the start
        Assert.Equal(0f, b2[40]);   // silent after the sample ends (no loop)
    }

    [Fact]
    public void PitchResamplingShortensHigherNotes()
    {
        const string sfz = "<region> sample=a.wav pitch_keycenter=60 lokey=0 hikey=127";

        int ActiveFrames(int note)
        {
            var inst = Make(sfz, ("a.wav", Const(0.5f, 200)));
            inst.NoteOn(note, 1f);
            var b = Render(inst, 512);
            return b.Count(s => s != 0f);
        }

        var atRoot = ActiveFrames(60);   // rate 1.0 -> ~200 frames
        var octaveUp = ActiveFrames(72); // rate 2.0 -> ~100 frames

        Assert.InRange(atRoot, 195, 205);
        Assert.InRange(octaveUp, 95, 110);
    }

    [Fact]
    public void ReleaseFadesVoiceToSilence()
    {
        var inst = Make("<region> sample=a.wav key=60 loop_mode=loop_continuous loop_start=0 loop_end=99 ampeg_release=0.001",
            ("a.wav", Const(0.5f, 100)));

        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 64)[0] > 0.4f);

        inst.NoteOff(60);
        // After the (1 ms ~ 44 sample) release, the instrument should be silent.
        var tail = Render(inst, 256);
        Assert.Equal(0f, tail[255]);
    }
}
