namespace Ongenet.Core.Audio.Midi;

/// <summary>
/// A normalized, platform-agnostic channel-voice MIDI message. Backends produce these either by
/// feeding raw bytes through <see cref="MidiRunningStatusParser"/> (ALSA/CoreMIDI) or by translating
/// already-parsed short messages (winmm). A value type, so routing it across threads never allocates.
/// </summary>
public readonly record struct MidiMessage(MidiMessageKind Kind, byte Channel, byte Data1, byte Data2)
{
    /// <summary>Note number (0..127) for note and poly-aftertouch messages.</summary>
    public int Note => Data1;

    /// <summary>Velocity mapped to 0..1, as <see cref="Instruments.IInstrument.NoteOn"/> expects.</summary>
    public float Velocity => Data2 / 127f;

    /// <summary>Controller number (0..127) for <see cref="MidiMessageKind.ControlChange"/>.</summary>
    public int Controller => Data1;

    /// <summary>Controller value (0..127) for <see cref="MidiMessageKind.ControlChange"/>.</summary>
    public int Value => Data2;

    /// <summary>Pitch-bend as a 14-bit value (0..16383, centre 8192).</summary>
    public int PitchBend14 => (Data2 << 7) | Data1;

    /// <summary>Pressure (0..127) for channel/poly aftertouch.</summary>
    public int Pressure => Kind == MidiMessageKind.PolyAftertouch ? Data2 : Data1;
}
