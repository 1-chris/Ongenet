using System.Collections.Generic;
using System.Globalization;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sfz;

/// <summary>
/// A read-only view over a flattened opcode dictionary with typed accessors. Unknown opcodes are
/// preserved in <see cref="Raw"/> (the engine interprets what it understands and ignores the rest),
/// which is what lets the parser accept the full SFZ specification without enumerating every opcode.
/// </summary>
public readonly struct SfzOpcodes
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public SfzOpcodes(IReadOnlyDictionary<string, string> values) => _values = values;

    /// <summary>The underlying opcode map (keys are lower-cased opcode names).</summary>
    public IReadOnlyDictionary<string, string> Raw => _values;

    public bool Has(string opcode) => _values.ContainsKey(opcode);

    public string? Get(string opcode) => _values.TryGetValue(opcode, out var v) ? v : null;

    public string Get(string opcode, string fallback) => _values.TryGetValue(opcode, out var v) ? v : fallback;

    public int GetInt(string opcode, int fallback)
        => _values.TryGetValue(opcode, out var v)
           && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n : fallback;

    public float GetFloat(string opcode, float fallback)
        => _values.TryGetValue(opcode, out var v)
           && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? f : fallback;

    public double GetDouble(string opcode, double fallback)
        => _values.TryGetValue(opcode, out var v)
           && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : fallback;

    /// <summary>Reads a key opcode (note name or number); falls back to <paramref name="fallback"/>.</summary>
    public int GetKey(string opcode, int fallback) => SfzNote.Parse(Get(opcode), fallback);

    /// <summary>
    /// Reads the first present opcode among <paramref name="opcodes"/> as a key, else
    /// <paramref name="fallback"/>. Used for the <c>key</c> shorthand (sets lokey/hikey/pitch_keycenter).
    /// </summary>
    public int GetKeyAny(int fallback, params string[] opcodes)
    {
        foreach (var op in opcodes)
        {
            var parsed = SfzNote.Parse(Get(op));
            if (parsed is { } v) return v;
        }

        return fallback;
    }
}
