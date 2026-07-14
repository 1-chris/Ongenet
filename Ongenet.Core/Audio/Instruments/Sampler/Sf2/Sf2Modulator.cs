using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sf2;

/// <summary>One SF2 <c>sfModList</c> record (10 bytes).</summary>
public readonly record struct Sf2ModItem(
    ushort SrcOper,
    Sf2Gen DestOper,
    short Amount,
    ushort AmtSrcOper,
    ushort TransOper);

/// <summary>SF2 modulator source primary (low 7 bits of SrcOper) and helpers.</summary>
public static class Sf2ModSources
{
    public const int None = 0;
    public const int NoteOnVelocity = 2;
    public const int NoteOnKeyNumber = 3;
    public const int PolyPressure = 10;
    public const int ChannelPressure = 13;
    public const int PitchWheel = 14;
    public const int PitchWheelSensitivity = 16;
    public const int Link = 127;

    public static bool IsCc(ushort srcOper) => (srcOper & 0x80) != 0;
    public static int CcIndex(ushort srcOper) => srcOper & 0x7F;
    public static int Primary(ushort srcOper) => srcOper & 0x7F;
    public static int Transform(ushort t) => t & 0xFF;
}

/// <summary>Default modulators from the SF2 specification plus application onto regions.</summary>
public static class Sf2Modulator
{
    /// <summary>Default note-on velocity → initial attenuation (concave, negative).</summary>
    public static readonly Sf2ModItem DefaultVelToAtten = new(
        Sf2ModSources.NoteOnVelocity, Sf2Gen.InitialAttenuation, 960, 0, 1 /* concave */);

    /// <summary>Default CC1 → vib LFO → pitch.</summary>
    public static readonly Sf2ModItem DefaultCc1ToVibPitch = new(
        (ushort)(0x80 | 1), Sf2Gen.VibLfoToPitch, 50, 0, 0);

    public static IReadOnlyList<Sf2ModItem> ReadMods(byte[] data, int offset, int size)
    {
        const int stride = 10;
        var count = size / stride;
        if (count <= 0) return Array.Empty<Sf2ModItem>();
        var list = new Sf2ModItem[count];
        for (var i = 0; i < count; i++)
        {
            var p = offset + i * stride;
            list[i] = new Sf2ModItem(
                BitConverter.ToUInt16(data, p),
                (Sf2Gen)BitConverter.ToUInt16(data, p + 2),
                BitConverter.ToInt16(data, p + 4),
                BitConverter.ToUInt16(data, p + 6),
                BitConverter.ToUInt16(data, p + 8));
        }
        return list;
    }

    public static List<Sf2ModItem> ZoneMods(IReadOnlyList<Sf2Bag> bags, IReadOnlyList<Sf2ModItem> mods, int zone)
    {
        var start = bags[zone].ModNdx;
        var end = zone + 1 < bags.Count ? bags[zone + 1].ModNdx : mods.Count;
        var list = new List<Sf2ModItem>();
        for (var m = start; m < end && m < mods.Count; m++) list.Add(mods[m]);
        return list;
    }

    /// <summary>
    /// Converts modulator list into continuous <see cref="SamplerModRoute"/>s and updates AmpVeltrack
    /// for the common velocity→attenuation case.
    /// </summary>
    public static (double AmpVeltrack, List<SamplerModRoute> Routes) ApplyToRoutes(
        IEnumerable<Sf2ModItem> mods, double currentAmpVeltrack)
    {
        var routes = new List<SamplerModRoute>();
        var ampVel = currentAmpVeltrack;
        foreach (var m in mods)
        {
            if (m.Amount == 0) continue;
            var dest = MapDest(m.DestOper);
            if (dest is null) continue;

            // Velocity → attenuation default style: bake into AmpVeltrack.
            if (Sf2ModSources.Primary(m.SrcOper) == Sf2ModSources.NoteOnVelocity
                && m.DestOper == Sf2Gen.InitialAttenuation
                && !Sf2ModSources.IsCc(m.SrcOper))
            {
                // 960 centibels full-scale ≈ SF2 default; map to 0..100 veltrack.
                ampVel = Math.Clamp(Math.Abs(m.Amount) / 9.6, 0, 100);
                continue;
            }

            var source = MapSource(m.SrcOper, out var srcIndex);
            if (source is null) continue;

            var depth = ScaleAmount(m.DestOper, m.Amount);
            routes.Add(new SamplerModRoute(dest.Value, source.Value, srcIndex, depth));
        }
        return (ampVel, routes);
    }

    private static SamplerModTarget? MapDest(Sf2Gen g) => g switch
    {
        Sf2Gen.InitialAttenuation => SamplerModTarget.AmplitudeDb,
        Sf2Gen.Pan => SamplerModTarget.Pan,
        Sf2Gen.InitialFilterFc => SamplerModTarget.CutoffCents,
        Sf2Gen.InitialFilterQ => SamplerModTarget.ResonanceDb,
        Sf2Gen.VibLfoToPitch or Sf2Gen.ModLfoToPitch or Sf2Gen.ModEnvToPitch
            or Sf2Gen.CoarseTune or Sf2Gen.FineTune => SamplerModTarget.PitchCents,
        Sf2Gen.ModLfoToFilterFc or Sf2Gen.ModEnvToFilterFc => SamplerModTarget.CutoffCents,
        Sf2Gen.ModLfoToVolume => SamplerModTarget.AmplitudeDb,
        _ => null
    };

    private static SamplerModSource? MapSource(ushort src, out int index)
    {
        index = 0;
        if (Sf2ModSources.IsCc(src))
        {
            index = Sf2ModSources.CcIndex(src);
            return SamplerModSource.Cc;
        }
        return Sf2ModSources.Primary(src) switch
        {
            Sf2ModSources.NoteOnVelocity => SamplerModSource.Velocity,
            Sf2ModSources.NoteOnKeyNumber => SamplerModSource.Key,
            Sf2ModSources.ChannelPressure => SamplerModSource.ChannelAftertouch,
            Sf2ModSources.PolyPressure => SamplerModSource.PolyAftertouch,
            Sf2ModSources.PitchWheel => SamplerModSource.PitchBend,
            _ => null
        };
    }

    private static double ScaleAmount(Sf2Gen dest, short amount) => dest switch
    {
        Sf2Gen.InitialAttenuation => -amount / 10.0, // cB → dB (negative = quieter)
        Sf2Gen.Pan => amount / 500.0,
        Sf2Gen.InitialFilterQ => amount / 10.0,
        Sf2Gen.ModLfoToVolume => amount / 10.0,
        _ => amount // cents for pitch/filter
    };

    public static double Concave(double x)
    {
        // SF2 concave transform approximation for normalized 0..1
        x = Math.Clamp(x, 0, 1);
        if (x <= 0) return 0;
        return -20.0 / 96.0 * Math.Log10(x) ; // maps roughly like SF2
    }
}
