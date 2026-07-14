using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>Tag metadata for factory presets that are not defined inline on <see cref="FactoryPresets"/> records.</summary>
public static class FactoryPresetTags
{
    private static readonly Dictionary<(string Group, string Name), string[]> Exact = new()
    {
        [("Kicka", "DnB Kick")] = ["drum", "kick", "dnb"],
        [("Kicka", "Techno Punch")] = ["drum", "kick", "techno"],
        [("Padda", "Sub Bass")] = ["bass", "sub", "808"],
        [("Perca", "Tight Snare")] = ["drum", "snare"],
        [("Field", "Prism Lead")] = ["lead", "field", "synth"],
        [("Field", "Reese Bass")] = ["bass", "reese", "field"],
    };

    public static IReadOnlyList<string> For(string group, string presetName, IReadOnlyList<string>? inlineTags = null)
    {
        if (inlineTags is { Count: > 0 }) return inlineTags;
        if (Exact.TryGetValue((group, presetName), out var tags)) return tags;
        return Infer(group, presetName);
    }

    private static string[] Infer(string group, string presetName)
    {
        var tags = new List<string> { NormalizeGroup(group) };
        var lower = presetName.ToLowerInvariant();
        if (lower.Contains("bass") || lower.Contains("sub") || lower.Contains("reese")) tags.Add("bass");
        if (lower.Contains("lead") || lower.Contains("stab") || lower.Contains("saw")) tags.Add("lead");
        if (lower.Contains("pad") || lower.Contains("ambient") || lower.Contains("wash")) tags.Add("pad");
        if (lower.Contains("pluck")) tags.Add("pluck");
        if (lower.Contains("kick") || lower.Contains("808") || lower.Contains("909")) tags.Add("drum");
        if (lower.Contains("snare") || lower.Contains("hat") || lower.Contains("clap")) tags.Add("drum");
        if (lower.Contains("keys") || lower.Contains("piano") || lower.Contains("organ")) tags.Add("keys");
        if (lower.Contains("fx") || lower.Contains("riser") || lower.Contains("noise")) tags.Add("fx");
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizeGroup(string group) => group switch
    {
        "3x Osc" or "FM Synth" or "Oscillator" or "Wavetable" or "Polysynth" or "Polymer" or "Phase-4" => "synth",
        "Bass Synth" => "bass",
        "Granular" or "Basic Sampler" => "sampler",
        "Organ" => "keys",
        "Drum Model" or "Kicka" or "Padda" or "Perca" => "drum",
        "Field Patches" or "Field Effects" => "field",
        _ => group.ToLowerInvariant()
    };
}
