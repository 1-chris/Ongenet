using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Instruments.Sampler.Sfz;

namespace Ongenet.Core.Tests.Sfz;

public class SfzArticulationTests
{
    private static SamplerInstrument Make(string sfz, params (string name, AudioSampleBuffer buf)[] samples)
    {
        var doc = SfzParser.Parse(sfz);
        var dict = new Dictionary<string, SamplerSample>();
        foreach (var (name, buf) in samples) dict[name] = SamplerSample.FromResident(buf);

        var inst = new SamplerInstrument();
        inst.Prepare(new AudioFormat(44100, 1));
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

    private static AudioSampleBuffer Const(float v, int frames)
        => new(Enumerable.Repeat(v, frames).ToArray(), 1, 44100);

    private static AudioSampleBuffer Alternating(float amp, int frames)
    {
        var s = new float[frames];
        for (var i = 0; i < frames; i++) s[i] = i % 2 == 0 ? amp : -amp;
        return new AudioSampleBuffer(s, 1, 44100);
    }

    private static float[] Render(SamplerInstrument inst, int frames)
    {
        var buffer = new float[frames];
        inst.Render(buffer);
        return buffer;
    }

    private static double Rms(float[] b) => Math.Sqrt(b.Sum(s => s * (double)s) / b.Length);

    [Fact]
    public void KeySwitchSelectsArticulation()
    {
        const string sfz = @"
<global> sw_lokey=0 sw_hikey=1 sw_default=0
<region> sample=a.wav key=60 sw_last=0 amp_veltrack=0
<region> sample=b.wav key=60 sw_last=1 amp_veltrack=0";

        var inst = Make(sfz, ("a.wav", Const(0.5f, 100)), ("b.wav", Const(-0.5f, 100)));

        inst.NoteOn(60, 1f);                       // default key-switch 0 -> region a
        Assert.True(Render(inst, 128)[0] > 0.4f);

        inst.NoteOn(1, 1f);                         // press key-switch -> articulation 1 (no sound)
        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 128)[0] < -0.4f);  // now region b
    }

    [Fact]
    public void ReleaseTriggerFiresOnNoteOff()
    {
        const string sfz = @"
<region> sample=main.wav key=60 amp_veltrack=0 loop_mode=loop_continuous loop_start=0 loop_end=99 ampeg_release=0.001
<region> sample=rel.wav key=60 trigger=release amp_veltrack=0";

        var inst = Make(sfz, ("main.wav", Const(0.5f, 100)), ("rel.wav", Const(-0.5f, 100)));

        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 64)[0] > 0.4f); // main sustaining

        inst.NoteOff(60);
        var tail = Render(inst, 128);
        Assert.True(tail[90] < -0.3f); // release sample sounds after main has faded
    }

    [Fact]
    public void OffByCutsExclusiveGroup()
    {
        const string sfz = @"
<region> sample=open.wav key=60 group=1 off_by=1 amp_veltrack=0 loop_mode=loop_continuous loop_start=0 loop_end=999
<region> sample=closed.wav key=62 group=1 off_by=1 amp_veltrack=0";

        var inst = Make(sfz, ("open.wav", Const(0.5f, 2000)), ("closed.wav", Const(-0.5f, 100)));

        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 64)[0] > 0.4f); // open ringing (loops forever)

        inst.NoteOn(62, 1f);                      // closed in same group cuts the open
        var b = Render(inst, 256);
        Assert.True(b[95] < -0.3f);               // closed sounds
        Assert.True(Math.Abs(b[200]) < 0.1f);     // open was choked (would still be +0.5 otherwise)
    }

    [Fact]
    public void FirstAndLegatoTriggers()
    {
        const string sfz = @"
<region> sample=first.wav lokey=60 hikey=72 pitch_keycenter=60 trigger=first amp_veltrack=0
<region> sample=legato.wav lokey=60 hikey=72 pitch_keycenter=60 trigger=legato amp_veltrack=0";

        var inst = Make(sfz, ("first.wav", Const(0.5f, 200)), ("legato.wav", Const(-0.5f, 200)));

        inst.NoteOn(60, 1f);                        // nothing held -> "first"
        Assert.True(Render(inst, 256)[0] > 0.4f);   // play it out (note 60 stays held, no note-off)

        inst.NoteOn(62, 1f);                        // a note already held -> "legato"
        Assert.True(Render(inst, 32)[0] < -0.4f);
    }

    [Fact]
    public void PitchBendRaisesPitch()
    {
        const string sfz = "<region> sample=a.wav pitch_keycenter=60 lokey=0 hikey=127 bend_up=1200 amp_veltrack=0";

        int ActiveFrames(int bend14)
        {
            var inst = Make(sfz, ("a.wav", Const(0.5f, 200)));
            inst.PitchBend(bend14);
            inst.NoteOn(60, 1f);
            return Render(inst, 512).Count(s => s != 0f);
        }

        Assert.InRange(ActiveFrames(8192), 195, 205); // centre: ~200 frames
        Assert.True(ActiveFrames(16383) < 130);       // +1 octave bend: read twice as fast
    }

    [Fact]
    public void CutoffCcOpensFilter()
    {
        const string sfz = "<region> sample=a.wav key=60 pitch_keycenter=60 cutoff=300 fil_type=lpf_2p cutoff_cc74=9600 amp_veltrack=0";

        double Brightness(int cc74)
        {
            var inst = Make(sfz, ("a.wav", Alternating(0.5f, 400)));
            inst.ControlChange(74, cc74);
            inst.NoteOn(60, 1f);
            return Rms(Render(inst, 400));
        }

        var dark = Brightness(0);    // cutoff 300 -> Nyquist tone suppressed
        var bright = Brightness(127); // CC74 pushes cutoff way up -> tone passes
        Assert.True(bright > dark * 3, $"bright={bright:F4} dark={dark:F4}");
    }

    [Fact]
    public void SustainPedalHoldsNotesUntilReleased()
    {
        const string sfz = "<region> sample=a.wav key=60 amp_veltrack=0 loop_mode=loop_continuous loop_start=0 loop_end=999 ampeg_release=0.001";
        var inst = Make(sfz, ("a.wav", Const(0.5f, 2000)));

        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 64)[0] > 0.4f);

        inst.ControlChange(64, 127); // pedal down
        inst.NoteOff(60);
        Assert.True(Render(inst, 128)[100] > 0.4f); // still sounding (held by pedal)

        inst.ControlChange(64, 0);   // pedal up -> release
        var tail = Render(inst, 256);
        Assert.Equal(0f, tail[255]);
    }
}
