using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Default registry of built-in instruments. Currently exposes the <see cref="OscillatorInstrument"/>;
/// new instruments are added by appending to <see cref="Available"/>.
/// </summary>
public sealed class InstrumentRegistry : IInstrumentRegistry
{
    private readonly object _lock = new();
    private readonly List<InstrumentInfo> _builtIn = new()
    {
        new InstrumentInfo(OscillatorInstrument.TypeId, "Oscillator", () => new OscillatorInstrument()),
        new InstrumentInfo(TripleOscInstrument.TypeId, "3x Osc", () => new TripleOscInstrument()),
        new InstrumentInfo(FmSynthInstrument.TypeId, "FM Synth", () => new FmSynthInstrument()),
        new InstrumentInfo(BasicSamplerInstrument.TypeId, "Basic Sampler", () => new BasicSamplerInstrument()),
        new InstrumentInfo(GranularInstrument.TypeId, "Granular", () => new GranularInstrument())
    };

    // Dynamically discovered instruments (e.g. CLAP plugins), added at runtime.
    private readonly List<InstrumentInfo> _dynamic = new();

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

        if (info is null) throw new ArgumentException($"Unknown instrument type '{id}'.", nameof(id));
        return info.Create();
    }

    public void Register(InstrumentInfo info)
    {
        lock (_lock)
        {
            if (_builtIn.Any(i => i.Id == info.Id) || _dynamic.Any(i => i.Id == info.Id)) return;
            _dynamic.Add(info);
        }

        Changed?.Invoke();
    }

    /// <summary>The id of the instrument used for new instrument tracks.</summary>
    public static string DefaultInstrumentId => OscillatorInstrument.TypeId;
}
