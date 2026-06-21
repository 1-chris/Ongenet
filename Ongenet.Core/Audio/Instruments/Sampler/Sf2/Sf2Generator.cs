using System;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sf2;

/// <summary>
/// The SoundFont 2 generator operators (SF2 spec §8.1.2). A generator is the SF2 equivalent of an SFZ
/// opcode: a named parameter that shapes a zone. Only the operators the engine understands are named; any
/// others are read and ignored.
/// </summary>
public enum Sf2Gen : ushort
{
    StartAddrsOffset = 0,
    EndAddrsOffset = 1,
    StartloopAddrsOffset = 2,
    EndloopAddrsOffset = 3,
    StartAddrsCoarseOffset = 4,
    ModLfoToPitch = 5,
    VibLfoToPitch = 6,
    ModEnvToPitch = 7,
    InitialFilterFc = 8,
    InitialFilterQ = 9,
    ModLfoToFilterFc = 10,
    ModEnvToFilterFc = 11,
    EndAddrsCoarseOffset = 12,
    ModLfoToVolume = 13,
    ChorusEffectsSend = 15,
    ReverbEffectsSend = 16,
    Pan = 17,
    DelayModLFO = 21,
    FreqModLFO = 22,
    DelayVibLFO = 23,
    FreqVibLFO = 24,
    DelayModEnv = 25,
    AttackModEnv = 26,
    HoldModEnv = 27,
    DecayModEnv = 28,
    SustainModEnv = 29,
    ReleaseModEnv = 30,
    KeynumToModEnvHold = 31,
    KeynumToModEnvDecay = 32,
    DelayVolEnv = 33,
    AttackVolEnv = 34,
    HoldVolEnv = 35,
    DecayVolEnv = 36,
    SustainVolEnv = 37,
    ReleaseVolEnv = 38,
    KeynumToVolEnvHold = 39,
    KeynumToVolEnvDecay = 40,
    Instrument = 41,
    KeyRange = 43,
    VelRange = 44,
    StartloopAddrsCoarseOffset = 45,
    Keynum = 46,
    Velocity = 47,
    InitialAttenuation = 48,
    EndloopAddrsCoarseOffset = 50,
    CoarseTune = 51,
    FineTune = 52,
    SampleID = 53,
    SampleModes = 54,
    ScaleTuning = 56,
    ExclusiveClass = 57,
    OverridingRootKey = 58,
    EndOper = 60
}

/// <summary>
/// One generator entry as stored in a pgen/igen chunk: the operator plus its 16-bit amount. The amount is
/// a union — read it as a signed short for scalar generators, or as a low/high byte pair for the range
/// generators (key/velocity range).
/// </summary>
public readonly record struct Sf2GenItem(Sf2Gen Oper, ushort Raw)
{
    public short Short => (short)Raw;
    public int Lo => Raw & 0xFF;
    public int Hi => (Raw >> 8) & 0xFF;
}

/// <summary>
/// SF2 generator helpers: per-generator defaults, level-classification (which generators are instrument-only
/// and therefore ignored at the preset level), and the unit conversions from SF2's encodings (timecents,
/// centibels, absolute cents) to the seconds / linear-gain / Hz the engine uses.
/// </summary>
public static class Sf2Convert
{
    /// <summary>The SF2-spec default amount for a generator when a zone doesn't specify it (§8.1.3).</summary>
    public static short Default(Sf2Gen g) => g switch
    {
        Sf2Gen.InitialFilterFc => 13500,                                   // ~no filter (~19.9 kHz)
        Sf2Gen.DelayModLFO or Sf2Gen.DelayVibLFO => -12000,               // ~1 ms (effectively none)
        Sf2Gen.DelayModEnv or Sf2Gen.AttackModEnv or Sf2Gen.HoldModEnv
            or Sf2Gen.DecayModEnv or Sf2Gen.ReleaseModEnv => -12000,
        Sf2Gen.DelayVolEnv or Sf2Gen.AttackVolEnv or Sf2Gen.HoldVolEnv
            or Sf2Gen.DecayVolEnv or Sf2Gen.ReleaseVolEnv => -12000,
        Sf2Gen.ScaleTuning => 100,
        Sf2Gen.OverridingRootKey or Sf2Gen.Keynum or Sf2Gen.Velocity => -1,
        _ => 0
    };

    /// <summary>
    /// True for generators that are only meaningful inside an instrument zone (sample addressing, looping,
    /// fixed key/velocity, exclusive class, root-key override, sub-references). The SF2 spec says such a
    /// generator in a preset zone is ignored, so the preset level never contributes an offset for it.
    /// </summary>
    public static bool IsInstrumentOnly(Sf2Gen g) => g switch
    {
        Sf2Gen.StartAddrsOffset or Sf2Gen.EndAddrsOffset or Sf2Gen.StartloopAddrsOffset
            or Sf2Gen.EndloopAddrsOffset or Sf2Gen.StartAddrsCoarseOffset or Sf2Gen.EndAddrsCoarseOffset
            or Sf2Gen.StartloopAddrsCoarseOffset or Sf2Gen.EndloopAddrsCoarseOffset
            or Sf2Gen.Keynum or Sf2Gen.Velocity or Sf2Gen.SampleModes or Sf2Gen.ExclusiveClass
            or Sf2Gen.OverridingRootKey or Sf2Gen.SampleID or Sf2Gen.Instrument => true,
        _ => false
    };

    /// <summary>Converts a timecents value to seconds (<c>2^(tc/1200)</c>). Very small values clamp to 0.</summary>
    public static double TimecentsToSeconds(int timecents)
        => timecents <= -32768 ? 0.0 : Math.Pow(2.0, timecents / 1200.0);

    /// <summary>Converts an absolute-cents pitch (8.176 Hz reference) to Hz: <c>8.176·2^(cents/1200)</c>.</summary>
    public static double AbsoluteCentsToHz(double cents) => 8.176 * Math.Pow(2.0, cents / 1200.0);

    /// <summary>Converts centibels of attenuation to a linear gain multiplier (<c>10^(-cB/200)</c>).</summary>
    public static double AttenuationToGain(double centibels)
        => centibels <= 0 ? 1.0 : Math.Pow(10.0, -centibels / 200.0);

    /// <summary>Converts a volume-envelope sustain (centibels of attenuation) to a 0..1 level.</summary>
    public static double SustainCentibelsToLevel(double centibels)
    {
        if (centibels <= 0) return 1.0;
        var lin = Math.Pow(10.0, -centibels / 200.0);
        return lin < 0 ? 0 : lin > 1 ? 1 : lin;
    }
}
