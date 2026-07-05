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
        "Oscillator", "3x Osc", "FM Synth", "Wavetable", "Padda", "Basic Sampler", "Kicka", "Granular", "Sampler (SFZ)"
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
