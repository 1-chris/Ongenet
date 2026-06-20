using System;

namespace Ongenet.Core.Audio.Midi;

/// <summary>
/// Stateful MIDI byte-stream parser implementing the running-status protocol. Backends that deliver
/// raw bytes (ALSA rawmidi, CoreMIDI packets) feed bytes in via <see cref="Push"/>; each completed
/// channel-voice message is handed to the callback as a <see cref="MidiMessage"/>.
///
/// Protocol rules handled:
/// <list type="bullet">
/// <item>System real-time bytes (0xF8..0xFF) may appear anywhere, even mid-message; they are dropped
///   without disturbing the in-progress message or running status.</item>
/// <item>System common (0xF0..0xF7) clears running status. 0xF0 begins a SysEx dump whose data bytes
///   are swallowed until 0xF7 (or any new status byte) ends it.</item>
/// <item>Channel-voice status (0x80..0xEF) sets running status; subsequent data bytes with no status
///   byte reuse it.</item>
/// <item>NoteOn with velocity 0 is normalized to NoteOff (the common running-status idiom).</item>
/// </list>
///
/// Not thread-safe: each backend owns one parser, driven from its single read thread.
/// </summary>
public sealed class MidiRunningStatusParser
{
    private byte _status;     // current running status byte (channel message), 0 when none
    private byte _data1;      // first data byte of a two-byte message awaiting its second
    private int _dataIndex;   // data bytes collected for the message in progress
    private int _dataNeeded;  // data bytes the current status requires (1 or 2)
    private bool _inSysEx;

    /// <summary>Clears all parser state (e.g. after a device reopen).</summary>
    public void Reset()
    {
        _status = 0;
        _data1 = 0;
        _dataIndex = 0;
        _dataNeeded = 0;
        _inSysEx = false;
    }

    /// <summary>Feeds a span of received bytes, invoking <paramref name="onMessage"/> per completed message.</summary>
    public void Push(ReadOnlySpan<byte> bytes, Action<MidiMessage> onMessage)
    {
        foreach (var b in bytes)
        {
            if (b >= 0xF8)
            {
                // System real-time: never disturbs the message in progress or running status.
                continue;
            }

            if (b >= 0x80)
            {
                // A status byte. System common (0xF0..0xF7) clears running status; 0xF0/0xF7 toggle SysEx.
                if (b >= 0xF0)
                {
                    if (b == 0xF0) _inSysEx = true;
                    else if (b == 0xF7) _inSysEx = false;
                    _status = 0;
                    _dataNeeded = 0;
                    _dataIndex = 0;
                    continue;
                }

                // Channel-voice/mode status. Begins a new message and aborts any SysEx in progress.
                _inSysEx = false;
                _status = b;
                _dataNeeded = DataBytesFor(b);
                _dataIndex = 0;
                continue;
            }

            // Data byte (0x00..0x7F).
            if (_inSysEx) continue;       // swallow SysEx payload
            if (_status == 0) continue;   // stray data with no running status

            if (_dataIndex == 0)
            {
                _data1 = b;
                if (_dataNeeded == 1)
                {
                    Emit(_status, _data1, 0, onMessage);
                    _dataIndex = 0; // ready for the next message under running status
                }
                else
                {
                    _dataIndex = 1;
                }
            }
            else
            {
                Emit(_status, _data1, b, onMessage);
                _dataIndex = 0;
            }
        }
    }

    /// <summary>Number of data bytes a channel-voice status byte (0x80..0xEF) carries.</summary>
    public static int DataBytesFor(byte status) => (status & 0xF0) switch
    {
        0xC0 => 1, // Program Change
        0xD0 => 1, // Channel Aftertouch
        _ => 2,
    };

    /// <summary>
    /// Decodes one channel-voice message from a status byte (0x80..0xEF) and its data bytes into a
    /// <see cref="MidiMessage"/>, applying the NoteOn-velocity-0 → NoteOff normalization. Shared with
    /// backends (e.g. winmm) that receive already-framed short messages and so bypass the byte parser.
    /// </summary>
    public static MidiMessage Decode(byte status, byte data1, byte data2)
    {
        var channel = (byte)(status & 0x0F);
        return (status & 0xF0) switch
        {
            0x80 => new MidiMessage(MidiMessageKind.NoteOff, channel, data1, data2),
            // NoteOn with velocity 0 is a NoteOff.
            0x90 => data2 == 0
                ? new MidiMessage(MidiMessageKind.NoteOff, channel, data1, 0)
                : new MidiMessage(MidiMessageKind.NoteOn, channel, data1, data2),
            0xA0 => new MidiMessage(MidiMessageKind.PolyAftertouch, channel, data1, data2),
            0xB0 => new MidiMessage(MidiMessageKind.ControlChange, channel, data1, data2),
            0xC0 => new MidiMessage(MidiMessageKind.ProgramChange, channel, data1, 0),
            0xD0 => new MidiMessage(MidiMessageKind.ChannelAftertouch, channel, data1, 0),
            _ => new MidiMessage(MidiMessageKind.PitchBend, channel, data1, data2), // 0xE0
        };
    }

    private static void Emit(byte status, byte d1, byte d2, Action<MidiMessage> onMessage)
        => onMessage(Decode(status, d1, d2));
}
