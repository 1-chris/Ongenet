using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sf2;

/// <summary>
/// Hand-rolled SoundFont 2 (RIFF) reader — no external dependencies, matching this repo's other binary
/// parsers (WAV, Standard MIDI File). Parses the <c>sfbk</c> form's <c>INFO</c>, <c>sdta</c> (sample pool)
/// and <c>pdta</c> (preset/instrument/sample hydra) lists into an <see cref="Sf2File"/>. Throws
/// <see cref="InvalidDataException"/> on a malformed file.
/// </summary>
public static class Sf2Reader
{
    public static Sf2File Parse(string path) => Parse(File.ReadAllBytes(path));

    public static Sf2File Parse(byte[] data)
    {
        if (data.Length < 12 || Id(data, 0) != "RIFF" || Id(data, 8) != "sfbk")
            throw new InvalidDataException("Not a SoundFont (RIFF/sfbk) file.");

        var riffSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
        var end = Math.Min(data.Length, 8 + riffSize);

        string name = "SoundFont";
        long smplOffset = -1, smplBytes = 0, sm24Offset = -1;
        Sf2PresetHeader[]? phdr = null;
        Sf2Bag[]? pbag = null;
        Sf2GenItem[]? pgen = null;
        Sf2InstHeader[]? inst = null;
        Sf2Bag[]? ibag = null;
        Sf2GenItem[]? igen = null;
        Sf2SampleHeader[]? shdr = null;

        var pos = 12;
        while (pos + 8 <= end)
        {
            var id = Id(data, pos);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4, 4));
            var body = pos + 8;
            if (id == "LIST" && body + 4 <= end)
            {
                var listType = Id(data, body);
                var listStart = body + 4;
                var listEnd = Math.Min(end, body + size);
                switch (listType)
                {
                    case "INFO":
                        WalkSubChunks(data, listStart, listEnd, (sid, bp, bs) =>
                        {
                            if (sid == "INAM") name = FixedString(data, bp, bs);
                        });
                        break;
                    case "sdta":
                        WalkSubChunks(data, listStart, listEnd, (sid, bp, bs) =>
                        {
                            if (sid == "smpl") { smplOffset = bp; smplBytes = bs; }
                            else if (sid == "sm24") sm24Offset = bp;
                        });
                        break;
                    case "pdta":
                        WalkSubChunks(data, listStart, listEnd, (sid, bp, bs) =>
                        {
                            switch (sid)
                            {
                                case "phdr": phdr = ReadPresetHeaders(data, bp, bs); break;
                                case "pbag": pbag = ReadBags(data, bp, bs); break;
                                case "pgen": pgen = ReadGens(data, bp, bs); break;
                                case "inst": inst = ReadInstHeaders(data, bp, bs); break;
                                case "ibag": ibag = ReadBags(data, bp, bs); break;
                                case "igen": igen = ReadGens(data, bp, bs); break;
                                case "shdr": shdr = ReadSampleHeaders(data, bp, bs); break;
                                // pmod / imod modulators are not used by this engine; skipped.
                            }
                        });
                        break;
                }
            }

            pos = body + size + (size & 1); // chunks are word-aligned
        }

        if (phdr is null || pbag is null || pgen is null || inst is null || ibag is null || igen is null || shdr is null)
            throw new InvalidDataException("SoundFont is missing required pdta chunks.");
        if (smplOffset < 0)
            throw new InvalidDataException("SoundFont has no sample data (smpl).");

        return new Sf2File
        {
            Name = name,
            Presets = phdr,
            PresetBags = pbag,
            PresetGens = pgen,
            Instruments = inst,
            InstBags = ibag,
            InstGens = igen,
            SampleHeaders = shdr,
            Data = data,
            SmplOffset = smplOffset,
            SmplFrames = smplBytes / 2,
            Sm24Offset = sm24Offset
        };
    }

    private static void WalkSubChunks(byte[] data, int start, int end, Action<string, int, int> handle)
    {
        var p = start;
        while (p + 8 <= end)
        {
            var id = Id(data, p);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 4, 4));
            var body = p + 8;
            if (body + size > end) size = end - body; // tolerate a truncated final chunk
            handle(id, body, size);
            p = body + size + (size & 1);
        }
    }

    private static Sf2PresetHeader[] ReadPresetHeaders(byte[] d, int p, int size)
    {
        var n = size / 38;
        var arr = new Sf2PresetHeader[n];
        for (var i = 0; i < n; i++)
        {
            var o = p + i * 38;
            arr[i] = new Sf2PresetHeader(
                FixedString(d, o, 20),
                U16(d, o + 20),
                U16(d, o + 22),
                U16(d, o + 24));
        }

        return arr;
    }

    private static Sf2InstHeader[] ReadInstHeaders(byte[] d, int p, int size)
    {
        var n = size / 22;
        var arr = new Sf2InstHeader[n];
        for (var i = 0; i < n; i++)
        {
            var o = p + i * 22;
            arr[i] = new Sf2InstHeader(FixedString(d, o, 20), U16(d, o + 20));
        }

        return arr;
    }

    private static Sf2Bag[] ReadBags(byte[] d, int p, int size)
    {
        var n = size / 4;
        var arr = new Sf2Bag[n];
        for (var i = 0; i < n; i++)
        {
            var o = p + i * 4;
            arr[i] = new Sf2Bag(U16(d, o), U16(d, o + 2));
        }

        return arr;
    }

    private static Sf2GenItem[] ReadGens(byte[] d, int p, int size)
    {
        var n = size / 4;
        var arr = new Sf2GenItem[n];
        for (var i = 0; i < n; i++)
        {
            var o = p + i * 4;
            arr[i] = new Sf2GenItem((Sf2Gen)U16(d, o), (ushort)U16(d, o + 2));
        }

        return arr;
    }

    private static Sf2SampleHeader[] ReadSampleHeaders(byte[] d, int p, int size)
    {
        var n = size / 46;
        var arr = new Sf2SampleHeader[n];
        for (var i = 0; i < n; i++)
        {
            var o = p + i * 46;
            arr[i] = new Sf2SampleHeader(
                FixedString(d, o, 20),
                BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o + 20, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o + 24, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o + 28, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o + 32, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o + 36, 4)),
                d[o + 40],
                (sbyte)d[o + 41],
                U16(d, o + 42),
                U16(d, o + 44));
        }

        return arr;
    }

    private static int U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o, 2));

    private static string Id(byte[] d, int o) => Encoding.ASCII.GetString(d, o, 4);

    private static string FixedString(byte[] d, int o, int max)
    {
        var len = 0;
        while (len < max && o + len < d.Length && d[o + len] != 0) len++;
        return Encoding.ASCII.GetString(d, o, len).TrimEnd();
    }
}
