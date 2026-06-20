using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// The <c>&lt;control&gt;</c> section: instrument-wide settings that aren't per-region, chiefly the
/// sample <c>default_path</c> prefix, note/octave offsets, and initial MIDI CC values.
/// </summary>
public sealed class SfzControl
{
    /// <summary>Path prefix prepended to every region's <c>sample</c> opcode (forward-slash normalized).</summary>
    public string DefaultPath { get; init; } = string.Empty;

    /// <summary>Global semitone offset added to incoming keys (<c>note_offset</c>).</summary>
    public int NoteOffset { get; init; }

    /// <summary>Global octave offset added to incoming keys (<c>octave_offset</c>).</summary>
    public int OctaveOffset { get; init; }

    /// <summary>Initial values for MIDI CCs declared via <c>set_ccN=V</c> (CC number → 0..127 value).</summary>
    public IReadOnlyDictionary<int, int> InitialCcValues { get; init; } = new Dictionary<int, int>();

    /// <summary>All raw <c>&lt;control&gt;</c> opcodes, for anything not surfaced above.</summary>
    public SfzOpcodes Opcodes { get; init; } = new(new Dictionary<string, string>());

    public static SfzControl Empty { get; } = new();
}
