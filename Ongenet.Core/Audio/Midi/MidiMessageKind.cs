namespace Ongenet.Core.Audio.Midi;

/// <summary>
/// The channel-voice MIDI messages Ongenet acts on. System real-time/common and SysEx bytes are
/// consumed by the parser but not surfaced as messages (they carry no routing meaning for v1).
/// </summary>
public enum MidiMessageKind : byte
{
    NoteOff,
    NoteOn,
    PolyAftertouch,
    ControlChange,
    ProgramChange,
    ChannelAftertouch,
    PitchBend,
}
