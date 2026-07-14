using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sfz;

/// <summary>
/// Classifies SFZ opcodes as implemented, intentionally ignored (ARIA GUI / effects / synth),
/// or unknown. Used to emit load-time warnings without drowning the user in noise.
/// </summary>
public static class SfzOpcodeCatalog
{
    private static readonly HashSet<string> ImplementedExact = new(StringComparer.Ordinal)
    {
        "sample", "key", "lokey", "hikey", "lovel", "hivel", "pitch_keycenter", "pitch_keytrack",
        "transpose", "tune", "pitch", "volume", "amplitude", "pan", "amp_veltrack",
        "offset", "end", "loop_mode", "loopmode", "loop_start", "loopstart", "loop_end", "loopend",
        "direction", "bend_up", "bend_down", "bend_step", "bend_stepup", "bend_stepdown", "bend_smooth",
        "ampeg_delay", "ampeg_start", "ampeg_attack", "ampeg_hold", "ampeg_decay", "ampeg_sustain", "ampeg_release",
        "fileg_delay", "fileg_start", "fileg_attack", "fileg_hold", "fileg_decay", "fileg_sustain", "fileg_release",
        "pitcheg_delay", "pitcheg_start", "pitcheg_attack", "pitcheg_hold", "pitcheg_decay", "pitcheg_sustain", "pitcheg_release",
        "cutoff", "resonance", "fil_type", "fil_keytrack", "fil_keycenter", "fil_veltrack", "fileg_depth", "fil_random",
        "fillfo_freq", "fillfo_depth", "fillfo_delay", "fillfo_fade",
        "amplfo_freq", "amplfo_depth", "amplfo_delay", "amplfo_fade",
        "pitchlfo_freq", "pitchlfo_depth", "pitchlfo_delay", "pitchlfo_fade", "pitcheg_depth",
        "eq1_freq", "eq1_gain", "eq1_bw", "eq2_freq", "eq2_gain", "eq2_bw", "eq3_freq", "eq3_gain", "eq3_bw",
        "seq_length", "seq_position", "group", "off_by", "off_mode", "trigger",
        "sw_last", "sw_down", "sw_up", "sw_previous", "sw_vel", "sw_lokey", "sw_hikey", "sw_default",
        "lorand", "hirand", "lochan", "hichan", "lobend", "hibend",
        "lochanaft", "hichanaft", "lopolyaft", "hipolyaft", "lobpm", "hibpm",
        "delay", "delay_random", "delay_beats", "delay_samples", "count", "rt_decay", "rt_dead",
        "offset_random", "pitch_veltrack", "pitch_random", "amp_keytrack", "amp_keycenter", "amp_random",
        "width", "position", "phase", "polyphony", "note_polyphony", "note_selfmask",
        "loprog", "hiprog", "lotimer", "hitimer", "sustain_sw", "sostenuto_sw",
        "loop_count", "loop_crossfade", "loop_type", "sample_fadeout", "sync_beats", "sync_offset",
        "stop_beats", "default_path", "note_offset", "octave_offset", "output",
        "effect1", "effect2", "effect3", "effect4",
        "cutoff2", "resonance2", "fil2_type",
    };

    private static readonly HashSet<string> IgnoredExact = new(StringComparer.Ordinal)
    {
        "script", "load_mode", "load_start", "load_end", "sample_quality", "waveguide", "image", "md5",
        "global_volume", "master_volume", "group_volume", "global_amplitude", "master_amplitude", "group_amplitude",
        "noise_level", "noise_tone", "noise_stereo",
    };

    public enum Kind { Implemented, Ignored, Unknown }

    public static Kind Classify(string opcode)
    {
        if (string.IsNullOrEmpty(opcode)) return Kind.Unknown;
        if (ImplementedExact.Contains(opcode)) return Kind.Implemented;
        if (IgnoredExact.Contains(opcode)) return Kind.Ignored;

        // Prefix / patterned opcodes recognised as implemented.
        if (MatchesImplementedPattern(opcode)) return Kind.Implemented;

        // Intentionally ignored families.
        if (opcode.StartsWith("gui_", StringComparison.Ordinal)
            || opcode.StartsWith("label_", StringComparison.Ordinal)
            || opcode.StartsWith("hint_", StringComparison.Ordinal)
            || opcode.EndsWith("_label", StringComparison.Ordinal)
            || opcode.StartsWith("global_label", StringComparison.Ordinal)
            || opcode.StartsWith("oscillator", StringComparison.Ordinal)
            || opcode.StartsWith("reverb_", StringComparison.Ordinal)
            || opcode.StartsWith("comp_", StringComparison.Ordinal)
            || opcode.StartsWith("phaser_", StringComparison.Ordinal)
            || opcode.StartsWith("disto_", StringComparison.Ordinal)
            || opcode.StartsWith("tdfir_", StringComparison.Ordinal)
            || opcode.StartsWith("apan_", StringComparison.Ordinal)
            || opcode.StartsWith("gate_", StringComparison.Ordinal)
            || opcode.StartsWith("static_", StringComparison.Ordinal)
            || opcode.StartsWith("strings_", StringComparison.Ordinal)
            || opcode is "type" or "bus" or "dsp_order" or "vendor_specific" or "internal"
            || opcode.StartsWith("directtomain", StringComparison.Ordinal)
            || opcode.StartsWith("fx", StringComparison.Ordinal)
            || opcode.StartsWith("bypass_", StringComparison.Ordinal)
            || opcode.StartsWith("param_", StringComparison.Ordinal))
            return Kind.Ignored;

        return Kind.Unknown;
    }

    private static bool MatchesImplementedPattern(string op)
    {
        if (op.StartsWith("set_cc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("locc", StringComparison.Ordinal) || op.StartsWith("hicc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("on_locc", StringComparison.Ordinal) || op.StartsWith("on_hicc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("start_locc", StringComparison.Ordinal) || op.StartsWith("start_hicc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("stop_locc", StringComparison.Ordinal) || op.StartsWith("stop_hicc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("cutoff_cc", StringComparison.Ordinal) || op.StartsWith("cutoff_oncc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("resonance_cc", StringComparison.Ordinal) || op.StartsWith("resonance_oncc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("gain_cc", StringComparison.Ordinal) || op.StartsWith("volume_oncc", StringComparison.Ordinal)
            || op.StartsWith("volume_cc", StringComparison.Ordinal) || op.StartsWith("gain_oncc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("pan_oncc", StringComparison.Ordinal) || op.StartsWith("pan_cc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("pitch_oncc", StringComparison.Ordinal) || op.StartsWith("pitch_cc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("width_oncc", StringComparison.Ordinal) || op.StartsWith("delay_cc", StringComparison.Ordinal)
            || op.StartsWith("delay_oncc", StringComparison.Ordinal) || op.StartsWith("offset_cc", StringComparison.Ordinal)
            || op.StartsWith("offset_oncc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("xfin_", StringComparison.Ordinal) || op.StartsWith("xfout_", StringComparison.Ordinal)
            || op.StartsWith("xf_", StringComparison.Ordinal)) return true;
        if (op.StartsWith("amp_velcurve_", StringComparison.Ordinal)) return true;
        if (op.StartsWith("ampeg_vel2", StringComparison.Ordinal) || op.StartsWith("fileg_vel2", StringComparison.Ordinal)
            || op.StartsWith("pitcheg_vel2", StringComparison.Ordinal)) return true;
        if (ContainsCcSuffix(op, "ampeg_") || ContainsCcSuffix(op, "fileg_") || ContainsCcSuffix(op, "pitcheg_")) return true;
        if (op.Contains("depthcc", StringComparison.Ordinal) || op.Contains("freqcc", StringComparison.Ordinal)
            || op.Contains("depthchanaft", StringComparison.Ordinal) || op.Contains("depthpolyaft", StringComparison.Ordinal)
            || op.Contains("freqchanaft", StringComparison.Ordinal) || op.Contains("freqpolyaft", StringComparison.Ordinal)
            || op.Contains("curvecc", StringComparison.Ordinal) || op.Contains("smoothcc", StringComparison.Ordinal)
            || op.Contains("stepcc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("eq", StringComparison.Ordinal) && (op.Contains("cc", StringComparison.Ordinal)
            || op.Contains("vel2", StringComparison.Ordinal))) return true;
        if (op.StartsWith("cutoff_chanaft", StringComparison.Ordinal) || op.StartsWith("cutoff_polyaft", StringComparison.Ordinal)) return true;
        if (op.StartsWith("loop_startcc", StringComparison.Ordinal) || op.StartsWith("loop_lengthcc", StringComparison.Ordinal)
            || op.StartsWith("loop_start_oncc", StringComparison.Ordinal) || op.StartsWith("loop_length_oncc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("reverse_locc", StringComparison.Ordinal) || op.StartsWith("reverse_hicc", StringComparison.Ordinal)) return true;
        if (op.StartsWith("delay_samples", StringComparison.Ordinal)) return true;
        if (op.StartsWith("eg", StringComparison.Ordinal) && op.Length > 2 && char.IsDigit(op[2])) return true;
        if (op.StartsWith("lfo", StringComparison.Ordinal) && op.Length > 3 && char.IsDigit(op[3])) return true;
        if (op.StartsWith("v", StringComparison.Ordinal) && op.Length >= 2 && char.IsDigit(op[1])) return true; // curve points
        return false;
    }

    private static bool ContainsCcSuffix(string op, string prefix)
    {
        if (!op.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return op.Contains("cc", StringComparison.Ordinal);
    }

    /// <summary>Appends de-duplicated ignore/unknown warnings for all opcodes on a region set.</summary>
    public static void CollectWarnings(IEnumerable<IReadOnlyDictionary<string, string>> opcodeMaps, List<string> warnings)
    {
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var map in opcodeMaps)
        {
            foreach (var key in map.Keys)
            {
                switch (Classify(key))
                {
                    case Kind.Ignored: ignored.Add(key); break;
                    case Kind.Unknown: unknown.Add(key); break;
                }
            }
        }

        if (ignored.Count > 0)
            warnings.Add($"Ignoring unsupported SFZ opcodes: {string.Join(", ", ignored)}");
        if (unknown.Count > 0)
            warnings.Add($"Unknown SFZ opcodes (not applied): {string.Join(", ", unknown)}");
    }
}
