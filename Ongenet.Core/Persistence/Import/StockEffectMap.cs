using System;
using System.Collections.Generic;

namespace Ongenet.Core.Persistence.Import;

/// <summary>Maps foreign stock device names to Ongenet effect TypeIds (best-effort).</summary>
public static class StockEffectMap
{
    private static readonly Dictionary<string, string> FlStudio = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fruity parametric EQ 2"] = "eq_plus",
        ["Fruity Parametric EQ 2"] = "eq_plus",
        ["Fruity parametric EQ"] = "eq",
        ["Fruity Parametric EQ"] = "eq",
        ["Fruity EQ 2"] = "eq2",
        ["Fruity 7 Band EQ"] = "eq5",
        ["EQ"] = "eq",
        ["Fruity Delay 3"] = "delay",
        ["Fruity Delay 2"] = "delay2",
        ["Fruity Delay"] = "delay",
        ["Fruity Delay Bank"] = "delay_plus",
        ["Delay"] = "delay",
        ["Fruity Reverb 2"] = "reverb",
        ["Fruity Reeverb 2"] = "reverb", // FL internal spelling
        ["Fruity Reverb"] = "reverb",
        ["Fruity Reeverb"] = "reverb",
        ["Reverb"] = "reverb",
        ["Fruity Compressor"] = "compressor",
        ["Compressor"] = "compressor",
        ["Fruity Limiter"] = "limiter",
        ["Limiter"] = "limiter",
        ["Fruity Soft Clipper"] = "clipper",
        ["Fruity WaveShaper"] = "distortion",
        ["Fruity Blood Overdrive"] = "over",
        ["Fruity Fast Dist"] = "distortion",
        ["Fruity Squeeze"] = "compressor",
        ["Fruity Multiband Compressor"] = "multiband_comp",
        ["Maximus"] = "multiband_comp",
        ["Soundgoodizer"] = "saturator",
        ["Transient Processor"] = "transient_control",
        ["Gross Beat"] = "stuttero",
        ["Fruity Chorus"] = "chorus",
        ["Fruity Flanger"] = "flanger",
        ["Fruity Phaser"] = "phaser",
        ["Fruity Filter"] = "filter",
        ["Fruity Love Philter"] = "filter_plus",
        ["Fruity Stereo Enhancer"] = "stereowidth",
        ["Fruity Balance"] = "utility",
        ["Fruity Stereo Shaper"] = "stereowidth",
        ["Fruity Convolver"] = "convolution",
        ["Fruity Vocoder"] = "vocoder",
        ["Fruity Pitch Shifter"] = "pitch_shifter",
        ["Fruity Notebook"] = "utility",
        ["Fruity Formula Controller"] = "utility",
        ["Fruity Peak Controller"] = "utility",
        ["Fruity X-Y Controller"] = "utility",
        ["Fruity HTML Notebook"] = "utility",
        ["Fruity Dance"] = "delay",
        ["Fruity Scratcher"] = "utility",
        ["Fruity PanOMatic"] = "tremolo",
        ["Fruity Tremolo"] = "tremolo",
        ["Fruity Crusher"] = "bitcrusher",
        ["Fruity Spectroman"] = "spectrum",
        ["Fruity Wave Candy"] = "oscilloscope",
        ["Fruity Free Filter"] = "filter",
        ["Fruity Bass Boost"] = "eq",
        ["Fruity Center"] = "utility",
        ["Fruity Mute 2"] = "utility",
        ["Hyper Chorus"] = "chorus_plus",
        ["Frequency Shifter"] = "freq_shifter",
        ["Gate"] = "gate",
        ["Chorus"] = "chorus",
        ["Flanger"] = "flanger",
        ["Phaser"] = "phaser",
        ["Distortion"] = "distortion",
    };

    private static readonly Dictionary<string, string> Ableton = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Eq8"] = "eq_plus",
        ["Eq3"] = "eq_dj",
        ["Compressor2"] = "compressor",
        ["GlueCompressor"] = "compressor_plus",
        ["Limiter"] = "limiter",
        ["Gate"] = "gate",
        ["AutoFilter"] = "filter",
        ["Reverb"] = "reverb",
        ["Delay"] = "delay",
        ["Echo"] = "delay_plus",
        ["Chorus"] = "chorus",
        ["Chorus2"] = "chorus_plus",
        ["Flanger"] = "flanger",
        ["Phaser"] = "phaser",
        ["PhaserNew"] = "phaser_plus",
        ["Overdrive"] = "over",
        ["Saturator"] = "saturator",
        ["Redux"] = "bitcrusher",
        ["Utility"] = "utility",
        ["Tuner"] = "tuner",
        ["Spectrum"] = "spectrum",
        ["AutoPan"] = "tremolo",
        ["VinylDistortion"] = "distortion",
        ["SimpleDelay"] = "delay",
        ["PingPongDelay"] = "delay2",
        ["FilterDelay"] = "delay_plus",
        ["MultibandDynamics"] = "multiband_comp",
        ["Corpus"] = "resonator_bank",
        ["Amp"] = "amp",
        ["Cabinet"] = "amp",
        ["Pedal"] = "over",
        ["Erosion"] = "bitcrusher",
        ["DynamicTube"] = "saturator",
        ["Vocoder"] = "vocoder",
    };

    private static readonly Dictionary<string, string> DawProject = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Equalizer"] = "eq",
        ["compressor"] = "compressor",
        ["noiseGate"] = "gate",
        ["limiter"] = "limiter",
        ["EQ"] = "eq",
        ["Compressor"] = "compressor",
        ["Gate"] = "gate",
        ["Limiter"] = "limiter",
    };

    private static readonly Dictionary<string, string> Bitwig = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EQ-5"] = "eq5",
        ["EQ-2"] = "eq2",
        ["EQ-DJ"] = "eq_dj",
        ["Filter"] = "filter",
        ["Compressor"] = "compressor",
        ["Dynamics"] = "dynamics",
        ["Gate"] = "gate",
        ["Peak Limiter"] = "peak_limiter",
        ["Delay-1"] = "delay1",
        ["Delay-2"] = "delay2",
        ["Delay-4"] = "delay4",
        ["Reverb"] = "reverb",
        ["Chorus"] = "chorus",
        ["Flanger"] = "flanger",
        ["Phaser"] = "phaser",
        ["Distortion"] = "distortion",
        ["Bit-8"] = "bitcrusher",
        ["Tool"] = "tool",
        ["Utility"] = "utility",
    };

    public static bool TryMap(string sourceFormat, string deviceName, out string typeId)
    {
        typeId = "";
        if (string.IsNullOrWhiteSpace(deviceName)) return false;

        var table = sourceFormat.ToLowerInvariant() switch
        {
            "flp" or "flstudio" => FlStudio,
            "als" or "ableton" => Ableton,
            "dawproject" => DawProject,
            "bwproject" or "bitwig" => Bitwig,
            _ => null
        };

        if (table is null) return false;

        if (table.TryGetValue(deviceName.Trim(), out typeId!))
            return true;

        // Loose contains match for Fruity / Live naming variants.
        foreach (var (key, value) in table)
        {
            if (deviceName.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
            {
                typeId = value;
                return true;
            }
        }

        return false;
    }
}
