using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Field.Nodes;

namespace Ongenet.Core.Audio.Field.Patches;

/// <summary>
/// Full-decomposition Field patches that reproduce the built-in instruments and effects. The tractable ones
/// are rebuilt from primitive nodes (oscillators, envelopes, filters, math); the irreducible cores (grain
/// scheduler, SFZ mapping, freeverb network, pitch engines, the Stuttero performance engine) are wired via
/// their faithful module-wrapper node, which runs the exact same code path. Surfaced by the Field instrument
/// as presets and by the palette as "insert built-in as patch".
/// </summary>
public static class FieldBuiltInPatches
{
    public static readonly IReadOnlyList<string> InstrumentPatchNames = new[]
    {
        "Oscillator", "3x Osc", "FM Synth", "Wavetable", "Padda", "Basic Sampler", "Kicka", "Granular", "Sampler (SFZ)",
        "Perca", "Prism Lead", "Reese Bass", "Nova Saw", "Crystal Pluck", "Aether Lead", "Solace Lead", "Acid Bass",
        "Comet Riser"
    };

    public static readonly IReadOnlyList<string> EffectPatchNames = new[]
    {
        "EQ", "Filter", "Compressor", "Limiter", "Gate", "Sidechain", "Chorus", "Phaser", "Flanger", "Tremolo",
        "Stuttero", "Delay", "Reverb", "Distortion", "Bitcrusher", "Vocoder", "Auto-Tune", "Stereo Width",
        "Live Difference", "Utility", "3D Scope"
    };

    public static void BuildInstrument(int index, FieldGraph g, IFieldNodeRegistry reg)
    {
        g.Clear();
        switch (index)
        {
            case 0: Oscillator(g); break;
            case 1: TripleOsc(g); break;
            case 2: FmSynth(g); break;
            case 3: Wavetable(g); break;
            case 4: Padda(g, reg); break;
            case 5: BasicSampler(g); break;
            case 6: Kicka(g); break;
            case 7: ModuleInstrument(g, reg, "granular"); break;
            case 8: Sfz(g); break;
            case 9: Perca(g); break;
            case 10: PrismLead(g); break;
            case 11: ReeseBass(g); break;
            case 12: NovaSaw(g); break;
            case 13: CrystalPluck(g); break;
            case 14: AetherLead(g, reg); break;
            case 15: SolaceLead(g); break;
            case 16: AcidBass(g); break;
            case 17: CometRiser(g); break;
            default: Oscillator(g); break;
        }
    }

    public static void BuildEffect(int index, FieldGraph g, IFieldNodeRegistry reg)
    {
        g.Clear();
        switch (EffectPatchNames[Math.Clamp(index, 0, EffectPatchNames.Count - 1)])
        {
            case "Filter": StereoFilter(g); break;
            case "Delay": StereoDelay(g); break;
            case "Distortion": StereoWaveshaper(g); break;
            case "Bitcrusher": StereoBitcrush(g); break;
            case "Tremolo": Tremolo(g); break;
            case "Compressor": StereoCompressor(g); break;
            default: ModuleEffect(g, reg, EffectIdFor(index)); break;
        }
    }

    // ---- Instrument patches ----

    private static void Oscillator(FieldGraph g)
    {
        var note = Add(g, new NoteInNode { X = 40, Y = 160 });
        var osc = Add(g, new WaveOscNode { X = 240, Y = 100, WaveIndex = 2 }); // saw
        var adsr = Add(g, new AdsrNode { X = 240, Y = 300, Attack = 0.005, Decay = 0.08, Sustain = 0.7, Release = 0.2 });
        var vca = Add(g, new GainNode { X = 460, Y = 160, Amount = 0.22 });
        var outN = Add(g, new AudioOutNode { X = 680, Y = 160 });
        g.Connect(note.Id, "pitch", osc.Id, "pitch");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(osc.Id, "out", vca.Id, "in");
        g.Connect(adsr.Id, "out", vca.Id, "cv");
        Stereo(g, vca, "out", outN);
    }

    private static void TripleOsc(FieldGraph g)
    {
        var note = Add(g, new NoteInNode { X = 40, Y = 200 });
        var o1 = Add(g, new WaveOscNode { X = 220, Y = 60, WaveIndex = 2, Level = 1.0 });
        var o2 = Add(g, new WaveOscNode { X = 220, Y = 200, WaveIndex = 2, Level = 0.7, Fine = 8 });
        var o3 = Add(g, new WaveOscNode { X = 220, Y = 340, WaveIndex = 2, Level = 0.5, Coarse = -12 });
        var mix1 = Add(g, new AddNode { X = 420, Y = 130 });
        var mix2 = Add(g, new AddNode { X = 560, Y = 200 });
        var filt = Add(g, new BiquadFilterNode { X = 700, Y = 200, ModeIndex = 0, Cutoff = 18000 });
        var adsr = Add(g, new AdsrNode { X = 700, Y = 360 });
        var vca = Add(g, new GainNode { X = 860, Y = 200, Amount = 0.25 });
        var outN = Add(g, new AudioOutNode { X = 1040, Y = 200 });
        foreach (var o in new[] { o1, o2, o3 }) g.Connect(note.Id, "pitch", o.Id, "pitch");
        g.Connect(o1.Id, "out", mix1.Id, "a");
        g.Connect(o2.Id, "out", mix1.Id, "b");
        g.Connect(mix1.Id, "out", mix2.Id, "a");
        g.Connect(o3.Id, "out", mix2.Id, "b");
        g.Connect(mix2.Id, "out", filt.Id, "in");
        g.Connect(filt.Id, "out", vca.Id, "in");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(adsr.Id, "out", vca.Id, "cv");
        Stereo(g, vca, "out", outN);
    }

    private static void FmSynth(FieldGraph g)
    {
        var note = Add(g, new NoteInNode { X = 40, Y = 160 });
        var fm = Add(g, new FmOperatorNode { X = 240, Y = 100, Ratio = 2.0, ModIndex = 2.0 });
        var adsr = Add(g, new AdsrNode { X = 240, Y = 300, Attack = 0.005, Decay = 0.12, Sustain = 0.7, Release = 0.25 });
        var vca = Add(g, new GainNode { X = 460, Y = 160, Amount = 0.22 });
        var outN = Add(g, new AudioOutNode { X = 680, Y = 160 });
        g.Connect(note.Id, "pitch", fm.Id, "pitch");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(fm.Id, "out", vca.Id, "in");
        g.Connect(adsr.Id, "out", vca.Id, "cv");
        Stereo(g, vca, "out", outN);
    }

    private static void Wavetable(FieldGraph g)
    {
        var note = Add(g, new NoteInNode { X = 40, Y = 180 });
        var osc = Add(g, new WavetableOscNode { X = 220, Y = 100, PresetIndex = 1, Position = 0.25 });
        var lfo = Add(g, new LfoNode { X = 220, Y = 300, Rate = 0.3, Depth = 0.4, Unipolar = true });
        var filt = Add(g, new BiquadFilterNode { X = 420, Y = 180, Cutoff = 6000 });
        var adsr = Add(g, new AdsrNode { X = 420, Y = 340, Attack = 0.02, Sustain = 0.8 });
        var vca = Add(g, new GainNode { X = 620, Y = 180, Amount = 0.25 });
        var outN = Add(g, new AudioOutNode { X = 820, Y = 180 });
        g.Connect(note.Id, "pitch", osc.Id, "pitch");
        g.Connect(lfo.Id, "out", osc.Id, "mod:1"); // modulate table position
        g.Connect(osc.Id, "out", filt.Id, "in");
        g.Connect(filt.Id, "out", vca.Id, "in");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(adsr.Id, "out", vca.Id, "cv");
        Stereo(g, vca, "out", outN);
    }

    private static void Padda(FieldGraph g, IFieldNodeRegistry reg)
    {
        var note = Add(g, new NoteInNode { X = 40, Y = 240 });
        var layerA = Add(g, new UnisonOscNode { X = 220, Y = 80, Voices = 7, DetuneCents = 18, Level = 0.5 });
        var layerB = Add(g, new UnisonOscNode { X = 220, Y = 220, Voices = 5, DetuneCents = 30, Coarse = 12, Level = 0.3 });
        var sub = Add(g, new WaveOscNode { X = 220, Y = 360, WaveIndex = 0, Coarse = -12, Level = 0.4 });
        var noise = Add(g, new NoiseNode { X = 220, Y = 480, Level = 0.05 });
        var mixA = Add(g, new AddNode { X = 420, Y = 150 });
        var mixB = Add(g, new AddNode { X = 560, Y = 260 });
        var mixC = Add(g, new AddNode { X = 700, Y = 380 });
        var filt = Add(g, new BiquadFilterNode { X = 840, Y = 260, Cutoff = 1200, Resonance = 2.0 });
        var filtEnv = Add(g, new AdsrNode { X = 840, Y = 460, Attack = 0.4, Decay = 1.0, Sustain = 0.3, Release = 1.2 });
        var ampEnv = Add(g, new AdsrNode { X = 1000, Y = 460, Attack = 0.6, Sustain = 0.9, Release = 1.5 });
        var vca = Add(g, new GainNode { X = 1000, Y = 260, Amount = 0.18 });
        var sum = Add(g, new VoiceSumNode { X = 1160, Y = 260 });
        var chorus = Module(g, reg, "chorus", 1300, 220);
        var delay = Module(g, reg, "delay", 1440, 220);
        var reverb = Module(g, reg, "reverb", 1580, 220);
        var outN = Add(g, new AudioOutNode { X = 1740, Y = 260 });

        g.Connect(note.Id, "pitch", layerA.Id, "pitch");
        g.Connect(note.Id, "pitch", layerB.Id, "pitch");
        g.Connect(note.Id, "pitch", sub.Id, "pitch");
        g.Connect(layerA.Id, "l", mixA.Id, "a");
        g.Connect(layerB.Id, "l", mixA.Id, "b");
        g.Connect(mixA.Id, "out", mixB.Id, "a");
        g.Connect(sub.Id, "out", mixB.Id, "b");
        g.Connect(mixB.Id, "out", mixC.Id, "a");
        g.Connect(noise.Id, "out", mixC.Id, "b");
        g.Connect(mixC.Id, "out", filt.Id, "in");
        g.Connect(note.Id, "gate", filtEnv.Id, "gate");
        g.Connect(filtEnv.Id, "out", filt.Id, "mod:1"); // filter-env → cutoff
        g.Connect(filt.Id, "out", vca.Id, "in");
        g.Connect(note.Id, "gate", ampEnv.Id, "gate");
        g.Connect(ampEnv.Id, "out", vca.Id, "cv");
        g.Connect(vca.Id, "out", sum.Id, "l");
        g.Connect(vca.Id, "out", sum.Id, "r");
        g.Connect(sum.Id, "l", chorus.Id, "l");
        g.Connect(sum.Id, "r", chorus.Id, "r");
        g.Connect(chorus.Id, "l", delay.Id, "l");
        g.Connect(chorus.Id, "r", delay.Id, "r");
        g.Connect(delay.Id, "l", reverb.Id, "l");
        g.Connect(delay.Id, "r", reverb.Id, "r");
        g.Connect(reverb.Id, "l", outN.Id, "l");
        g.Connect(reverb.Id, "r", outN.Id, "r");
    }

    private static void BasicSampler(FieldGraph g)
    {
        var note = Add(g, new NoteInNode { X = 40, Y = 180 });
        var smp = Add(g, new SamplePlayerNode { X = 240, Y = 120 });
        var adsr = Add(g, new AdsrNode { X = 240, Y = 320, Attack = 0.001, Sustain = 1.0, Release = 0.08 });
        var vcaL = Add(g, new GainNode { X = 460, Y = 100, Amount = 1.0 });
        var vcaR = Add(g, new GainNode { X = 460, Y = 260, Amount = 1.0 });
        var outN = Add(g, new AudioOutNode { X = 680, Y = 180 });
        g.Connect(note.Id, "pitch", smp.Id, "pitch");
        g.Connect(note.Id, "gate", smp.Id, "gate");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(smp.Id, "l", vcaL.Id, "in");
        g.Connect(smp.Id, "r", vcaR.Id, "in");
        g.Connect(adsr.Id, "out", vcaL.Id, "cv");
        g.Connect(adsr.Id, "out", vcaR.Id, "cv");
        g.Connect(vcaL.Id, "out", outN.Id, "l");
        g.Connect(vcaR.Id, "out", outN.Id, "r");
    }

    private static void Kicka(FieldGraph g)
    {
        var note = Add(g, new NoteInNode { X = 40, Y = 220 });
        var pitchEnv = Add(g, new CurveEnvNode { X = 220, Y = 80, Attack = 0.001, Decay = 0.09, Curve = 0.85 });
        var toHz = Add(g, new ScaleOffsetNode { X = 400, Y = 80, Scale = 3.0, Offset = 0.09 }); // 0..1 -> ~50..3000 Hz below
        var freq = Add(g, new ConstantNode { X = 400, Y = 200, Value = 1000 });
        var mulFreq = Add(g, new MultiplyNode { X = 560, Y = 120 });
        var body = Add(g, new WaveOscNode { X = 720, Y = 120, WaveIndex = 0 });
        var ampEnv = Add(g, new CurveEnvNode { X = 720, Y = 300, Attack = 0.001, Decay = 0.35, Curve = 0.6 });
        var vca = Add(g, new GainNode { X = 900, Y = 180, Amount = 1.0 });
        var dist = Add(g, new DistortionStackNode { X = 1060, Y = 180, Stages = 3, Drive = 8, Scream = 1400 });
        var clip = Add(g, new SoftClipNode { X = 1220, Y = 180, Drive = 1.2 });
        var outN = Add(g, new AudioOutNode { X = 1380, Y = 180 });

        g.Connect(note.Id, "gate", pitchEnv.Id, "gate");
        g.Connect(note.Id, "gate", ampEnv.Id, "gate");
        g.Connect(pitchEnv.Id, "out", toHz.Id, "in");
        g.Connect(toHz.Id, "out", mulFreq.Id, "a");
        g.Connect(freq.Id, "out", mulFreq.Id, "b");
        g.Connect(mulFreq.Id, "out", body.Id, "pitch");
        g.Connect(body.Id, "out", vca.Id, "in");
        g.Connect(ampEnv.Id, "out", vca.Id, "cv");
        g.Connect(vca.Id, "out", dist.Id, "in");
        g.Connect(dist.Id, "out", clip.Id, "in");
        Stereo(g, clip, "out", outN);
    }

    private static void Perca(FieldGraph g)
    {
        // The Perca clap decomposed to primitives: band-passed noise shaped by three staggered
        // one-shot curve envelopes (the multi-tap burst that makes a clap a clap) into a soft clip.
        var note = Add(g, new NoteInNode { X = 40, Y = 240 });
        var noise = Add(g, new NoiseNode { X = 220, Y = 100, Level = 1.0 });
        var filt = Add(g, new BiquadFilterNode { X = 400, Y = 100, ModeIndex = 1, Cutoff = 1500, Resonance = 1.8 });
        var tap1 = Add(g, new CurveEnvNode { X = 220, Y = 280, Delay = 0.0, Attack = 0.0005, Decay = 0.028, Curve = 0.65 });
        var tap2 = Add(g, new CurveEnvNode { X = 220, Y = 440, Delay = 0.011, Attack = 0.0005, Decay = 0.028, Curve = 0.65 });
        var tap3 = Add(g, new CurveEnvNode { X = 220, Y = 600, Delay = 0.022, Attack = 0.0005, Decay = 0.38, Curve = 0.65 });
        var mixA = Add(g, new AddNode { X = 440, Y = 360 });
        var mixB = Add(g, new AddNode { X = 580, Y = 440 });
        var vca = Add(g, new GainNode { X = 620, Y = 180, Amount = 0.7 });
        var clip = Add(g, new SoftClipNode { X = 780, Y = 180, Drive = 1.3 });
        var outN = Add(g, new AudioOutNode { X = 940, Y = 180 });

        g.Connect(note.Id, "gate", tap1.Id, "gate");
        g.Connect(note.Id, "gate", tap2.Id, "gate");
        g.Connect(note.Id, "gate", tap3.Id, "gate");
        g.Connect(tap1.Id, "out", mixA.Id, "a");
        g.Connect(tap2.Id, "out", mixA.Id, "b");
        g.Connect(mixA.Id, "out", mixB.Id, "a");
        g.Connect(tap3.Id, "out", mixB.Id, "b");
        g.Connect(noise.Id, "out", filt.Id, "in");
        g.Connect(filt.Id, "out", vca.Id, "in");
        g.Connect(mixB.Id, "out", vca.Id, "cv");
        g.Connect(vca.Id, "out", clip.Id, "in");
        Stereo(g, clip, "out", outN);
    }

    private static void PrismLead(FieldGraph g)
    {
        // The preview song's melody voice: a Harmonics wavetable slowly scanned by an LFO, into a
        // warm low-pass, with the Scope's 3D waveform trail on the way out.
        var note = Add(g, new NoteInNode { X = 40, Y = 180 });
        var osc = Add(g, new WavetableOscNode { X = 220, Y = 100, PresetIndex = 1, Position = 0.15 });
        var lfo = Add(g, new LfoNode { X = 220, Y = 320, Rate = 0.15, Depth = 0.35, Unipolar = true });
        var filt = Add(g, new BiquadFilterNode { X = 440, Y = 180, Cutoff = 1900, Resonance = 1.0 });
        var adsr = Add(g, new AdsrNode { X = 440, Y = 360, Attack = 0.04, Decay = 0.3, Sustain = 0.6, Release = 0.45 });
        var vca = Add(g, new GainNode { X = 640, Y = 180, Amount = 0.22 });
        var scope = Add(g, new ScopeNode { X = 820, Y = 180 });
        var outN = Add(g, new AudioOutNode { X = 1020, Y = 180 });

        g.Connect(note.Id, "pitch", osc.Id, "pitch");
        g.Connect(lfo.Id, "out", osc.Id, "mod:1"); // scan the table position
        g.Connect(osc.Id, "out", filt.Id, "in");
        g.Connect(filt.Id, "out", vca.Id, "in");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(adsr.Id, "out", vca.Id, "cv");
        g.Connect(vca.Id, "out", scope.Id, "in");
        Stereo(g, scope, "thru", outN);
    }

    private static void ReeseBass(FieldGraph g)
    {
        // The dark DnB workhorse. Richness comes from a 7-voice detuned saw unison beating against an
        // octave-down saw, driven through a waveshaper BEFORE the filter (the pre-filter distortion is
        // what turns beating into growl). The low-pass sits dark with low resonance; the LFO only
        // breathes the cutoff very slowly — the movement is phasing, not a wah sweep.
        var note = Add(g, new NoteInNode { X = 40, Y = 220 });
        var uni = Add(g, new UnisonOscNode { X = 220, Y = 80, WaveIndex = 2, Voices = 7, DetuneCents = 24, StereoWidth = 0.3, Blend = 0.85, Level = 0.9 });
        var sub = Add(g, new WaveOscNode { X = 220, Y = 300, WaveIndex = 2, Coarse = -12, Level = 0.45 });
        var mono = Add(g, new AddNode { X = 440, Y = 140 });
        var mix = Add(g, new AddNode { X = 580, Y = 220 });
        var shaper = Add(g, new WaveShaperNode { X = 720, Y = 220, Drive = 2.2 });
        var lfo = Add(g, new LfoNode { X = 720, Y = 420, Rate = 0.5, Depth = 0.045, Unipolar = true });
        var filt = Add(g, new BiquadFilterNode { X = 880, Y = 220, ModeIndex = 0, Cutoff = 480, Resonance = 1.1 });
        var adsr = Add(g, new AdsrNode { X = 880, Y = 420, Attack = 0.004, Decay = 0.12, Sustain = 0.85, Release = 0.12 });
        var vca = Add(g, new GainNode { X = 1040, Y = 220, Amount = 0.2 });
        var clip = Add(g, new SoftClipNode { X = 1200, Y = 220, Drive = 1.3 });
        var outN = Add(g, new AudioOutNode { X = 1360, Y = 220 });

        g.Connect(note.Id, "pitch", uni.Id, "pitch");
        g.Connect(note.Id, "pitch", sub.Id, "pitch");
        g.Connect(uni.Id, "l", mono.Id, "a");
        g.Connect(uni.Id, "r", mono.Id, "b");
        g.Connect(mono.Id, "out", mix.Id, "a");
        g.Connect(sub.Id, "out", mix.Id, "b");
        g.Connect(mix.Id, "out", shaper.Id, "in");
        g.Connect(shaper.Id, "out", filt.Id, "in");
        g.Connect(lfo.Id, "out", filt.Id, "mod:1"); // slow breathing, not a wah
        g.Connect(filt.Id, "out", vca.Id, "in");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(adsr.Id, "out", vca.Id, "cv");
        g.Connect(vca.Id, "out", clip.Id, "in");
        Stereo(g, clip, "out", outN);
    }

    private static void NovaSaw(FieldGraph g)
    {
        // A big supersaw layer: nine hard-detuned saws spread wide in stereo, filtered per side so the
        // width survives to the output, with soft-clip drive to thicken the harmonics and glue it.
        var note = Add(g, new NoteInNode { X = 40, Y = 200 });
        var uni = Add(g, new UnisonOscNode { X = 240, Y = 120, WaveIndex = 2, Voices = 9, DetuneCents = 38, StereoWidth = 0.95, Blend = 0.78, Level = 0.82 });
        var fl = Add(g, new BiquadFilterNode { X = 460, Y = 80, ModeIndex = 0, Cutoff = 4800, Resonance = 0.7 });
        var fr = Add(g, new BiquadFilterNode { X = 460, Y = 260, ModeIndex = 0, Cutoff = 4800, Resonance = 0.7 });
        var adsr = Add(g, new AdsrNode { X = 460, Y = 440, Attack = 0.003, Decay = 0.14, Sustain = 0.55, Release = 0.12 });
        var vcaL = Add(g, new GainNode { X = 680, Y = 80, Amount = 0.3 });
        var vcaR = Add(g, new GainNode { X = 680, Y = 260, Amount = 0.3 });
        var clipL = Add(g, new SoftClipNode { X = 840, Y = 80, Drive = 1.35 });
        var clipR = Add(g, new SoftClipNode { X = 840, Y = 260, Drive = 1.35 });
        var outN = Add(g, new AudioOutNode { X = 1020, Y = 180 });

        g.Connect(note.Id, "pitch", uni.Id, "pitch");
        g.Connect(uni.Id, "l", fl.Id, "in");
        g.Connect(uni.Id, "r", fr.Id, "in");
        g.Connect(fl.Id, "out", vcaL.Id, "in");
        g.Connect(fr.Id, "out", vcaR.Id, "in");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(adsr.Id, "out", vcaL.Id, "cv");
        g.Connect(adsr.Id, "out", vcaR.Id, "cv");
        g.Connect(vcaL.Id, "out", clipL.Id, "in");
        g.Connect(vcaR.Id, "out", clipR.Id, "in");
        g.Connect(clipL.Id, "out", outN.Id, "l");
        g.Connect(clipR.Id, "out", outN.Id, "r");
    }

    private static void AetherLead(FieldGraph g, IFieldNodeRegistry reg)
    {
        // The 2010s festival supersaw wall. Not one saw — a stack: a wide 9-voice main supersaw, a
        // SECOND 7-voice saw detuned much harder for thickness (two beating supersaws is the trick
        // behind the "huge" sound), a quiet 5-voice unison an octave up for air, and a pure sine an
        // octave down for physical weight. A filter-cutoff envelope blooms each note bright; the
        // filter sits high so it stays "clear as day"; hard soft-clip drive packs in harmonics and
        // loudness, and the chorus widens the whole thing before the track's hall reverb.
        var note = Add(g, new NoteInNode { X = 40, Y = 300 });
        var uniA = Add(g, new UnisonOscNode { X = 220, Y = 40, WaveIndex = 2, Voices = 9, DetuneCents = 26, StereoWidth = 1.0, Blend = 0.8, Level = 0.7 });
        var uniB = Add(g, new UnisonOscNode { X = 220, Y = 200, WaveIndex = 2, Voices = 7, DetuneCents = 44, StereoWidth = 0.85, Blend = 0.7, Level = 0.4 }); // detuned thickness stack
        var uniC = Add(g, new UnisonOscNode { X = 220, Y = 360, WaveIndex = 2, Voices = 5, DetuneCents = 14, Coarse = 12, StereoWidth = 0.9, Blend = 0.7, Level = 0.26 }); // octave-up air
        var sub = Add(g, new WaveOscNode { X = 220, Y = 520, WaveIndex = 0, Coarse = -12, Level = 0.32 }); // sub weight (mono)
        var sumL1 = Add(g, new AddNode { X = 420, Y = 80 });
        var sumL2 = Add(g, new AddNode { X = 540, Y = 100 });
        var mixL = Add(g, new AddNode { X = 660, Y = 120 });
        var sumR1 = Add(g, new AddNode { X = 420, Y = 320 });
        var sumR2 = Add(g, new AddNode { X = 540, Y = 340 });
        var mixR = Add(g, new AddNode { X = 660, Y = 360 });
        var fl = Add(g, new BiquadFilterNode { X = 800, Y = 120, ModeIndex = 0, Cutoff = 2600, Resonance = 0.5 });
        var fr = Add(g, new BiquadFilterNode { X = 800, Y = 360, ModeIndex = 0, Cutoff = 2600, Resonance = 0.5 });
        var adsr = Add(g, new AdsrNode { X = 800, Y = 560, Attack = 0.004, Decay = 0.16, Sustain = 0.62, Release = 0.16 });
        // Filter-cutoff envelope: fast, snappy bloom — bright crack on the attack, then settles for a plucky front.
        var filtEnv = Add(g, new AdsrNode { X = 640, Y = 620, Attack = 0.001, Decay = 0.18, Sustain = 0.3, Release = 0.15 });
        var filtScale = Add(g, new GainNode { X = 800, Y = 620, Amount = 0.13 });
        var vcaL = Add(g, new GainNode { X = 960, Y = 120, Amount = 0.34 });
        var vcaR = Add(g, new GainNode { X = 960, Y = 360, Amount = 0.34 });
        var clipL = Add(g, new SoftClipNode { X = 1100, Y = 120, Drive = 1.5 });
        var clipR = Add(g, new SoftClipNode { X = 1100, Y = 360, Drive = 1.5 });
        var chorus = Module(g, reg, "chorus", 1240, 240);
        var outN = Add(g, new AudioOutNode { X = 1420, Y = 240 });

        g.Connect(note.Id, "pitch", uniA.Id, "pitch");
        g.Connect(note.Id, "pitch", uniB.Id, "pitch");
        g.Connect(note.Id, "pitch", uniC.Id, "pitch");
        g.Connect(note.Id, "pitch", sub.Id, "pitch");
        // Left sum: uniA + uniB + uniC + sub
        g.Connect(uniA.Id, "l", sumL1.Id, "a");
        g.Connect(uniB.Id, "l", sumL1.Id, "b");
        g.Connect(sumL1.Id, "out", sumL2.Id, "a");
        g.Connect(uniC.Id, "l", sumL2.Id, "b");
        g.Connect(sumL2.Id, "out", mixL.Id, "a");
        g.Connect(sub.Id, "out", mixL.Id, "b");
        // Right sum
        g.Connect(uniA.Id, "r", sumR1.Id, "a");
        g.Connect(uniB.Id, "r", sumR1.Id, "b");
        g.Connect(sumR1.Id, "out", sumR2.Id, "a");
        g.Connect(uniC.Id, "r", sumR2.Id, "b");
        g.Connect(sumR2.Id, "out", mixR.Id, "a");
        g.Connect(sub.Id, "out", mixR.Id, "b");
        g.Connect(mixL.Id, "out", fl.Id, "in");
        g.Connect(mixR.Id, "out", fr.Id, "in");
        g.Connect(fl.Id, "out", vcaL.Id, "in");
        g.Connect(fr.Id, "out", vcaR.Id, "in");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(adsr.Id, "out", vcaL.Id, "cv");
        g.Connect(adsr.Id, "out", vcaR.Id, "cv");
        g.Connect(note.Id, "gate", filtEnv.Id, "gate");
        g.Connect(filtEnv.Id, "out", filtScale.Id, "in");
        g.Connect(filtScale.Id, "out", fl.Id, "mod:1");
        g.Connect(filtScale.Id, "out", fr.Id, "mod:1");
        g.Connect(vcaL.Id, "out", clipL.Id, "in");
        g.Connect(vcaR.Id, "out", clipR.Id, "in");
        g.Connect(clipL.Id, "out", chorus.Id, "l");
        g.Connect(clipR.Id, "out", chorus.Id, "r");
        g.Connect(chorus.Id, "l", outN.Id, "l");
        g.Connect(chorus.Id, "r", outN.Id, "r");
    }

    private static void AcidBass(FieldGraph g)
    {
        // A 303-style acid voice, kept CLEAN but massive: the saw runs through a squelching
        // resonant low-pass (accent envelope kicking the cutoff on every note) with moderate drive,
        // while a pure sine at the fundamental bypasses the whole dirty path and joins at the VCA —
        // untouched low end carrying the weight, the squelch riding on top. Automate a filter
        // insert on the track for the classic rising acid build.
        var note = Add(g, new NoteInNode { X = 40, Y = 260 });
        var osc = Add(g, new WaveOscNode { X = 220, Y = 120, WaveIndex = 2, Level = 0.85 });
        var sub = Add(g, new WaveOscNode { X = 220, Y = 480, WaveIndex = 0, Level = 0.55 }); // the clean fundamental
        var accent = Add(g, new CurveEnvNode { X = 220, Y = 320, Attack = 0.001, Decay = 0.18, Curve = 0.85 });
        var accScale = Add(g, new GainNode { X = 400, Y = 320, Amount = 0.1 });
        var filt = Add(g, new BiquadFilterNode { X = 420, Y = 120, ModeIndex = 0, Cutoff = 260, Resonance = 5.0 });
        var shaper = Add(g, new WaveShaperNode { X = 600, Y = 120, Drive = 1.6 });
        var mix = Add(g, new AddNode { X = 760, Y = 200 });
        var ampEnv = Add(g, new AdsrNode { X = 600, Y = 320, Attack = 0.002, Decay = 0.16, Sustain = 0.2, Release = 0.08 });
        var vca = Add(g, new GainNode { X = 900, Y = 200, Amount = 0.22 });
        var clip = Add(g, new SoftClipNode { X = 1060, Y = 200, Drive = 1.1 });
        var outN = Add(g, new AudioOutNode { X = 1220, Y = 200 });

        g.Connect(note.Id, "pitch", osc.Id, "pitch");
        g.Connect(note.Id, "pitch", sub.Id, "pitch");
        g.Connect(osc.Id, "out", filt.Id, "in");
        g.Connect(note.Id, "gate", accent.Id, "gate");
        g.Connect(accent.Id, "out", accScale.Id, "in");
        g.Connect(accScale.Id, "out", filt.Id, "mod:1"); // the acid squelch
        g.Connect(filt.Id, "out", shaper.Id, "in");
        g.Connect(shaper.Id, "out", mix.Id, "a");
        g.Connect(sub.Id, "out", mix.Id, "b"); // clean sine joins after the dirt
        g.Connect(mix.Id, "out", vca.Id, "in");
        g.Connect(note.Id, "gate", ampEnv.Id, "gate");
        g.Connect(ampEnv.Id, "out", vca.Id, "cv");
        g.Connect(vca.Id, "out", clip.Id, "in");
        Stereo(g, clip, "out", outN);
    }

    private static void CometRiser(FieldGraph g)
    {
        // A TONAL riser — the harmonic layer that sits under a white-noise sweep. Two detuned saws
        // whose pitch climbs ~24 semitones over roughly four bars at 140 BPM (a one-shot curve
        // envelope, inverted by a scale/offset so it rises instead of falls, driving the Coarse
        // mod inlet). Hold a note into a drop and let the track volume swell do the rest.
        var note = Add(g, new NoteInNode { X = 40, Y = 220 });
        var o1 = Add(g, new WaveOscNode { X = 220, Y = 100, WaveIndex = 2, Fine = 10, Level = 0.6 });
        var o2 = Add(g, new WaveOscNode { X = 220, Y = 260, WaveIndex = 2, Fine = -10, Level = 0.6 });
        var riseEnv = Add(g, new CurveEnvNode { X = 220, Y = 440, Attack = 0.0, Decay = 6.8, Curve = 0.1 });
        var riseMap = Add(g, new ScaleOffsetNode { X = 400, Y = 440, Scale = -0.25, Offset = 0.25 }); // 1→0 becomes 0→0.25 (+24 st)
        var mix = Add(g, new AddNode { X = 440, Y = 180 });
        var filt = Add(g, new BiquadFilterNode { X = 600, Y = 180, ModeIndex = 0, Cutoff = 2800, Resonance = 1.2 });
        var adsr = Add(g, new AdsrNode { X = 600, Y = 360, Attack = 2.5, Decay = 0.2, Sustain = 1.0, Release = 0.4 });
        var vca = Add(g, new GainNode { X = 780, Y = 180, Amount = 0.2 });
        var outN = Add(g, new AudioOutNode { X = 960, Y = 180 });

        g.Connect(note.Id, "pitch", o1.Id, "pitch");
        g.Connect(note.Id, "pitch", o2.Id, "pitch");
        g.Connect(note.Id, "gate", riseEnv.Id, "gate");
        g.Connect(riseEnv.Id, "out", riseMap.Id, "in");
        g.Connect(riseMap.Id, "out", o1.Id, "mod:1"); // Coarse climbs as the envelope runs
        g.Connect(riseMap.Id, "out", o2.Id, "mod:1");
        g.Connect(o1.Id, "out", mix.Id, "a");
        g.Connect(o2.Id, "out", mix.Id, "b");
        g.Connect(mix.Id, "out", filt.Id, "in");
        g.Connect(filt.Id, "out", vca.Id, "in");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(adsr.Id, "out", vca.Id, "cv");
        Stereo(g, vca, "out", outN);
    }

    private static void SolaceLead(FieldGraph g)
    {
        // The emotional theme voice, now a fuller singing supersaw: a 7-voice unison (18 cents) wide
        // in stereo, anchored by a triangle at the fundamental and a sine an octave below for weight,
        // through a bright low-pass with a per-note cutoff bloom, and soft-clip drive so it has body
        // and presence rather than sounding thin. Holds long notes beautifully.
        var note = Add(g, new NoteInNode { X = 40, Y = 260 });
        var uni = Add(g, new UnisonOscNode { X = 220, Y = 60, WaveIndex = 2, Voices = 7, DetuneCents = 18, StereoWidth = 0.8, Blend = 0.75, Level = 0.62 });
        var body = Add(g, new WaveOscNode { X = 220, Y = 260, WaveIndex = 1, Level = 0.32 });          // triangle anchor
        var warmth = Add(g, new WaveOscNode { X = 220, Y = 420, WaveIndex = 0, Coarse = -12, Level = 0.2 }); // sine sub-octave
        var mono = Add(g, new AddNode { X = 440, Y = 340 });
        var mixL = Add(g, new AddNode { X = 580, Y = 120 });
        var mixR = Add(g, new AddNode { X = 580, Y = 320 });
        var bloom = Add(g, new CurveEnvNode { X = 580, Y = 500, Attack = 0.002, Decay = 0.28, Curve = 0.5 });
        var bloomScale = Add(g, new GainNode { X = 740, Y = 500, Amount = 0.06 });
        var fl = Add(g, new BiquadFilterNode { X = 760, Y = 120, ModeIndex = 0, Cutoff = 3800, Resonance = 0.55 });
        var fr = Add(g, new BiquadFilterNode { X = 760, Y = 320, ModeIndex = 0, Cutoff = 3800, Resonance = 0.55 });
        var adsr = Add(g, new AdsrNode { X = 920, Y = 500, Attack = 0.006, Decay = 0.2, Sustain = 0.6, Release = 0.22 });
        var vcaL = Add(g, new GainNode { X = 940, Y = 120, Amount = 0.28 });
        var vcaR = Add(g, new GainNode { X = 940, Y = 320, Amount = 0.28 });
        var clipL = Add(g, new SoftClipNode { X = 1100, Y = 120, Drive = 1.25 });
        var clipR = Add(g, new SoftClipNode { X = 1100, Y = 320, Drive = 1.25 });
        var outN = Add(g, new AudioOutNode { X = 1280, Y = 220 });

        g.Connect(note.Id, "pitch", uni.Id, "pitch");
        g.Connect(note.Id, "pitch", body.Id, "pitch");
        g.Connect(note.Id, "pitch", warmth.Id, "pitch");
        g.Connect(body.Id, "out", mono.Id, "a");
        g.Connect(warmth.Id, "out", mono.Id, "b");
        g.Connect(uni.Id, "l", mixL.Id, "a");
        g.Connect(mono.Id, "out", mixL.Id, "b");
        g.Connect(uni.Id, "r", mixR.Id, "a");
        g.Connect(mono.Id, "out", mixR.Id, "b");
        g.Connect(mixL.Id, "out", fl.Id, "in");
        g.Connect(mixR.Id, "out", fr.Id, "in");
        g.Connect(note.Id, "gate", bloom.Id, "gate");
        g.Connect(bloom.Id, "out", bloomScale.Id, "in");
        g.Connect(bloomScale.Id, "out", fl.Id, "mod:1"); // per-note cutoff bloom
        g.Connect(bloomScale.Id, "out", fr.Id, "mod:1");
        g.Connect(fl.Id, "out", vcaL.Id, "in");
        g.Connect(fr.Id, "out", vcaR.Id, "in");
        g.Connect(note.Id, "gate", adsr.Id, "gate");
        g.Connect(adsr.Id, "out", vcaL.Id, "cv");
        g.Connect(adsr.Id, "out", vcaR.Id, "cv");
        g.Connect(vcaL.Id, "out", clipL.Id, "in");
        g.Connect(vcaR.Id, "out", clipR.Id, "in");
        g.Connect(clipL.Id, "out", outN.Id, "l");
        g.Connect(clipR.Id, "out", outN.Id, "r");
    }

    private static void CrystalPluck(FieldGraph g)
    {
        // The trance pluck (Oda arcade-style): two barely-detuned saws plus a single hollow square
        // for the punchy vintage-digital transient, run through a low-pass whose cutoff is kicked
        // open by a fast one-shot envelope (scaled down so the sweep covers ~3.5 kHz, not the whole
        // range), with a plucky zero-sustain amp envelope. Feed it 16th arpeggios and add delay.
        var note = Add(g, new NoteInNode { X = 40, Y = 220 });
        var o1 = Add(g, new WaveOscNode { X = 220, Y = 60, WaveIndex = 2, Fine = 8, Level = 0.7 });
        var o2 = Add(g, new WaveOscNode { X = 220, Y = 200, WaveIndex = 2, Fine = -8, Level = 0.7 });
        var sq = Add(g, new WaveOscNode { X = 220, Y = 340, WaveIndex = 3, Level = 0.28 }); // hollow square transient
        var mix = Add(g, new AddNode { X = 420, Y = 120 });
        var mix2 = Add(g, new AddNode { X = 500, Y = 150 });
        var filtEnv = Add(g, new CurveEnvNode { X = 420, Y = 360, Attack = 0.001, Decay = 0.15, Curve = 0.85 });
        var envScale = Add(g, new GainNode { X = 580, Y = 360, Amount = 0.22 });
        var filt = Add(g, new BiquadFilterNode { X = 620, Y = 150, ModeIndex = 0, Cutoff = 900, Resonance = 2.0 });
        var ampEnv = Add(g, new AdsrNode { X = 620, Y = 520, Attack = 0.001, Decay = 0.15, Sustain = 0.0, Release = 0.06 });
        var vca = Add(g, new GainNode { X = 800, Y = 150, Amount = 0.22 });
        var outN = Add(g, new AudioOutNode { X = 980, Y = 150 });

        g.Connect(note.Id, "pitch", o1.Id, "pitch");
        g.Connect(note.Id, "pitch", o2.Id, "pitch");
        g.Connect(note.Id, "pitch", sq.Id, "pitch");
        g.Connect(o1.Id, "out", mix.Id, "a");
        g.Connect(o2.Id, "out", mix.Id, "b");
        g.Connect(mix.Id, "out", mix2.Id, "a");
        g.Connect(sq.Id, "out", mix2.Id, "b");
        g.Connect(mix2.Id, "out", filt.Id, "in");
        g.Connect(note.Id, "gate", filtEnv.Id, "gate");
        g.Connect(filtEnv.Id, "out", envScale.Id, "in");
        g.Connect(envScale.Id, "out", filt.Id, "mod:1"); // one-shot cutoff kick
        g.Connect(filt.Id, "out", vca.Id, "in");
        g.Connect(note.Id, "gate", ampEnv.Id, "gate");
        g.Connect(ampEnv.Id, "out", vca.Id, "cv");
        Stereo(g, vca, "out", outN);
    }

    private static void Sfz(FieldGraph g)
    {
        // A SoundFont source feeding the SFZ/SF2 sampler engine (load a soundfont in the SoundFont node).
        var sf = Add(g, new SoundFontNode { X = 60, Y = 120 });
        var sampler = Add(g, new SamplerNode { X = 320, Y = 200 });
        var outN = Add(g, new AudioOutNode { X = 620, Y = 200 });
        g.Connect(sf.Id, "sf", sampler.Id, "sf");
        g.Connect(sampler.Id, "l", outN.Id, "l");
        g.Connect(sampler.Id, "r", outN.Id, "r");
    }

    // ---- Effect patches (primitive decompositions) ----

    private static void StereoFilter(FieldGraph g)
    {
        var inN = Add(g, new AudioInNode { X = 60, Y = 160 });
        var fl = Add(g, new BiquadFilterNode { X = 300, Y = 80, Cutoff = 1200 });
        var fr = Add(g, new BiquadFilterNode { X = 300, Y = 260, Cutoff = 1200 });
        var outN = Add(g, new AudioOutNode { X = 560, Y = 160 });
        g.Connect(inN.Id, "l", fl.Id, "in");
        g.Connect(inN.Id, "r", fr.Id, "in");
        g.Connect(fl.Id, "out", outN.Id, "l");
        g.Connect(fr.Id, "out", outN.Id, "r");
    }

    private static void StereoDelay(FieldGraph g)
    {
        var inN = Add(g, new AudioInNode { X = 60, Y = 160 });
        var dl = Add(g, new DelayNode { X = 300, Y = 80, TimeMs = 375, Feedback = 0.4, Mix = 0.3 });
        var dr = Add(g, new DelayNode { X = 300, Y = 260, TimeMs = 500, Feedback = 0.4, Mix = 0.3 });
        var outN = Add(g, new AudioOutNode { X = 560, Y = 160 });
        g.Connect(inN.Id, "l", dl.Id, "in");
        g.Connect(inN.Id, "r", dr.Id, "in");
        g.Connect(dl.Id, "out", outN.Id, "l");
        g.Connect(dr.Id, "out", outN.Id, "r");
    }

    private static void StereoWaveshaper(FieldGraph g)
    {
        var inN = Add(g, new AudioInNode { X = 60, Y = 160 });
        var dl = Add(g, new WaveShaperNode { X = 300, Y = 80, Drive = 4 });
        var dr = Add(g, new WaveShaperNode { X = 300, Y = 260, Drive = 4 });
        var outN = Add(g, new AudioOutNode { X = 560, Y = 160 });
        g.Connect(inN.Id, "l", dl.Id, "in");
        g.Connect(inN.Id, "r", dr.Id, "in");
        g.Connect(dl.Id, "out", outN.Id, "l");
        g.Connect(dr.Id, "out", outN.Id, "r");
    }

    private static void StereoBitcrush(FieldGraph g)
    {
        var inN = Add(g, new AudioInNode { X = 60, Y = 160 });
        var dl = Add(g, new BitcrusherNode { X = 300, Y = 80, Bits = 8, Downsample = 4 });
        var dr = Add(g, new BitcrusherNode { X = 300, Y = 260, Bits = 8, Downsample = 4 });
        var outN = Add(g, new AudioOutNode { X = 560, Y = 160 });
        g.Connect(inN.Id, "l", dl.Id, "in");
        g.Connect(inN.Id, "r", dr.Id, "in");
        g.Connect(dl.Id, "out", outN.Id, "l");
        g.Connect(dr.Id, "out", outN.Id, "r");
    }

    private static void StereoCompressor(FieldGraph g)
    {
        var inN = Add(g, new AudioInNode { X = 60, Y = 160 });
        var cl = Add(g, new CompressorNode { X = 300, Y = 80 });
        var cr = Add(g, new CompressorNode { X = 300, Y = 260 });
        var outN = Add(g, new AudioOutNode { X = 560, Y = 160 });
        g.Connect(inN.Id, "l", cl.Id, "in");
        g.Connect(inN.Id, "r", cr.Id, "in");
        g.Connect(cl.Id, "out", outN.Id, "l");
        g.Connect(cr.Id, "out", outN.Id, "r");
    }

    private static void Tremolo(FieldGraph g)
    {
        var inN = Add(g, new AudioInNode { X = 60, Y = 160 });
        var lfo = Add(g, new LfoNode { X = 300, Y = 320, Rate = 5, Depth = 0.5, Unipolar = true });
        var gl = Add(g, new GainNode { X = 300, Y = 80, Amount = 1 });
        var gr = Add(g, new GainNode { X = 300, Y = 240, Amount = 1 });
        var outN = Add(g, new AudioOutNode { X = 560, Y = 160 });
        g.Connect(inN.Id, "l", gl.Id, "in");
        g.Connect(inN.Id, "r", gr.Id, "in");
        g.Connect(lfo.Id, "out", gl.Id, "cv");
        g.Connect(lfo.Id, "out", gr.Id, "cv");
        g.Connect(gl.Id, "out", outN.Id, "l");
        g.Connect(gr.Id, "out", outN.Id, "r");
    }

    // ---- Shared builders ----

    private static void ModuleEffect(FieldGraph g, IFieldNodeRegistry reg, string fxId)
    {
        var inN = Add(g, new AudioInNode { X = 60, Y = 160 });
        var mod = Module(g, reg, fxId, 320, 160);
        var outN = Add(g, new AudioOutNode { X = 600, Y = 160 });
        g.Connect(inN.Id, "l", mod.Id, "l");
        g.Connect(inN.Id, "r", mod.Id, "r");
        g.Connect(mod.Id, "l", outN.Id, "l");
        g.Connect(mod.Id, "r", outN.Id, "r");
    }

    private static void ModuleInstrument(FieldGraph g, IFieldNodeRegistry reg, string instId)
    {
        var mod = reg.TryCreate(InstrumentModuleNode.Prefix + instId);
        var outN = Add(g, new AudioOutNode { X = 520, Y = 160 });
        if (mod is null) return;
        mod.X = 220;
        mod.Y = 160;
        g.AddNode(mod);
        g.Connect(mod.Id, "l", outN.Id, "l");
        g.Connect(mod.Id, "r", outN.Id, "r");
    }

    private static FieldNode Module(FieldGraph g, IFieldNodeRegistry reg, string fxId, double x, double y)
    {
        var mod = reg.TryCreate(EffectModuleNode.Prefix + fxId) ?? new VoiceSumNode();
        mod.X = x;
        mod.Y = y;
        g.AddNode(mod);
        return mod;
    }

    private static T Add<T>(FieldGraph g, T node) where T : FieldNode
    {
        g.AddNode(node);
        return node;
    }

    private static void Stereo(FieldGraph g, FieldNode source, string port, AudioOutNode outN)
    {
        g.Connect(source.Id, port, outN.Id, "l");
        g.Connect(source.Id, port, outN.Id, "r");
    }

    private static string EffectIdFor(int index) => EffectPatchNames[index] switch
    {
        "EQ" => "eq",
        "Filter" => "filter",
        "Compressor" => "compressor",
        "Limiter" => "limiter",
        "Gate" => "gate",
        "Sidechain" => "sidechain",
        "Chorus" => "chorus",
        "Phaser" => "phaser",
        "Flanger" => "flanger",
        "Tremolo" => "tremolo",
        "Stuttero" => "stuttero",
        "Delay" => "delay",
        "Reverb" => "reverb",
        "Distortion" => "distortion",
        "Bitcrusher" => "bitcrusher",
        "Vocoder" => "vocoder",
        "Auto-Tune" => "autotune",
        "Stereo Width" => "stereowidth",
        "Live Difference" => "live-difference",
        "Utility" => "utility",
        "3D Scope" => "waveform-visualizer",
        _ => "utility"
    };
}
