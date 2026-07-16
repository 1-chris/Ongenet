using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ongenet.Core.Persistence.Import.FlStudio;

internal readonly struct FlpEvent
{
    public FlpEvent(byte id, byte[] data)
    {
        Id = id;
        Data = data;
    }

    public byte Id { get; }
    public byte[] Data { get; }
}

/// <summary>From-scratch FLP TLV event reader (FLhd / FLdt).</summary>
internal static class FlpEventReader
{
    /// <summary>
    /// FL Studio 25/26 stores project event 172 with a 3-byte payload instead of a DWORD.
    /// Treating it as 4 bytes desyncs the entire event stream (tempo/channels/playlist lost).
    /// </summary>
    private const byte ThreeByteDwordEventId = 172;

    public sealed class Header
    {
        public short Format { get; init; }
        public ushort ChannelCount { get; init; }
        public ushort Ppq { get; init; }
    }

    public static (Header Header, IReadOnlyList<FlpEvent> Events) Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "FLhd")
            throw new InvalidDataException("Not an FL Studio project (missing FLhd).");

        var headerLen = reader.ReadInt32();
        if (headerLen != 6)
            throw new InvalidDataException($"Unexpected FLP header length {headerLen}.");

        var format = reader.ReadInt16();
        var channelCount = reader.ReadUInt16();
        var ppq = reader.ReadUInt16();
        if (ppq == 0) ppq = 96;

        // Skip chunks until FLdt.
        long eventsEnd;
        while (true)
        {
            if (reader.BaseStream.Position + 8 > reader.BaseStream.Length)
                throw new InvalidDataException("Missing FLdt chunk.");

            var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkLen = reader.ReadInt32();
            if (chunkLen < 0 || chunkLen > 0x10000000)
                throw new InvalidDataException($"Invalid FLP chunk length {chunkLen}.");

            if (chunkId == "FLdt")
            {
                eventsEnd = reader.BaseStream.Position + chunkLen;
                if (eventsEnd > reader.BaseStream.Length)
                    throw new InvalidDataException("FLdt chunk extends past end of file.");
                break;
            }

            reader.BaseStream.Position += chunkLen;
        }

        var events = new List<FlpEvent>();
        var versionMajor = 0;
        while (reader.BaseStream.Position < eventsEnd)
        {
            var id = reader.ReadByte();
            byte[] data;

            // FL 25/26+: event 172 uses a 3-byte payload (not a 4-byte DWORD).
            if (id == ThreeByteDwordEventId && versionMajor >= 25)
            {
                data = ReadExact(reader, 3, eventsEnd);
            }
            else if (id < 64)
            {
                data = ReadExact(reader, 1, eventsEnd);
            }
            else if (id < 128)
            {
                data = ReadExact(reader, 2, eventsEnd);
            }
            else if (id < 192)
            {
                data = ReadExact(reader, 4, eventsEnd);
            }
            else
            {
                var len = ReadVarLen(reader, eventsEnd);
                if (len < 0 || reader.BaseStream.Position + len > eventsEnd)
                    throw new InvalidDataException(
                        $"FLP varlen event {id} length {len} exceeds FLdt bounds at offset {reader.BaseStream.Position}.");
                data = ReadExact(reader, len, eventsEnd);
            }

            events.Add(new FlpEvent(id, data));

            if (id == FlpEventId.Version && versionMajor == 0)
            {
                var ver = ReadUtf8(data);
                var parts = ver.Split('.');
                if (parts.Length > 0 && int.TryParse(parts[0], out var maj))
                    versionMajor = maj;
            }
        }

        return (new Header { Format = format, ChannelCount = channelCount, Ppq = ppq }, events);
    }

    private static byte[] ReadExact(BinaryReader reader, int count, long eventsEnd)
    {
        if (count == 0) return Array.Empty<byte>();
        if (reader.BaseStream.Position + count > eventsEnd)
            throw new InvalidDataException(
                $"FLP truncated: need {count} bytes at offset {reader.BaseStream.Position}.");

        var data = reader.ReadBytes(count);
        if (data.Length != count)
            throw new InvalidDataException(
                $"FLP truncated: expected {count} bytes, got {data.Length} at offset {reader.BaseStream.Position - data.Length}.");
        return data;
    }

    private static int ReadVarLen(BinaryReader reader, long eventsEnd)
    {
        if (reader.BaseStream.Position >= eventsEnd)
            throw new InvalidDataException("FLP truncated while reading varlen length.");

        var b = reader.ReadByte();
        var len = b & 0x7F;
        var shift = 0;
        while ((b & 0x80) != 0)
        {
            if (reader.BaseStream.Position >= eventsEnd)
                throw new InvalidDataException("FLP truncated while reading varlen length.");
            b = reader.ReadByte();
            shift += 7;
            if (shift > 35)
                throw new InvalidDataException("FLP varlen length overflow.");
            len |= (b & 0x7F) << shift;
        }
        return len;
    }

    public static string ReadUnicode(byte[] data)
    {
        if (data.Length == 0) return "";
        var s = Encoding.Unicode.GetString(data);
        return s.TrimEnd('\0');
    }

    public static string ReadUtf8(byte[] data)
    {
        if (data.Length == 0) return "";
        var s = Encoding.UTF8.GetString(data);
        return s.TrimEnd('\0');
    }

    public static ushort ReadU16(byte[] data) =>
        data.Length >= 2 ? BitConverter.ToUInt16(data, 0) : (ushort)0;

    public static uint ReadU32(byte[] data) =>
        data.Length >= 4 ? BitConverter.ToUInt32(data, 0) : 0u;

    public static int ReadI32(byte[] data) =>
        data.Length >= 4 ? BitConverter.ToInt32(data, 0) : 0;
}
