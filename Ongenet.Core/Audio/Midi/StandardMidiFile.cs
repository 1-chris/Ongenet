using System;
using System.Collections.Generic;
using System.IO;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Midi;

/// <summary>The result of reading a Standard MIDI File: notes in clip-relative beats plus metadata.</summary>
public sealed class MidiClipData
{
    /// <summary>The notes, merged from every track/channel, positions in beats.</summary>
    public List<MidiNote> Notes { get; } = new();

    /// <summary>Length spanned by the notes, in beats (the last note's end).</summary>
    public double LengthBeats { get; set; }

    /// <summary>The first tempo found in the file (BPM), or null if none.</summary>
    public double? TempoBpm { get; set; }
}

/// <summary>
/// A small, dependency-free Standard MIDI File (SMF) reader/writer. Reads format 0 and 1 files,
/// pairing note-on/off events across all tracks and converting ticks to beats via the header's
/// PPQ division (tempo-independent, matching how the app stores positions). Writes a format-0 file
/// with a tempo and time-signature meta event.
/// </summary>
public static class StandardMidiFile
{
    private const int DefaultPpq = 480;
    private const double MinNoteBeats = 1.0 / 64.0;

    // --- Reading ---

    public static MidiClipData Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        var pos = 0;

        var (id, headerLen) = ReadChunkHeader(data, ref pos);
        if (id != "MThd") throw new InvalidDataException("Not a Standard MIDI File (missing MThd header).");

        var headerEnd = pos + headerLen;
        ReadUInt16(data, ref pos);                  // format (0/1/2) — we merge tracks regardless
        var trackCount = ReadUInt16(data, ref pos);
        var division = ReadUInt16(data, ref pos);
        pos = headerEnd;                            // skip any extra header bytes

        // PPQ when the top bit is clear; SMPTE division is rare and can't be mapped to beats
        // without per-frame timing, so fall back to a sane default in that case.
        var ppq = (division & 0x8000) == 0 && division > 0 ? division : DefaultPpq;

        var result = new MidiClipData();
        var maxEndTicks = 0L;

        for (var t = 0; t < trackCount && pos < data.Length; t++)
        {
            var (trackId, trackLen) = ReadChunkHeader(data, ref pos);
            var trackEnd = pos + trackLen;
            if (trackId != "MTrk") { pos = trackEnd; continue; }   // skip unknown chunks

            ParseTrack(data, pos, trackEnd, result, ref maxEndTicks, ppq);
            pos = trackEnd;
        }

        result.LengthBeats = maxEndTicks / (double)ppq;
        return result;
    }

    private static void ParseTrack(byte[] data, int pos, int end, MidiClipData result,
        ref long maxEndTicks, int ppq)
    {
        long absTicks = 0;
        byte runningStatus = 0;
        // Pending note-ons keyed by (channel, note); a queue handles repeated same-pitch notes.
        var pending = new Dictionary<int, Queue<(long tick, int vel)>>();

        while (pos < end)
        {
            absTicks += ReadVlq(data, ref pos);
            if (pos >= end) break;

            var status = data[pos];
            if (status < 0x80)
            {
                status = runningStatus;             // running status: reuse the previous status byte
            }
            else
            {
                pos++;
            }

            var high = status & 0xF0;
            if (high is >= 0x80 and <= 0xE0)
            {
                runningStatus = status;
                var channel = status & 0x0F;

                switch (high)
                {
                    case 0x80: // note off
                    {
                        var note = data[pos++];
                        pos++;                       // release velocity (unused)
                        CloseNote(pending, channel, note, absTicks, result, ref maxEndTicks, ppq);
                        break;
                    }
                    case 0x90: // note on (velocity 0 == note off)
                    {
                        var note = data[pos++];
                        var vel = data[pos++];
                        if (vel == 0)
                            CloseNote(pending, channel, note, absTicks, result, ref maxEndTicks, ppq);
                        else
                            OpenNote(pending, channel, note, absTicks, vel);
                        break;
                    }
                    case 0xA0: // poly aftertouch
                    case 0xB0: // control change
                    case 0xE0: // pitch bend
                        pos += 2;
                        break;
                    case 0xC0: // program change
                    case 0xD0: // channel pressure
                        pos += 1;
                        break;
                }
            }
            else if (status == 0xFF) // meta event
            {
                var type = data[pos++];
                var len = (int)ReadVlq(data, ref pos);
                if (type == 0x51 && len == 3) // set tempo (microseconds per quarter note)
                {
                    var usPerQuarter = (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];
                    if (usPerQuarter > 0) result.TempoBpm ??= 60_000_000.0 / usPerQuarter;
                }

                pos += len;
                runningStatus = 0;
            }
            else if (status is 0xF0 or 0xF7) // sysex
            {
                var len = (int)ReadVlq(data, ref pos);
                pos += len;
                runningStatus = 0;
            }
            else
            {
                break; // unrecognized — bail out of this track rather than misread the stream
            }
        }
    }

    private static void OpenNote(Dictionary<int, Queue<(long, int)>> pending,
        int channel, int note, long tick, int vel)
    {
        var key = (channel << 8) | note;
        if (!pending.TryGetValue(key, out var q)) pending[key] = q = new Queue<(long, int)>();
        q.Enqueue((tick, vel));
    }

    private static void CloseNote(Dictionary<int, Queue<(long, int)>> pending,
        int channel, int note, long endTick, MidiClipData result, ref long maxEndTicks, int ppq)
    {
        var key = (channel << 8) | note;
        if (!pending.TryGetValue(key, out var q) || q.Count == 0) return;

        var (startTick, vel) = q.Dequeue();
        var startBeat = startTick / (double)ppq;
        var lengthBeats = Math.Max(MinNoteBeats, (endTick - startTick) / (double)ppq);

        result.Notes.Add(new MidiNote
        {
            Note = note,
            StartBeat = startBeat,
            LengthBeats = lengthBeats,
            Velocity = Math.Clamp(vel / 127f, 0f, 1f)
        });

        if (endTick > maxEndTicks) maxEndTicks = endTick;
    }

    // --- Writing ---

    public static void Write(Stream stream, IReadOnlyList<MidiNote> notes, double lengthBeats,
        double tempoBpm, TimeSignature timeSignature, int ppq = DefaultPpq)
    {
        // Expand notes into a tick-sorted on/off event list. Offs sort before ons at the same tick
        // so zero-gap adjacent notes don't cancel each other.
        var events = new List<(long tick, bool isOn, int note, int vel)>();
        foreach (var n in notes)
        {
            var onTick = (long)Math.Round(n.StartBeat * ppq);
            var offTick = Math.Max(onTick + 1, (long)Math.Round(n.EndBeat * ppq));
            var note = Math.Clamp(n.Note, 0, 127);
            var vel = Math.Clamp((int)Math.Round(n.Velocity * 127f), 1, 127);
            events.Add((onTick, true, note, vel));
            events.Add((offTick, false, note, 0));
        }

        events.Sort((a, b) => a.tick != b.tick ? a.tick.CompareTo(b.tick) : a.isOn.CompareTo(b.isOn));

        var track = new List<byte>();

        // Tempo meta: FF 51 03 tttttt
        var usPerQuarter = (int)Math.Round(60_000_000.0 / Math.Max(1.0, tempoBpm));
        WriteVlq(track, 0);
        track.Add(0xFF); track.Add(0x51); track.Add(0x03);
        track.Add((byte)((usPerQuarter >> 16) & 0xFF));
        track.Add((byte)((usPerQuarter >> 8) & 0xFF));
        track.Add((byte)(usPerQuarter & 0xFF));

        // Time-signature meta: FF 58 04 nn dd cc bb
        WriteVlq(track, 0);
        track.Add(0xFF); track.Add(0x58); track.Add(0x04);
        track.Add((byte)Math.Clamp(timeSignature.Numerator, 1, 255));
        track.Add((byte)Log2Denominator(timeSignature.Denominator));
        track.Add(24);  // MIDI clocks per metronome click
        track.Add(8);   // 32nd notes per quarter

        long last = 0;
        foreach (var ev in events)
        {
            WriteVlq(track, ev.tick - last);
            last = ev.tick;
            track.Add((byte)(ev.isOn ? 0x90 : 0x80)); // channel 0, explicit status (no running status)
            track.Add((byte)ev.note);
            track.Add((byte)ev.vel);
        }

        // End of track: FF 2F 00
        WriteVlq(track, 0);
        track.Add(0xFF); track.Add(0x2F); track.Add(0x00);

        // Header chunk.
        WriteChunkId(stream, "MThd");
        WriteUInt32(stream, 6);
        WriteUInt16(stream, 0);                 // format 0
        WriteUInt16(stream, 1);                 // one track
        WriteUInt16(stream, (ushort)ppq);

        // Track chunk.
        WriteChunkId(stream, "MTrk");
        WriteUInt32(stream, (uint)track.Count);
        stream.Write(track.ToArray(), 0, track.Count);
    }

    private static int Log2Denominator(int denominator)
    {
        var d = denominator < 1 ? 4 : denominator;
        var log = 0;
        while (d > 1) { d >>= 1; log++; }
        return log;
    }

    // --- Primitive read/write helpers (MIDI is big-endian) ---

    private static (string id, int length) ReadChunkHeader(byte[] data, ref int pos)
    {
        var id = new string(new[] { (char)data[pos], (char)data[pos + 1], (char)data[pos + 2], (char)data[pos + 3] });
        pos += 4;
        var len = (int)ReadUInt32(data, ref pos);
        return (id, len);
    }

    private static ushort ReadUInt16(byte[] data, ref int pos)
    {
        var v = (ushort)((data[pos] << 8) | data[pos + 1]);
        pos += 2;
        return v;
    }

    private static uint ReadUInt32(byte[] data, ref int pos)
    {
        var v = (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
        pos += 4;
        return v;
    }

    private static long ReadVlq(byte[] data, ref int pos)
    {
        long value = 0;
        while (pos < data.Length)
        {
            var b = data[pos++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) break;
        }

        return value;
    }

    private static void WriteChunkId(Stream s, string id)
    {
        foreach (var c in id) s.WriteByte((byte)c);
    }

    private static void WriteUInt16(Stream s, ushort v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)(v & 0xFF));
    }

    private static void WriteUInt32(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)(v & 0xFF));
    }

    private static void WriteVlq(List<byte> buffer, long value)
    {
        if (value < 0) value = 0;
        Span<byte> tmp = stackalloc byte[5];
        var count = 0;
        tmp[count++] = (byte)(value & 0x7F);
        value >>= 7;
        while (value > 0)
        {
            tmp[count++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }

        for (var i = count - 1; i >= 0; i--) buffer.Add(tmp[i]);
    }
}
