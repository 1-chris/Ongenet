using System;
using System.Collections.Generic;
using System.Linq;

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

    private readonly List<EffectInfo> _builtIn = new()
    {
        new EffectInfo(EqEffect.TypeId, "EQ", () => new EqEffect(), CatEqFilter),
        new EffectInfo(FilterEffect.TypeId, "Filter", () => new FilterEffect(), CatEqFilter),
        new EffectInfo(CompressorEffect.TypeId, "Compressor", () => new CompressorEffect(), CatDynamics),
        new EffectInfo(LimiterEffect.TypeId, "Limiter", () => new LimiterEffect(), CatDynamics),
        new EffectInfo(GateEffect.TypeId, "Gate", () => new GateEffect(), CatDynamics),
        new EffectInfo(SidechainEffect.TypeId, "Sidechain", () => new SidechainEffect(), CatDynamics),
        new EffectInfo(ChorusEffect.TypeId, "Chorus", () => new ChorusEffect(), CatModulation),
        new EffectInfo(PhaserEffect.TypeId, "Phaser", () => new PhaserEffect(), CatModulation),
        new EffectInfo(FlangerEffect.TypeId, "Flanger", () => new FlangerEffect(), CatModulation),
        new EffectInfo(TremoloEffect.TypeId, "Tremolo", () => new TremoloEffect(), CatModulation),
        new EffectInfo(StutteroEffect.TypeId, "Stuttero", () => new StutteroEffect(), CatModulation),
        new EffectInfo(DelayEffect.TypeId, "Delay", () => new DelayEffect(), CatDelayReverb),
        new EffectInfo(ReverbEffect.TypeId, "Reverb", () => new ReverbEffect(), CatDelayReverb),
        new EffectInfo(DistortionEffect.TypeId, "Distortion", () => new DistortionEffect(), CatDistortion),
        new EffectInfo(BitcrusherEffect.TypeId, "Bitcrusher", () => new BitcrusherEffect(), CatDistortion),
        new EffectInfo(VocoderEffect.TypeId, "Vocoder", () => new VocoderEffect(), CatPitch),
        new EffectInfo(AutoTuneEffect.TypeId, "Auto-Tune", () => new AutoTuneEffect(), CatPitch),
        new EffectInfo(StereoWidthEffect.TypeId, "Stereo Width", () => new StereoWidthEffect(), CatUtility),
        new EffectInfo(LiveDifferenceEffect.TypeId, "Live Difference", () => new LiveDifferenceEffect(), CatUtility),
        new EffectInfo(UtilityEffect.TypeId, "Utility", () => new UtilityEffect(), CatUtility),
        new EffectInfo(WaveformVisualizerEffect.TypeId, "3D Scope", () => new WaveformVisualizerEffect(), CatVisualizer)
    };

    // Dynamically discovered effects (e.g. CLAP audio-effect plugins), added at runtime.
    private readonly List<EffectInfo> _dynamic = new();

    public event Action? Changed;

    public IReadOnlyList<EffectInfo> Available
    {
        get { lock (_lock) return _builtIn.Concat(_dynamic).ToList(); }
    }

    public IAudioEffect Create(string id)
    {
        EffectInfo? info;
        lock (_lock) info = _builtIn.Concat(_dynamic).FirstOrDefault(e => e.Id == id);
        if (info is null) throw new ArgumentException($"Unknown effect type '{id}'.", nameof(id));
        return info.Create();
    }

    public void Register(EffectInfo info)
    {
        lock (_lock)
        {
            if (_builtIn.Any(e => e.Id == info.Id) || _dynamic.Any(e => e.Id == info.Id)) return;
            _dynamic.Add(info);
        }

        Changed?.Invoke();
    }
}
