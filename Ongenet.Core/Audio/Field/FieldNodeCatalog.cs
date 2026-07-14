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
        yield return new FieldNodeInfo(DrawbarOrganNode.Type, "Drawbar Organ", FieldNodeCategories.Oscillators, () => new DrawbarOrganNode());
        yield return new FieldNodeInfo(PhaseDistortionNode.Type, "Phase Distortion", FieldNodeCategories.Oscillators, () => new PhaseDistortionNode());
        yield return new FieldNodeInfo(KarplusNode.Type, "Karplus", FieldNodeCategories.Oscillators, () => new KarplusNode());
        yield return new FieldNodeInfo(PartialBankNode.Type, "Partial Bank", FieldNodeCategories.Oscillators, () => new PartialBankNode());

        // Envelopes
        yield return new FieldNodeInfo(AdsrNode.Type, "ADSR", FieldNodeCategories.Envelopes, () => new AdsrNode());
        yield return new FieldNodeInfo(DahdsrNode.Type, "DAHDSR", FieldNodeCategories.Envelopes, () => new DahdsrNode());
        yield return new FieldNodeInfo(CurveEnvNode.Type, "Curve Env", FieldNodeCategories.Envelopes, () => new CurveEnvNode());
        yield return new FieldNodeInfo(DrumPitchEnvNode.Type, "Drum Pitch Env", FieldNodeCategories.Envelopes, () => new DrumPitchEnvNode());
        yield return new FieldNodeInfo(EnvFollowerNode.Type, "Env Follower", FieldNodeCategories.Envelopes, () => new EnvFollowerNode());

        // Filters
        yield return new FieldNodeInfo(BiquadFilterNode.Type, "Filter", FieldNodeCategories.Filters, () => new BiquadFilterNode());
        yield return new FieldNodeInfo(OnePoleNode.Type, "One-Pole", FieldNodeCategories.Filters, () => new OnePoleNode());
        yield return new FieldNodeInfo(EqBandNode.Type, "EQ Band", FieldNodeCategories.Filters, () => new EqBandNode());
        yield return new FieldNodeInfo(CombNode.Type, "Comb", FieldNodeCategories.Filters, () => new CombNode());
        yield return new FieldNodeInfo(AllpassNode.Type, "All-Pass", FieldNodeCategories.Filters, () => new AllpassNode());
        yield return new FieldNodeInfo(AllpassDiffuserNode.Type, "Diffuser", FieldNodeCategories.Filters, () => new AllpassDiffuserNode());

        // Modulators
        yield return new FieldNodeInfo(LfoNode.Type, "LFO", FieldNodeCategories.Modulators, () => new LfoNode());
        yield return new FieldNodeInfo(DriftNode.Type, "Drift", FieldNodeCategories.Modulators, () => new DriftNode());
        yield return new FieldNodeInfo(RandomShNode.Type, "Random S&H", FieldNodeCategories.Modulators, () => new RandomShNode());
        yield return new FieldNodeInfo(PhasorNode.Type, "Phasor", FieldNodeCategories.Modulators, () => new PhasorNode());
        yield return new FieldNodeInfo(MacroNode.Type, "Macro", FieldNodeCategories.Modulators, () => new MacroNode());
        yield return new FieldNodeInfo(SegmentsNode.Type, "Segments", FieldNodeCategories.Modulators, () => new SegmentsNode());
        yield return new FieldNodeInfo(WavetableLfoNode.Type, "Wavetable LFO", FieldNodeCategories.Modulators, () => new WavetableLfoNode());
        yield return new FieldNodeInfo(BeatLfoNode.Type, "Beat LFO", FieldNodeCategories.Modulators, () => new BeatLfoNode());
        yield return new FieldNodeInfo(ClassicLfoNode.Type, "Classic LFO", FieldNodeCategories.Modulators, () => new ClassicLfoNode());
        yield return new FieldNodeInfo(StepsNode.Type, "Steps", FieldNodeCategories.Modulators, () => new StepsNode());
        yield return new FieldNodeInfo(FourStageNode.Type, "4-Stage", FieldNodeCategories.Modulators, () => new FourStageNode());
        yield return new FieldNodeInfo(RampNode.Type, "Ramp", FieldNodeCategories.Modulators, () => new RampNode());
        yield return new FieldNodeInfo(ButtonNode.Type, "Button", FieldNodeCategories.Modulators, () => new ButtonNode());
        yield return new FieldNodeInfo(Macro4Node.Type, "Macro-4", FieldNodeCategories.Modulators, () => new Macro4Node());
        yield return new FieldNodeInfo(XyCvNode.Type, "XY", FieldNodeCategories.Modulators, () => new XyCvNode());
        yield return new FieldNodeInfo(KeytrackNode.Type, "Keytrack+", FieldNodeCategories.Modulators, () => new KeytrackNode());
        yield return new FieldNodeInfo(MathCvNode.Type, "Math", FieldNodeCategories.Modulators, () => new MathCvNode());
        yield return new FieldNodeInfo(MixCvNode.Type, "Mix", FieldNodeCategories.Modulators, () => new MixCvNode());
        yield return new FieldNodeInfo(VibratoCvNode.Type, "Vibrato", FieldNodeCategories.Modulators, () => new VibratoCvNode());
        yield return new FieldNodeInfo(SampleHoldCvNode.Type, "Sample & Hold", FieldNodeCategories.Modulators, () => new SampleHoldCvNode());
        yield return new FieldNodeInfo(QuantizeCvNode.Type, "Quantize CV", FieldNodeCategories.Modulators, () => new QuantizeCvNode());
        yield return new FieldNodeInfo(Pitch12Node.Type, "Pitch-12", FieldNodeCategories.Modulators, () => new Pitch12Node());
        yield return new FieldNodeInfo(Select4Node.Type, "Select-4", FieldNodeCategories.Modulators, () => new Select4Node());
        yield return new FieldNodeInfo(Vector4Node.Type, "Vector-4", FieldNodeCategories.Modulators, () => new Vector4Node());
        yield return new FieldNodeInfo(BeatPhaseNode.Type, "Beat Phase", FieldNodeCategories.Modulators, () => new BeatPhaseNode());
        yield return new FieldNodeInfo(StackSpreadNode.Type, "Stack Spread", FieldNodeCategories.Modulators, () => new StackSpreadNode());
        yield return new FieldNodeInfo(ParSeq8Node.Type, "ParSeq-8", FieldNodeCategories.Modulators, () => new ParSeq8Node());
        yield return new FieldNodeInfo(GlobalsNode.Type, "Globals", FieldNodeCategories.Modulators, () => new GlobalsNode());

        // Containers
        yield return new FieldNodeInfo(ContainerLayerNode.Type, "Layer", FieldNodeCategories.Containers, () => new ContainerLayerNode());
        yield return new FieldNodeInfo(ContainerSelectorNode.Type, "Selector", FieldNodeCategories.Containers, () => new ContainerSelectorNode());
        yield return new FieldNodeInfo(ContainerMultibandNode.Type, "Multiband", FieldNodeCategories.Containers, () => new ContainerMultibandNode());

        // Spectral
        yield return new FieldNodeInfo(SpectralSplitNode.Type, "Freq Split", FieldNodeCategories.Spectral, () => new SpectralSplitNode());
        yield return new FieldNodeInfo(SpectralTransientNode.Type, "Transient Split", FieldNodeCategories.Spectral, () => new SpectralTransientNode());
        yield return new FieldNodeInfo(SpectralImportNode.Type, "Spectral Import", FieldNodeCategories.Spectral, () => new SpectralImportNode());

        // Shapers & dynamics
        yield return new FieldNodeInfo(WaveShaperNode.Type, "Waveshaper", FieldNodeCategories.Shapers, () => new WaveShaperNode());
        yield return new FieldNodeInfo(SoftClipNode.Type, "Soft Clip", FieldNodeCategories.Shapers, () => new SoftClipNode());
        yield return new FieldNodeInfo(BitcrusherNode.Type, "Bitcrusher", FieldNodeCategories.Shapers, () => new BitcrusherNode());
        yield return new FieldNodeInfo(DistortionStackNode.Type, "Distortion Stack", FieldNodeCategories.Shapers, () => new DistortionStackNode());
        yield return new FieldNodeInfo(RingModNode.Type, "Ring Mod", FieldNodeCategories.Shapers, () => new RingModNode());
        yield return new FieldNodeInfo(HarmonicSculptNode.Type, "Harmonic Sculpt", FieldNodeCategories.Shapers, () => new HarmonicSculptNode());
        yield return new FieldNodeInfo(CompressorNode.Type, "Compressor", FieldNodeCategories.Dynamics, () => new CompressorNode());

        // Time & space
        yield return new FieldNodeInfo(DelayNode.Type, "Delay", FieldNodeCategories.Time, () => new DelayNode());
        yield return new FieldNodeInfo(TapeStopNode.Type, "Tape Stop", FieldNodeCategories.Time, () => new TapeStopNode());
        yield return new FieldNodeInfo(PitchShiftNode.Type, "Pitch Shift", FieldNodeCategories.Time, () => new PitchShiftNode());
        yield return new FieldNodeInfo(FreqShiftNode.Type, "Freq Shift", FieldNodeCategories.Time, () => new FreqShiftNode());
        yield return new FieldNodeInfo(RotaryNode.Type, "Rotary", FieldNodeCategories.Time, () => new RotaryNode());
        yield return new FieldNodeInfo(ConvolutionNode.Type, "Convolution", FieldNodeCategories.Time, () => new ConvolutionNode());

        // Sampler
        yield return new FieldNodeInfo(SamplePlayerNode.Type, "Sample Player", FieldNodeCategories.Sampler, () => new SamplePlayerNode());
        yield return new FieldNodeInfo(DrumTriggerNode.Type, "Drum Trigger", FieldNodeCategories.Sampler, () => new DrumTriggerNode());
        yield return new FieldNodeInfo(DrumNoiseNode.Type, "Drum Noise", FieldNodeCategories.Sampler, () => new DrumNoiseNode());
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

        foreach (var info in FieldExpansionNodes.CatalogEntries())
            yield return info;
    }
}
