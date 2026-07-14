using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Containers;
using Ongenet.Core.Audio.Hardware;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Default registry of built-in instruments. Dynamically registered types (Field, plugins, user Field
/// instruments) live in a separate list that can be rescanned.
/// </summary>
public sealed class InstrumentRegistry : IInstrumentRegistry
{
    private readonly object _lock = new();
    private const string CatSynth = "Synth";
    private const string CatSampler = "Sampler";
    private const string CatDrum = "Drum";
    private const string CatHardware = "Hardware";
    private const string CatContainers = "Containers";

    private readonly List<InstrumentInfo> _builtIn = new()
    {
        new InstrumentInfo(OscillatorInstrument.TypeId, "Oscillator", () => new OscillatorInstrument(), CatSynth),
        new InstrumentInfo(TripleOscInstrument.TypeId, "3x Osc", () => new TripleOscInstrument(), CatSynth),
        new InstrumentInfo(WavetableInstrument.TypeId, "Wavetable", () => new WavetableInstrument(), CatSynth),
        new InstrumentInfo(FmSynthInstrument.TypeId, "FM Synth", () => new FmSynthInstrument(), CatSynth),
        new InstrumentInfo(BassSynthInstrument.TypeId, "Bass Synth", () => new BassSynthInstrument(), CatSynth),
        new InstrumentInfo(PaddaInstrument.TypeId, "Padda", () => new PaddaInstrument(), CatSynth),
        new InstrumentInfo(BasicSamplerInstrument.TypeId, "Basic Sampler", () => new BasicSamplerInstrument(), CatSampler),
        new InstrumentInfo(Sampler.SamplerInstrument.TypeId, "Sampler", () => new Sampler.SamplerInstrument(), CatSampler),
        new InstrumentInfo(GranularInstrument.TypeId, "Granular", () => new GranularInstrument(), CatSampler),
        new InstrumentInfo(KickaInstrument.TypeId, "Kicka", () => new KickaInstrument(), CatDrum),
        new InstrumentInfo(PercaInstrument.TypeId, "Perca", () => new PercaInstrument(), CatDrum),
        new InstrumentInfo(OrganInstrument.TypeId, "Organ", () => new OrganInstrument(), CatSynth),
        new InstrumentInfo(Phase4Instrument.TypeId, "Phase-4", () => new Phase4Instrument(), CatSynth),
        new InstrumentInfo(PolymerInstrument.TypeId, "Polymer", () => new PolymerInstrument(), CatSynth),
        new InstrumentInfo(PolysynthInstrument.TypeId, "Polysynth", () => new PolysynthInstrument(), CatSynth),
        new InstrumentInfo(DrumModelInstrument.TypeId, "Drum Model", () => new DrumModelInstrument(), CatDrum),

        // Hardware
        new InstrumentInfo(HwInstrument.TypeId, "HW Instrument", () => new HwInstrument(), CatHardware),
        new InstrumentInfo(HwCvInstrument.TypeId, "HW CV Instrument", () => new HwCvInstrument(), CatHardware),

        // Containers
        new InstrumentInfo(DrumMachineInstrument.TypeId, "Drum Machine", () => new DrumMachineInstrument(), CatContainers),
        new InstrumentInfo(InstrumentLayerInstrument.TypeId, "Instrument Layer", () => new InstrumentLayerInstrument(), CatContainers),
        new InstrumentInfo(InstrumentSelectorInstrument.TypeId, "Instrument Selector", () => new InstrumentSelectorInstrument(), CatContainers),
        new InstrumentInfo(ChainInstrument.TypeId, "Chain", () => new ChainInstrument(), CatContainers),
        new InstrumentInfo(XyInstrument.TypeId, "XY Instrument", () => new XyInstrument(), CatContainers),
        new InstrumentInfo(ReplacerInstrument.TypeId, "Replacer", () => new ReplacerInstrument(), CatContainers),
    };

    private readonly List<InstrumentInfo> _dynamic = new();
    private Func<string, IInstrument?>? _fallbackCreate;

    public event Action? Changed;

    public IReadOnlyList<InstrumentInfo> Available
    {
        get
        {
            lock (_lock) return _builtIn.Concat(_dynamic).ToList();
        }
    }

    public IInstrument Create(string id)
    {
        InstrumentInfo? info;
        lock (_lock)
        {
            info = _builtIn.Concat(_dynamic).FirstOrDefault(i => i.Id == id);
        }

        if (info is not null) return info.Create();

        var fallback = _fallbackCreate?.Invoke(id);
        if (fallback is not null) return fallback;

        throw new ArgumentException($"Unknown instrument type '{id}'.", nameof(id));
    }

    public void Register(InstrumentInfo info)
    {
        lock (_lock)
        {
            if (_builtIn.Any(i => i.Id == info.Id)) return;
            var existing = _dynamic.FindIndex(i => i.Id == info.Id);
            if (existing >= 0) _dynamic[existing] = info;
            else _dynamic.Add(info);
        }

        Changed?.Invoke();
    }

    public bool Unregister(string id)
    {
        lock (_lock)
        {
            var removed = _dynamic.RemoveAll(i => i.Id == id) > 0;
            if (!removed) return false;
        }

        Changed?.Invoke();
        return true;
    }

    public void SetFallbackCreate(Func<string, IInstrument?> fallback) => _fallbackCreate = fallback;

    /// <summary>The id of the instrument used for new instrument tracks.</summary>
    public static string DefaultInstrumentId => OscillatorInstrument.TypeId;
}
