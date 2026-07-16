using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Containers;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Persistence.Import;

/// <summary>Maps foreign stock instrument / generator names to Ongenet instrument TypeIds.</summary>
public static class StockInstrumentMap
{
    private static readonly Dictionary<string, string> FlStudio = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sampler"] = BasicSamplerInstrument.TypeId,
        ["AudioClip"] = BasicSamplerInstrument.TypeId,
        ["3x Osc"] = TripleOscInstrument.TypeId,
        ["3xOsc"] = TripleOscInstrument.TypeId,
        ["Bootsrap"] = TripleOscInstrument.TypeId, // legacy FL misspelling seen in dumps
        ["BeepMap"] = TripleOscInstrument.TypeId,
        ["GMS"] = PolymerInstrument.TypeId,
        ["FL Keys"] = PolymerInstrument.TypeId,
        ["BooBass"] = PolymerInstrument.TypeId,
        ["Plucked"] = PolymerInstrument.TypeId,
        ["Fruity Granulizer"] = GranularInstrument.TypeId,
        ["Granulizer"] = GranularInstrument.TypeId,
        ["Fruity DrumSynth Live"] = DrumModelInstrument.TypeId,
        ["DrumSynth Live"] = DrumModelInstrument.TypeId,
        ["Fruity Kick"] = KickaInstrument.TypeId,
        ["Fruit Kick"] = KickaInstrument.TypeId,
        ["Fruity DX10"] = FmSynthInstrument.TypeId,
        ["DX10"] = FmSynthInstrument.TypeId,
        ["Harmless"] = WavetableInstrument.TypeId,
        ["Harmor"] = WavetableInstrument.TypeId,
        ["Sytrus"] = FmSynthInstrument.TypeId,
        ["Flex"] = PolymerInstrument.TypeId,
        ["FPC"] = DrumMachineInstrument.TypeId,
        ["Fruity Slicer"] = BasicSamplerInstrument.TypeId,
        ["Slicer"] = BasicSamplerInstrument.TypeId,
        ["Fruity WaveTraveller"] = GranularInstrument.TypeId,
    };

    public static bool TryMap(string sourceFormat, string deviceName, out string typeId)
    {
        typeId = "";
        if (string.IsNullOrWhiteSpace(deviceName)) return false;

        if (!sourceFormat.Equals("flp", StringComparison.OrdinalIgnoreCase) &&
            !sourceFormat.Equals("flstudio", StringComparison.OrdinalIgnoreCase))
            return false;

        var name = deviceName.Trim();
        if (FlStudio.TryGetValue(name, out typeId!))
            return true;

        foreach (var (key, value) in FlStudio)
        {
            if (name.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                typeId = value;
                return true;
            }
        }

        return false;
    }

    public static bool IsKnownStock(string sourceFormat, string deviceName) =>
        TryMap(sourceFormat, deviceName, out _);
}
