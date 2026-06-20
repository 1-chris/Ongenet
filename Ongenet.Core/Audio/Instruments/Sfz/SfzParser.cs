using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// Parses SFZ text into an <see cref="SfzDocument"/>: preprocesses (comments/includes/defines),
/// tokenizes, then builds the <c>&lt;global&gt; → &lt;master&gt; → &lt;group&gt; → &lt;region&gt;</c>
/// inheritance tree, flattening each region's effective opcodes (nearer scopes override farther ones).
/// </summary>
public static class SfzParser
{
    public static SfzDocument Parse(string text, SfzParseOptions? options = null)
    {
        var warnings = new List<string>();
        var expanded = SfzPreprocessor.Expand(text ?? string.Empty, options, warnings);
        var tokens = SfzTokenizer.Tokenize(expanded);

        var regions = new List<SfzRegion>();

        // Active inheritance scopes. Opcodes before any header land in the global scope.
        var global = NewScope();
        var master = NewScope();
        var group = NewScope();
        var control = NewScope();
        Dictionary<string, string>? region = null;
        var discard = NewScope();

        var current = global;
        var groupIndex = -1;
        var nextGroupIndex = 0;

        void FinalizeRegion()
        {
            if (region is null) return;
            regions.Add(new SfzRegion
            {
                Index = regions.Count,
                GroupIndex = groupIndex,
                Opcodes = new SfzOpcodes(Flatten(global, master, group, region))
            });
            region = null;
        }

        foreach (var token in tokens)
        {
            if (token.Kind == SfzTokenKind.Header)
            {
                switch (token.Name)
                {
                    case "global":
                        FinalizeRegion();
                        global = NewScope(); master = NewScope(); group = NewScope();
                        groupIndex = -1;
                        current = global;
                        break;
                    case "master":
                        FinalizeRegion();
                        master = NewScope(); group = NewScope();
                        groupIndex = -1;
                        current = master;
                        break;
                    case "group":
                        FinalizeRegion();
                        group = NewScope();
                        groupIndex = nextGroupIndex++;
                        current = group;
                        break;
                    case "region":
                        FinalizeRegion();
                        region = NewScope();
                        current = region;
                        break;
                    case "control":
                        FinalizeRegion();
                        current = control;
                        break;
                    default:
                        // curve/effect/midi/sample and any unknown header: collected but not used yet.
                        FinalizeRegion();
                        current = discard = NewScope();
                        break;
                }

                continue;
            }

            // Opcode: assign into the current scope (later assignments overwrite earlier ones).
            current[token.Name] = token.Value;
        }

        FinalizeRegion();

        return new SfzDocument
        {
            Control = BuildControl(control),
            Regions = regions,
            Warnings = warnings
        };
    }

    // Builds a region's effective opcode set: global, then master, then group, then region — each
    // later scope overriding keys from the earlier ones.
    private static Dictionary<string, string> Flatten(
        Dictionary<string, string> global,
        Dictionary<string, string> master,
        Dictionary<string, string> group,
        Dictionary<string, string> region)
    {
        var result = new Dictionary<string, string>(global, StringComparer.Ordinal);
        foreach (var kv in master) result[kv.Key] = kv.Value;
        foreach (var kv in group) result[kv.Key] = kv.Value;
        foreach (var kv in region) result[kv.Key] = kv.Value;
        return result;
    }

    private static SfzControl BuildControl(Dictionary<string, string> control)
    {
        if (control.Count == 0) return SfzControl.Empty;

        var cc = new Dictionary<int, int>();
        foreach (var kv in control)
        {
            if (!kv.Key.StartsWith("set_cc", StringComparison.Ordinal)) continue;
            if (int.TryParse(kv.Key.AsSpan("set_cc".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)
                && int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
            {
                cc[num] = val;
            }
        }

        var ops = new SfzOpcodes(control);
        return new SfzControl
        {
            DefaultPath = NormalizeSlashes(ops.Get("default_path", string.Empty)),
            NoteOffset = ops.GetInt("note_offset", 0),
            OctaveOffset = ops.GetInt("octave_offset", 0),
            InitialCcValues = cc,
            Opcodes = ops
        };
    }

    private static string NormalizeSlashes(string path) => path.Replace('\\', '/');

    private static Dictionary<string, string> NewScope() => new(StringComparer.Ordinal);
}
