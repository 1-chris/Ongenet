using System.Collections.Generic;
using Ongenet.Core.Audio.Field.Nodes;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// The built-in Field node types. Kept in one place so <see cref="FieldNodeRegistry"/> and the loader agree
/// on the available primitives. Module-wrapper nodes (whole instruments/effects/plugins) are registered
/// separately at runtime via <see cref="FieldModuleNodes"/>.
/// </summary>
public static class FieldNodeCatalog
{
    public static IEnumerable<FieldNodeInfo> BuiltIns()
    {
        // I/O
        yield return new FieldNodeInfo(NoteInNode.Type, "Note In", FieldNodeCategories.Io, () => new NoteInNode());
        yield return new FieldNodeInfo(MidiInNode.Type, "MIDI In", FieldNodeCategories.Io, () => new MidiInNode());
        yield return new FieldNodeInfo(CcInNode.Type, "CC In", FieldNodeCategories.Io, () => new CcInNode());
        yield return new FieldNodeInfo(AudioInNode.Type, "Audio In", FieldNodeCategories.Io, () => new AudioInNode());
        yield return new FieldNodeInfo(SidechainInNode.Type, "Sidechain In", FieldNodeCategories.Io, () => new SidechainInNode());
        yield return new FieldNodeInfo(AudioOutNode.Type, "Audio Out", FieldNodeCategories.Io, () => new AudioOutNode());
        yield return new FieldNodeInfo(VoiceSumNode.Type, "Voice Sum", FieldNodeCategories.Io, () => new VoiceSumNode());
        yield return new FieldNodeInfo(ScopeNode.Type, "Scope", FieldNodeCategories.Io, () => new ScopeNode());

        // Oscillators
        yield return new FieldNodeInfo(WaveOscNode.Type, "Wave Osc", FieldNodeCategories.Oscillators, () => new WaveOscNode());
        yield return new FieldNodeInfo(NoiseNode.Type, "Noise", FieldNodeCategories.Oscillators, () => new NoiseNode());
        yield return new FieldNodeInfo(FmOperatorNode.Type, "FM Operator", FieldNodeCategories.Oscillators, () => new FmOperatorNode());
        yield return new FieldNodeInfo(UnisonOscNode.Type, "Unison Osc", FieldNodeCategories.Oscillators, () => new UnisonOscNode());
        yield return new FieldNodeInfo(WavetableOscNode.Type, "Wavetable Osc", FieldNodeCategories.Oscillators, () => new WavetableOscNode());

        // Envelopes
        yield return new FieldNodeInfo(AdsrNode.Type, "ADSR", FieldNodeCategories.Envelopes, () => new AdsrNode());
        yield return new FieldNodeInfo(DahdsrNode.Type, "DAHDSR", FieldNodeCategories.Envelopes, () => new DahdsrNode());
        yield return new FieldNodeInfo(CurveEnvNode.Type, "Curve Env", FieldNodeCategories.Envelopes, () => new CurveEnvNode());
        yield return new FieldNodeInfo(EnvFollowerNode.Type, "Env Follower", FieldNodeCategories.Envelopes, () => new EnvFollowerNode());

        // Filters
        yield return new FieldNodeInfo(BiquadFilterNode.Type, "Filter", FieldNodeCategories.Filters, () => new BiquadFilterNode());
        yield return new FieldNodeInfo(OnePoleNode.Type, "One-Pole", FieldNodeCategories.Filters, () => new OnePoleNode());
        yield return new FieldNodeInfo(EqBandNode.Type, "EQ Band", FieldNodeCategories.Filters, () => new EqBandNode());
        yield return new FieldNodeInfo(CombNode.Type, "Comb", FieldNodeCategories.Filters, () => new CombNode());
        yield return new FieldNodeInfo(AllpassNode.Type, "All-Pass", FieldNodeCategories.Filters, () => new AllpassNode());

        // Modulators
        yield return new FieldNodeInfo(LfoNode.Type, "LFO", FieldNodeCategories.Modulators, () => new LfoNode());
        yield return new FieldNodeInfo(DriftNode.Type, "Drift", FieldNodeCategories.Modulators, () => new DriftNode());
        yield return new FieldNodeInfo(RandomShNode.Type, "Random S&H", FieldNodeCategories.Modulators, () => new RandomShNode());
        yield return new FieldNodeInfo(PhasorNode.Type, "Phasor", FieldNodeCategories.Modulators, () => new PhasorNode());
        yield return new FieldNodeInfo(MacroNode.Type, "Macro", FieldNodeCategories.Modulators, () => new MacroNode());

        // Shapers & dynamics
        yield return new FieldNodeInfo(WaveShaperNode.Type, "Waveshaper", FieldNodeCategories.Shapers, () => new WaveShaperNode());
        yield return new FieldNodeInfo(SoftClipNode.Type, "Soft Clip", FieldNodeCategories.Shapers, () => new SoftClipNode());
        yield return new FieldNodeInfo(BitcrusherNode.Type, "Bitcrusher", FieldNodeCategories.Shapers, () => new BitcrusherNode());
        yield return new FieldNodeInfo(DistortionStackNode.Type, "Distortion Stack", FieldNodeCategories.Shapers, () => new DistortionStackNode());
        yield return new FieldNodeInfo(CompressorNode.Type, "Compressor", FieldNodeCategories.Dynamics, () => new CompressorNode());

        // Time & space
        yield return new FieldNodeInfo(DelayNode.Type, "Delay", FieldNodeCategories.Time, () => new DelayNode());
        yield return new FieldNodeInfo(TapeStopNode.Type, "Tape Stop", FieldNodeCategories.Time, () => new TapeStopNode());
        yield return new FieldNodeInfo(PitchShiftNode.Type, "Pitch Shift", FieldNodeCategories.Time, () => new PitchShiftNode());

        // Sampler
        yield return new FieldNodeInfo(SamplePlayerNode.Type, "Sample Player", FieldNodeCategories.Sampler, () => new SamplePlayerNode());
        yield return new FieldNodeInfo(SoundFontNode.Type, "SoundFont", FieldNodeCategories.Sampler, () => new SoundFontNode());
        yield return new FieldNodeInfo(SamplerNode.Type, "Sampler", FieldNodeCategories.Sampler, () => new SamplerNode());

        // Math & logic
        yield return new FieldNodeInfo(ConstantNode.Type, "Constant", FieldNodeCategories.Math, () => new ConstantNode());
        yield return new FieldNodeInfo(GainNode.Type, "Gain", FieldNodeCategories.Math, () => new GainNode());
        yield return new FieldNodeInfo(AddNode.Type, "Add", FieldNodeCategories.Math, () => new AddNode());
        yield return new FieldNodeInfo(MultiplyNode.Type, "Multiply", FieldNodeCategories.Math, () => new MultiplyNode());
        yield return new FieldNodeInfo(MixNode.Type, "Mix", FieldNodeCategories.Math, () => new MixNode());
        yield return new FieldNodeInfo(ScaleOffsetNode.Type, "Scale/Offset", FieldNodeCategories.Math, () => new ScaleOffsetNode());
        yield return new FieldNodeInfo(ClampNode.Type, "Clamp", FieldNodeCategories.Math, () => new ClampNode());
        yield return new FieldNodeInfo(InvertNode.Type, "Invert", FieldNodeCategories.Math, () => new InvertNode());
        yield return new FieldNodeInfo(PanNode.Type, "Pan", FieldNodeCategories.Math, () => new PanNode());
        yield return new FieldNodeInfo(ComparatorNode.Type, "Comparator", FieldNodeCategories.Logic, () => new ComparatorNode());
        yield return new FieldNodeInfo(QuantizeNode.Type, "Quantize", FieldNodeCategories.Logic, () => new QuantizeNode());
        yield return new FieldNodeInfo(SampleHoldNode.Type, "Sample & Hold", FieldNodeCategories.Logic, () => new SampleHoldNode());

        // Music theory
        yield return new FieldNodeInfo(ScaleQuantizeNode.Type, "Scale Quantize", FieldNodeCategories.Music, () => new ScaleQuantizeNode());
        yield return new FieldNodeInfo(KeyRootNode.Type, "Key Root", FieldNodeCategories.Music, () => new KeyRootNode());
    }
}
