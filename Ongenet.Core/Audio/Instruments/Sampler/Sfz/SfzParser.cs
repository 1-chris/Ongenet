using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sfz;

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
        var curves = new SamplerCurveBank();

        // Active inheritance scopes. Opcodes before any header land in the global scope.
        var global = NewScope();
        var master = NewScope();
        var group = NewScope();
        var control = NewScope();
        Dictionary<string, string>? region = null;
        Dictionary<string, string>? curveScope = null;
        var discard = NewScope();

        var current = global;
        var groupIndex = -1;
        var nextGroupIndex = 0;
        var nextCurveId = 0;
        // Snapshot of control default_path for each region (later <control> blocks rewrite it).
        var activeDefaultPath = string.Empty;

        void FinalizeRegion()
        {
            if (region is null) return;
            regions.Add(new SfzRegion
            {
                Index = regions.Count,
                GroupIndex = groupIndex,
                DefaultPath = activeDefaultPath,
                Opcodes = new SfzOpcodes(Flatten(global, master, group, region))
            });
            region = null;
        }

        void FinalizeCurve()
        {
            if (curveScope is null) return;
            var id = 0;
            if (curveScope.TryGetValue("curve_index", out var idText)
                && int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                id = parsed;
            else
                id = nextCurveId++;

            var values = SamplerCurve.CreateLinear();
            foreach (var kv in curveScope)
            {
                if (kv.Key.Length < 2 || kv.Key[0] != 'v') continue;
                if (!int.TryParse(kv.Key.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                    continue;
                if (idx is < 0 or > 127) continue;
                if (float.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    values[idx] = v;
            }
            curves.Set(new SamplerCurve { Id = id, Values = values });
            curveScope = null;
        }

        foreach (var token in tokens)
        {
            if (token.Kind == SfzTokenKind.Header)
            {
                switch (token.Name)
                {
                    case "global":
                        FinalizeRegion();
                        FinalizeCurve();
                        global = NewScope(); master = NewScope(); group = NewScope();
                        groupIndex = -1;
                        current = global;
                        break;
                    case "master":
                        FinalizeRegion();
                        FinalizeCurve();
                        master = NewScope(); group = NewScope();
                        groupIndex = -1;
                        current = master;
                        break;
                    case "group":
                        FinalizeRegion();
                        FinalizeCurve();
                        group = NewScope();
                        groupIndex = nextGroupIndex++;
                        current = group;
                        break;
                    case "region":
                        FinalizeRegion();
                        FinalizeCurve();
                        region = NewScope();
                        current = region;
                        break;
                    case "control":
                        FinalizeRegion();
                        FinalizeCurve();
                        current = control;
                        break;
                    case "curve":
                        FinalizeRegion();
                        FinalizeCurve();
                        curveScope = NewScope();
                        current = curveScope;
                        break;
                    default:
                        // effect/midi/sample and any unknown header: collected but not used.
                        FinalizeRegion();
                        FinalizeCurve();
                        if (token.Name is "effect" or "midi" or "sample")
                            warnings.Add($"SFZ <{token.Name}> header is not applied.");
                        current = discard = NewScope();
                        break;
                }

                continue;
            }

            // Opcode: assign into the current scope (later assignments overwrite earlier ones).
            current[token.Name] = token.Value;
            if (ReferenceEquals(current, control) && token.Name == "default_path")
                activeDefaultPath = NormalizeSlashes(token.Value);
        }

        FinalizeRegion();
        FinalizeCurve();

        return new SfzDocument
        {
            Control = BuildControl(control),
            Regions = regions,
            Curves = curves,
            Warnings = warnings
        };
    }

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
