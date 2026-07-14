using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Containers;
using Ongenet.Core.Audio.Effects.Spectral;
using Ongenet.Core.Audio.Hardware;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Default registry of built-in effects.</summary>
public sealed class EffectRegistry : IEffectRegistry
{
    private readonly object _lock = new();
    private const string CatDynamics = "Dynamics";
    private const string CatEqFilter = "EQ & Filter";
    private const string CatModulation = "Modulation";
    private const string CatDelayReverb = "Delay & Reverb";
    private const string CatDistortion = "Distortion";
    private const string CatPitch = "Pitch";
    private const string CatUtility = "Utility";
    private const string CatVisualizer = "Visualizer";
    private const string CatSpectral = "Spectral";
    private const string CatHardware = "Hardware";
    private const string CatContainers = "Containers";

    private readonly List<EffectInfo> _builtIn = new()
    {
        // EQ & Filter
        new EffectInfo(EqEffect.TypeId, "EQ", () => new EqEffect(), CatEqFilter),
        new EffectInfo(Eq2Effect.TypeId, "EQ-2", () => new Eq2Effect(), CatEqFilter),
        new EffectInfo(Eq5Effect.TypeId, "EQ-5", () => new Eq5Effect(), CatEqFilter),
        new EffectInfo(EqPlusEffect.TypeId, "EQ+", () => new EqPlusEffect(), CatEqFilter),
        new EffectInfo(EqDjEffect.TypeId, "EQ-DJ", () => new EqDjEffect(), CatEqFilter),
        new EffectInfo(MidSideEqEffect.TypeId, "Mid/Side EQ", () => new MidSideEqEffect(), CatEqFilter),
        new EffectInfo(TiltEffect.TypeId, "Tilt", () => new TiltEffect(), CatEqFilter),
        new EffectInfo(FilterEffect.TypeId, "Filter", () => new FilterEffect(), CatEqFilter),
        new EffectInfo(FilterPlusEffect.TypeId, "Filter+", () => new FilterPlusEffect(), CatEqFilter),
        new EffectInfo(LadderEffect.TypeId, "Ladder", () => new LadderEffect(), CatEqFilter),
        new EffectInfo(CombEffect.TypeId, "Comb", () => new CombEffect(), CatEqFilter),
        new EffectInfo(FocusEffect.TypeId, "Focus", () => new FocusEffect(), CatEqFilter),
        new EffectInfo(SculptEffect.TypeId, "Sculpt", () => new SculptEffect(), CatEqFilter),
        new EffectInfo(ResonatorBankEffect.TypeId, "Resonator Bank", () => new ResonatorBankEffect(), CatEqFilter),
        new EffectInfo(SweepEffect.TypeId, "Sweep", () => new SweepEffect(), CatEqFilter),

        // Dynamics
        new EffectInfo(CompressorEffect.TypeId, "Compressor", () => new CompressorEffect(), CatDynamics),
        new EffectInfo(CompressorPlusEffect.TypeId, "Compressor+", () => new CompressorPlusEffect(), CatDynamics),
        new EffectInfo(DynamicsEffect.TypeId, "Dynamics", () => new DynamicsEffect(), CatDynamics),
        new EffectInfo(MultibandCompressorEffect.TypeId, "Multiband (OTT)", () => new MultibandCompressorEffect(), CatDynamics),
        new EffectInfo(LimiterEffect.TypeId, "Limiter", () => new LimiterEffect(), CatDynamics),
        new EffectInfo(PeakLimiterEffect.TypeId, "Peak Limiter", () => new PeakLimiterEffect(), CatDynamics),
        new EffectInfo(GateEffect.TypeId, "Gate", () => new GateEffect(), CatDynamics),
        new EffectInfo(SidechainEffect.TypeId, "Sidechain", () => new SidechainEffect(), CatDynamics),
        new EffectInfo(DeEsserEffect.TypeId, "De-Esser", () => new DeEsserEffect(), CatDynamics),
        new EffectInfo(TransientControlEffect.TypeId, "Transient Control", () => new TransientControlEffect(), CatDynamics),

        // Modulation
        new EffectInfo(ChorusEffect.TypeId, "Chorus", () => new ChorusEffect(), CatModulation),
        new EffectInfo(ChorusPlusEffect.TypeId, "Chorus+", () => new ChorusPlusEffect(), CatModulation),
        new EffectInfo(PhaserEffect.TypeId, "Phaser", () => new PhaserEffect(), CatModulation),
        new EffectInfo(PhaserPlusEffect.TypeId, "Phaser+", () => new PhaserPlusEffect(), CatModulation),
        new EffectInfo(FlangerEffect.TypeId, "Flanger", () => new FlangerEffect(), CatModulation),
        new EffectInfo(FlangerPlusEffect.TypeId, "Flanger+", () => new FlangerPlusEffect(), CatModulation),
        new EffectInfo(TremoloEffect.TypeId, "Tremolo", () => new TremoloEffect(), CatModulation),
        new EffectInfo(StutteroEffect.TypeId, "Stuttero", () => new StutteroEffect(), CatModulation),
        new EffectInfo(RotaryEffect.TypeId, "Rotary", () => new RotaryEffect(), CatModulation),
        new EffectInfo(BlurEffect.TypeId, "Blur", () => new BlurEffect(), CatModulation),
        new EffectInfo(TreemonsterEffect.TypeId, "Treemonster", () => new TreemonsterEffect(), CatModulation),

        // Delay & Reverb
        new EffectInfo(DelayEffect.TypeId, "Delay", () => new DelayEffect(), CatDelayReverb),
        new EffectInfo(Delay1Effect.TypeId, "Delay-1", () => new Delay1Effect(), CatDelayReverb),
        new EffectInfo(Delay2Effect.TypeId, "Delay-2", () => new Delay2Effect(), CatDelayReverb),
        new EffectInfo(Delay4Effect.TypeId, "Delay-4", () => new Delay4Effect(), CatDelayReverb),
        new EffectInfo(DelayPlusEffect.TypeId, "Delay+", () => new DelayPlusEffect(), CatDelayReverb),
        new EffectInfo(ReverbEffect.TypeId, "Reverb", () => new ReverbEffect(), CatDelayReverb),
        new EffectInfo(ConvolutionEffect.TypeId, "Convolution", () => new ConvolutionEffect(), CatDelayReverb),

        // Distortion
        new EffectInfo(DistortionEffect.TypeId, "Distortion", () => new DistortionEffect(), CatDistortion),
        new EffectInfo(SaturatorEffect.TypeId, "Saturator", () => new SaturatorEffect(), CatDistortion),
        new EffectInfo(AmpEffect.TypeId, "Amp", () => new AmpEffect(), CatDistortion),
        new EffectInfo(OverEffect.TypeId, "Over", () => new OverEffect(), CatDistortion),
        new EffectInfo(ExciterEffect.TypeId, "Exciter", () => new ExciterEffect(), CatDistortion),
        new EffectInfo(ClipperEffect.TypeId, "Clipper", () => new ClipperEffect(), CatDistortion),
        new EffectInfo(BitcrusherEffect.TypeId, "Bitcrusher", () => new BitcrusherEffect(), CatDistortion),

        // Pitch
        new EffectInfo(VocoderEffect.TypeId, "Vocoder", () => new VocoderEffect(), CatPitch),
        new EffectInfo(AutoTuneEffect.TypeId, "Auto-Tune", () => new AutoTuneEffect(), CatPitch),
        new EffectInfo(PitchShiftEffect.TypeId, "Pitch Shifter", () => new PitchShiftEffect(), CatPitch),
        new EffectInfo(FreqShiftEffect.TypeId, "Freq Shifter", () => new FreqShiftEffect(), CatPitch),
        new EffectInfo(FreqShiftPlusEffect.TypeId, "Freq Shifter+", () => new FreqShiftPlusEffect(), CatPitch),
        new EffectInfo(RingModEffect.TypeId, "Ring-Mod", () => new RingModEffect(), CatPitch),
        new EffectInfo(TimeShiftEffect.TypeId, "Time Shift", () => new TimeShiftEffect(), CatPitch),

        // Utility
        new EffectInfo(StereoWidthEffect.TypeId, "Stereo Width", () => new StereoWidthEffect(), CatUtility),
        new EffectInfo(DualPanEffect.TypeId, "Dual Pan", () => new DualPanEffect(), CatUtility),
        new EffectInfo(DcOffsetEffect.TypeId, "DC Offset", () => new DcOffsetEffect(), CatUtility),
        new EffectInfo(LiveDifferenceEffect.TypeId, "Live Difference", () => new LiveDifferenceEffect(), CatUtility),
        new EffectInfo(UtilityEffect.TypeId, "Utility", () => new UtilityEffect(), CatUtility),
        new EffectInfo(ToolEffect.TypeId, "Tool", () => new ToolEffect(), CatUtility),
        new EffectInfo(TestToneEffect.TypeId, "Test Tone", () => new TestToneEffect(), CatUtility),
        new EffectInfo(TunerEffect.TypeId, "Tuner", () => new TunerEffect(), CatUtility),

        // Visualizer
        new EffectInfo(WaveformVisualizerEffect.TypeId, "3D Scope", () => new WaveformVisualizerEffect(), CatVisualizer),
        new EffectInfo(OscilloscopeEffect.TypeId, "Oscilloscope", () => new OscilloscopeEffect(), CatVisualizer),
        new EffectInfo(SpectrumEffect.TypeId, "Spectrum", () => new SpectrumEffect(), CatVisualizer),

        // Spectral
        new EffectInfo(FreqSplitEffect.TypeId, "Freq Split", () => new FreqSplitEffect(), CatSpectral),
        new EffectInfo(HarmonicSplitEffect.TypeId, "Harmonic Split", () => new HarmonicSplitEffect(), CatSpectral),
        new EffectInfo(LoudSplitEffect.TypeId, "Loud Split", () => new LoudSplitEffect(), CatSpectral),
        new EffectInfo(TransientSplitEffect.TypeId, "Transient Split", () => new TransientSplitEffect(), CatSpectral),

        // Hardware
        new EffectInfo(HwFxEffect.TypeId, "HW FX", () => new HwFxEffect(), CatHardware),
        new EffectInfo(HwCvInEffect.TypeId, "HW CV In", () => new HwCvInEffect(), CatHardware),
        new EffectInfo(HwCvOutEffect.TypeId, "HW CV Out", () => new HwCvOutEffect(), CatHardware),
        new EffectInfo(HwClockOutEffect.TypeId, "HW Clock Out", () => new HwClockOutEffect(), CatHardware),

        // Containers
        new EffectInfo(FxLayerEffect.TypeId, "FX Layer", () => new FxLayerEffect(), CatContainers),
        new EffectInfo(FxSelectorEffect.TypeId, "FX Selector", () => new FxSelectorEffect(), CatContainers),
        new EffectInfo(MultibandFxEffect.TypeId2, "Multiband FX-2", () => new MultibandFxEffect(2), CatContainers),
        new EffectInfo(MultibandFxEffect.TypeId3, "Multiband FX-3", () => new MultibandFxEffect(3), CatContainers),
        new EffectInfo(MidSideSplitEffect.TypeId, "Mid-Side Split", () => new MidSideSplitEffect(), CatContainers),
        new EffectInfo(StereoSplitEffect.TypeId, "Stereo Split", () => new StereoSplitEffect(), CatContainers),
        new EffectInfo(XyFxEffect.TypeId, "XY FX", () => new XyFxEffect(), CatContainers),
        new EffectInfo(AudioReceiverEffect.TypeId, "Audio Receiver", () => new AudioReceiverEffect(), CatContainers),
        new EffectInfo(NoteFxLayerEffect.TypeId, "Note FX Layer", () => new NoteFxLayerEffect(), CatContainers),
        new EffectInfo(NoteFxSelectorEffect.TypeId, "Note FX Selector", () => new NoteFxSelectorEffect(), CatContainers),
        new EffectInfo(NoteReceiverEffect.TypeId, "Note Receiver", () => new NoteReceiverEffect(), CatContainers),
    };

    private readonly List<EffectInfo> _dynamic = new();
    private Func<string, IAudioEffect?>? _fallbackCreate;

    public event Action? Changed;

    public IReadOnlyList<EffectInfo> Available
    {
        get { lock (_lock) return _builtIn.Concat(_dynamic).ToList(); }
    }

    public IAudioEffect Create(string id)
    {
        if (id == "bit8") id = BitcrusherEffect.TypeId;

        EffectInfo? info;
        lock (_lock) info = _builtIn.Concat(_dynamic).FirstOrDefault(e => e.Id == id);
        if (info is not null) return info.Create();

        var fallback = _fallbackCreate?.Invoke(id);
        if (fallback is not null) return fallback;

        throw new ArgumentException($"Unknown effect type '{id}'.", nameof(id));
    }

    public void Register(EffectInfo info)
    {
        lock (_lock)
        {
            if (_builtIn.Any(e => e.Id == info.Id)) return;
            var existing = _dynamic.FindIndex(e => e.Id == info.Id);
            if (existing >= 0) _dynamic[existing] = info;
            else _dynamic.Add(info);
        }

        Changed?.Invoke();
    }

    public bool Unregister(string id)
    {
        lock (_lock)
        {
            var removed = _dynamic.RemoveAll(e => e.Id == id) > 0;
            if (!removed) return false;
        }

        Changed?.Invoke();
        return true;
    }

    public void SetFallbackCreate(Func<string, IAudioEffect?> fallback) => _fallbackCreate = fallback;
}
