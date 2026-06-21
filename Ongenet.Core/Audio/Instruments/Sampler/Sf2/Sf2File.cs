using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sf2;

/// <summary>
/// A parsed SoundFont 2 file: the preset/instrument/sample "hydra" (the nine <c>pdta</c> chunks) plus a
/// reference to the raw 16-bit sample pool (<c>smpl</c>) and, when present, the 24-bit low-byte extension
/// (<c>sm24</c>). Produced by <see cref="Sf2Reader"/>. Holds data only; <see cref="Sf2Loader"/> turns a
/// chosen preset into playable regions.
/// </summary>
public sealed class Sf2File
{
    public required string Name { get; init; }

    // The hydra. Each header array's last entry is a terminal sentinel (EOP/EOI/EOS).
    public required IReadOnlyList<Sf2PresetHeader> Presets { get; init; }
    public required IReadOnlyList<Sf2Bag> PresetBags { get; init; }
    public required IReadOnlyList<Sf2GenItem> PresetGens { get; init; }
    public required IReadOnlyList<Sf2InstHeader> Instruments { get; init; }
    public required IReadOnlyList<Sf2Bag> InstBags { get; init; }
    public required IReadOnlyList<Sf2GenItem> InstGens { get; init; }
    public required IReadOnlyList<Sf2SampleHeader> SampleHeaders { get; init; }

    // The sample pool: 16-bit signed PCM in `Data` at `SmplOffset` (length `SmplFrames` points), with the
    // optional 24-bit low bytes at `Sm24Offset` (one byte per point) when `HasSm24` is true.
    public required byte[] Data { get; init; }
    public required long SmplOffset { get; init; }
    public required long SmplFrames { get; init; }
    public long Sm24Offset { get; init; } = -1;
    public bool HasSm24 => Sm24Offset >= 0;

    private IReadOnlyList<Sf2PresetRef>? _order;

    /// <summary>Selectable presets, sorted by bank then program (the terminal record is excluded).</summary>
    public IReadOnlyList<Sf2PresetRef> PresetOrder
    {
        get
        {
            if (_order is not null) return _order;
            var list = new List<Sf2PresetRef>();
            for (var i = 0; i < Presets.Count - 1; i++) // last is the EOP terminal
            {
                var p = Presets[i];
                list.Add(new Sf2PresetRef(i, p.Bank, p.Program, p.Name));
            }

            list.Sort((a, b) => a.Bank != b.Bank ? a.Bank.CompareTo(b.Bank) : a.Program.CompareTo(b.Program));
            return _order = list;
        }
    }

    /// <summary>Extracts one sample header's audio as mono float frames (its <c>Start..End</c> window),
    /// decoding 16- or 24-bit PCM. Returns an empty array for a degenerate (empty) sample.</summary>
    public float[] ReadMono(in Sf2SampleHeader h)
    {
        if (h.End <= h.Start) return Array.Empty<float>();
        var count = (int)Math.Min(h.End - h.Start, SmplFrames - h.Start);
        if (count <= 0) return Array.Empty<float>();

        var span = Data.AsSpan();
        var outp = new float[count];

        if (HasSm24)
        {
            const float scale24 = 1f / 8388608f; // 2^23
            for (var i = 0; i < count; i++)
            {
                var idx = h.Start + (uint)i;
                short hi = BinaryPrimitives.ReadInt16LittleEndian(span.Slice((int)(SmplOffset + idx * 2), 2));
                int lo = span[(int)(Sm24Offset + idx)];
                var v24 = (hi << 8) | lo;          // assemble 24-bit value (hi is signed, lo unsigned low byte)
                outp[i] = v24 * scale24;
            }
        }
        else
        {
            const float scale16 = 1f / 32768f;
            for (var i = 0; i < count; i++)
            {
                var idx = h.Start + (uint)i;
                short s = BinaryPrimitives.ReadInt16LittleEndian(span.Slice((int)(SmplOffset + idx * 2), 2));
                outp[i] = s * scale16;
            }
        }

        return outp;
    }
}
