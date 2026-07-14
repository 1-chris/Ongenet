using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>How a region's sample repeats during playback.</summary>
public enum SamplerLoopMode
{
    NoLoop,
    OneShot,
    LoopContinuous,
    LoopSustain
}

/// <summary>Loop direction for v2 <c>loop_type</c>.</summary>
public enum SamplerLoopType
{
    Forward,
    Backward,
    Alternate
}

/// <summary>One parametric EQ band.</summary>
public readonly record struct SamplerEqBand(double Freq, double GainDb, double BandwidthOctaves);

/// <summary>What plays a region.</summary>
public enum SamplerTrigger
{
    Attack,
    Release,
    First,
    Legato
}

/// <summary>A CC→parameter mapping (legacy cutoff helper; prefer <see cref="SamplerModRoute"/>).</summary>
public readonly record struct SamplerCcMod(int Cc, double Depth);

/// <summary>CC-triggered region (on_locc / start_locc).</summary>
public readonly record struct SamplerCcTrigger(int Cc, int Lo, int Hi);

/// <summary>One flexible EG destination amount.</summary>
public readonly record struct SamplerFlexEgDest(SamplerModTarget Target, double Depth);

/// <summary>Minimal flex EG (AR levels/times) used by SFZ v2 <c>egN_*</c>.</summary>
public sealed class SamplerFlexEg
{
    public double[] Times { get; init; } = Array.Empty<double>();
    public double[] Levels { get; init; } = Array.Empty<double>();
    public int SustainPoint { get; init; } = -1;
    public int LoopStart { get; init; } = -1;
    public IReadOnlyList<SamplerFlexEgDest> Dests { get; init; } = Array.Empty<SamplerFlexEgDest>();
}

/// <summary>Minimal flex LFO used by SFZ v2 <c>lfoN_*</c>.</summary>
public sealed class SamplerFlexLfo
{
    public double Freq { get; init; }
    public double Delay { get; init; }
    public double Fade { get; init; }
    public int Wave { get; init; } // 0=sine
    public double Phase { get; init; }
    public IReadOnlyList<SamplerFlexEgDest> Dests { get; init; } = Array.Empty<SamplerFlexEgDest>();
}

/// <summary>
/// Pre-computed, audio-thread-ready playback parameters for one mapped sample zone.
/// </summary>
public sealed class SamplerRegion
{
    public required SamplerSample Sample { get; init; }

    public Guid LayerId { get; init; }
    public uint LayerColorArgb { get; init; }

    public int LoKey { get; init; }
    public int HiKey { get; init; }
    public int LoVel { get; init; }
    public int HiVel { get; init; }

    public int PitchKeycenter { get; init; }
    public double KeytrackSemisPerKey { get; init; }
    public double TransposeSemis { get; init; }
    public double TuneCents { get; init; }
    public double PitchVeltrack { get; init; }
    public double PitchRandom { get; init; }

    public double Gain { get; init; }
    public double Pan { get; init; }
    public double AmpVeltrack { get; init; }
    public double AmpKeytrack { get; init; }
    public int AmpKeycenter { get; init; } = 60;
    public double AmpRandom { get; init; }
    public float[]? AmpVelcurve { get; init; }
    public SamplerEgSpec AmpEg { get; init; }

    public long Offset { get; init; }
    public long End { get; init; }
    public SamplerLoopMode LoopMode { get; init; }
    public SamplerLoopType LoopType { get; init; }
    public long LoopStart { get; init; }
    public long LoopEnd { get; init; }
    public int LoopCount { get; init; } // 0 = infinite
    public double LoopCrossfadeSeconds { get; init; }
    public bool Reverse { get; init; }
    public bool InvertPhase { get; init; }

    public int SeqLength { get; init; } = 1;
    public int SeqPosition { get; init; } = 1;
    public int RoundRobinKey { get; init; }
    public double LoRand { get; init; }
    public double HiRand { get; init; } = 1.0;

    public int Group { get; init; }
    public int OffBy { get; init; } = -1;
    public SamplerOffMode OffMode { get; init; }

    public bool HasFilter { get; init; }
    public FilterMode FilterMode { get; init; }
    public double Cutoff { get; init; }
    public double FilterQ { get; init; }
    public double FilKeytrack { get; init; }
    public int FilKeycenter { get; init; } = 60;
    public double FilVeltrack { get; init; }
    public double FilRandom { get; init; }

    public bool HasFilter2 { get; init; }
    public FilterMode Filter2Mode { get; init; }
    public double Cutoff2 { get; init; }
    public double Filter2Q { get; init; }

    public bool HasFilEg { get; init; }
    public double FilEgDepth { get; init; }
    public SamplerEgSpec FilEg { get; init; }
    public bool HasFilLfo { get; init; }
    public double FilLfoFreq { get; init; }
    public double FilLfoDepth { get; init; }
    public double FilLfoDelay { get; init; }
    public double FilLfoFade { get; init; }

    public bool HasAmpLfo { get; init; }
    public double AmpLfoFreq { get; init; }
    public double AmpLfoDepthDb { get; init; }
    public double AmpLfoDelay { get; init; }
    public double AmpLfoFade { get; init; }

    public bool HasPitchLfo { get; init; }
    public double PitchLfoFreq { get; init; }
    public double PitchLfoDepth { get; init; }
    public double PitchLfoDelay { get; init; }
    public double PitchLfoFade { get; init; }
    public bool HasPitchEg { get; init; }
    public double PitchEgDepth { get; init; }
    public SamplerEgSpec PitchEg { get; init; }

    public IReadOnlyList<SamplerEqBand> EqBands { get; init; } = Array.Empty<SamplerEqBand>();

    public SamplerTrigger Trigger { get; init; }
    public int SwLast { get; init; } = -1;
    public int SwDown { get; init; } = -1;
    public int SwUp { get; init; } = -1;
    public int SwPrevious { get; init; } = -1;
    public int SwVel { get; init; } = -1;
    public int SwLoKey { get; init; } = -1;
    public int SwHiKey { get; init; } = -1;
    public int SwDefault { get; init; } = -1;

    public double BendUpCents { get; init; } = 200;
    public double BendDownCents { get; init; } = 200;
    public double BendStepCents { get; init; }
    public int LoBend { get; init; } = -8192;
    public int HiBend { get; init; } = 8191;

    public int LoChan { get; init; } = 1;
    public int HiChan { get; init; } = 16;
    public int LoChanAft { get; init; }
    public int HiChanAft { get; init; } = 127;
    public int LoPolyAft { get; init; }
    public int HiPolyAft { get; init; } = 127;
    public double LoBpm { get; init; }
    public double HiBpm { get; init; } = 10000;
    public int LoProg { get; init; }
    public int HiProg { get; init; } = 127;

    public IReadOnlyList<SamplerCcGate> CcGates { get; init; } = Array.Empty<SamplerCcGate>();
    public IReadOnlyList<SamplerCcTrigger> OnCcTriggers { get; init; } = Array.Empty<SamplerCcTrigger>();
    public IReadOnlyList<SamplerCcTrigger> StopCcTriggers { get; init; } = Array.Empty<SamplerCcTrigger>();
    public int ReverseLoCc { get; init; } = -1;
    public int ReverseHiCc { get; init; } = 127;

    public double DelaySeconds { get; init; }
    public double DelayRandom { get; init; }
    public double DelayBeats { get; init; }
    public long DelaySamples { get; init; }
    public double OffsetRandom { get; init; }
    public int Count { get; init; } // 0 = unlimited one-shot plays for count opcode (SFZ: number of repeats)
    public double RtDecayDb { get; init; }
    public bool RtDead { get; init; }
    public double SyncBeats { get; init; }
    public double SyncOffset { get; init; }
    public double StopBeats { get; init; }
    public double SampleFadeout { get; init; }
    public double Width { get; init; } = 100;
    public double Position { get; init; }

    public int Polyphony { get; init; } // 0 = unlimited
    public int NotePolyphony { get; init; }
    public bool NoteSelfMask { get; init; }
    public bool SustainSw { get; init; } = true;
    public bool SostenutoSw { get; init; } = true;

    public SamplerXfade? Xfade { get; init; }
    public IReadOnlyList<SamplerModRoute> ModRoutes { get; init; } = Array.Empty<SamplerModRoute>();
    public IReadOnlyList<SamplerFlexEg> FlexEgs { get; init; } = Array.Empty<SamplerFlexEg>();
    public IReadOnlyList<SamplerFlexLfo> FlexLfos { get; init; } = Array.Empty<SamplerFlexLfo>();

    /// <summary>Legacy cutoff CC list mirrored into <see cref="ModRoutes"/> for older callers/tests.</summary>
    public IReadOnlyList<SamplerCcMod> CutoffCc { get; init; } = Array.Empty<SamplerCcMod>();

    public bool ModActive =>
        HasFilter || HasFilter2 || EqBands.Count > 0 || HasAmpLfo || HasPitchLfo || HasPitchEg
        || ModRoutes.Count > 0 || CutoffCc.Count > 0 || FlexEgs.Count > 0 || FlexLfos.Count > 0
        || (Xfade?.IsActive ?? false);

    public bool Matches(int key, int vel)
        => key >= LoKey && key <= HiKey && vel >= LoVel && vel <= HiVel;

    public SamplerRegion WithLayer(Guid layerId, uint colorArgb = 0, int? keyMaskLo = null, int? keyMaskHi = null)
    {
        var lo = LoKey;
        var hi = HiKey;
        if (keyMaskLo is int mLo) lo = Math.Max(lo, mLo);
        if (keyMaskHi is int mHi) hi = Math.Min(hi, mHi);
        return Copy(layerId: layerId, layerColorArgb: colorArgb != 0 ? colorArgb : LayerColorArgb, loKey: lo, hiKey: hi);
    }

    /// <summary>Shallow structural copy with selective overrides (for UI Apply).</summary>
    public SamplerRegion Copy(
        SamplerSample? sample = null,
        Guid? layerId = null,
        uint? layerColorArgb = null,
        int? loKey = null,
        int? hiKey = null,
        int? loVel = null,
        int? hiVel = null,
        int? pitchKeycenter = null,
        double? gain = null,
        double? pan = null,
        int? seqLength = null,
        int? seqPosition = null,
        SamplerEgSpec? ampEg = null,
        SamplerEgSpec? filEg = null,
        SamplerEgSpec? pitchEg = null,
        bool? hasFilter = null,
        FilterMode? filterMode = null,
        double? cutoff = null,
        double? filterQ = null,
        IReadOnlyList<SamplerModRoute>? modRoutes = null,
        IReadOnlyList<SamplerCcMod>? cutoffCc = null)
        => new()
        {
            Sample = sample ?? Sample,
            LayerId = layerId ?? LayerId,
            LayerColorArgb = layerColorArgb ?? LayerColorArgb,
            LoKey = loKey ?? LoKey,
            HiKey = hiKey ?? HiKey,
            LoVel = loVel ?? LoVel,
            HiVel = hiVel ?? HiVel,
            PitchKeycenter = pitchKeycenter ?? PitchKeycenter,
            KeytrackSemisPerKey = KeytrackSemisPerKey,
            TransposeSemis = TransposeSemis,
            TuneCents = TuneCents,
            PitchVeltrack = PitchVeltrack,
            PitchRandom = PitchRandom,
            Gain = gain ?? Gain,
            Pan = pan ?? Pan,
            AmpVeltrack = AmpVeltrack,
            AmpKeytrack = AmpKeytrack,
            AmpKeycenter = AmpKeycenter,
            AmpRandom = AmpRandom,
            AmpVelcurve = AmpVelcurve,
            AmpEg = ampEg ?? AmpEg,
            Offset = Offset,
            End = End,
            LoopMode = LoopMode,
            LoopType = LoopType,
            LoopStart = LoopStart,
            LoopEnd = LoopEnd,
            LoopCount = LoopCount,
            LoopCrossfadeSeconds = LoopCrossfadeSeconds,
            Reverse = Reverse,
            InvertPhase = InvertPhase,
            SeqLength = seqLength ?? SeqLength,
            SeqPosition = seqPosition ?? SeqPosition,
            RoundRobinKey = RoundRobinKey,
            LoRand = LoRand,
            HiRand = HiRand,
            Group = Group,
            OffBy = OffBy,
            OffMode = OffMode,
            HasFilter = hasFilter ?? HasFilter,
            FilterMode = filterMode ?? FilterMode,
            Cutoff = cutoff ?? Cutoff,
            FilterQ = filterQ ?? FilterQ,
            FilKeytrack = FilKeytrack,
            FilKeycenter = FilKeycenter,
            FilVeltrack = FilVeltrack,
            FilRandom = FilRandom,
            HasFilter2 = HasFilter2,
            Filter2Mode = Filter2Mode,
            Cutoff2 = Cutoff2,
            Filter2Q = Filter2Q,
            HasFilEg = HasFilEg,
            FilEgDepth = FilEgDepth,
            FilEg = filEg ?? FilEg,
            HasFilLfo = HasFilLfo,
            FilLfoFreq = FilLfoFreq,
            FilLfoDepth = FilLfoDepth,
            FilLfoDelay = FilLfoDelay,
            FilLfoFade = FilLfoFade,
            HasAmpLfo = HasAmpLfo,
            AmpLfoFreq = AmpLfoFreq,
            AmpLfoDepthDb = AmpLfoDepthDb,
            AmpLfoDelay = AmpLfoDelay,
            AmpLfoFade = AmpLfoFade,
            HasPitchLfo = HasPitchLfo,
            PitchLfoFreq = PitchLfoFreq,
            PitchLfoDepth = PitchLfoDepth,
            PitchLfoDelay = PitchLfoDelay,
            PitchLfoFade = PitchLfoFade,
            HasPitchEg = HasPitchEg,
            PitchEgDepth = PitchEgDepth,
            PitchEg = pitchEg ?? PitchEg,
            EqBands = EqBands,
            Trigger = Trigger,
            SwLast = SwLast,
            SwDown = SwDown,
            SwUp = SwUp,
            SwPrevious = SwPrevious,
            SwVel = SwVel,
            SwLoKey = SwLoKey,
            SwHiKey = SwHiKey,
            SwDefault = SwDefault,
            BendUpCents = BendUpCents,
            BendDownCents = BendDownCents,
            BendStepCents = BendStepCents,
            LoBend = LoBend,
            HiBend = HiBend,
            LoChan = LoChan,
            HiChan = HiChan,
            LoChanAft = LoChanAft,
            HiChanAft = HiChanAft,
            LoPolyAft = LoPolyAft,
            HiPolyAft = HiPolyAft,
            LoBpm = LoBpm,
            HiBpm = HiBpm,
            LoProg = LoProg,
            HiProg = HiProg,
            CcGates = CcGates,
            OnCcTriggers = OnCcTriggers,
            StopCcTriggers = StopCcTriggers,
            ReverseLoCc = ReverseLoCc,
            ReverseHiCc = ReverseHiCc,
            DelaySeconds = DelaySeconds,
            DelayRandom = DelayRandom,
            DelayBeats = DelayBeats,
            DelaySamples = DelaySamples,
            OffsetRandom = OffsetRandom,
            Count = Count,
            RtDecayDb = RtDecayDb,
            RtDead = RtDead,
            SyncBeats = SyncBeats,
            SyncOffset = SyncOffset,
            StopBeats = StopBeats,
            SampleFadeout = SampleFadeout,
            Width = Width,
            Position = Position,
            Polyphony = Polyphony,
            NotePolyphony = NotePolyphony,
            NoteSelfMask = NoteSelfMask,
            SustainSw = SustainSw,
            SostenutoSw = SostenutoSw,
            Xfade = Xfade,
            ModRoutes = modRoutes ?? ModRoutes,
            FlexEgs = FlexEgs,
            FlexLfos = FlexLfos,
            CutoffCc = cutoffCc ?? CutoffCc,
        };

    /// <summary>Merges legacy <see cref="CutoffCc"/> into <see cref="ModRoutes"/>.</summary>
    public static IReadOnlyList<SamplerModRoute> MergeCutoffRoutes(
        IReadOnlyList<SamplerModRoute> routes, IReadOnlyList<SamplerCcMod> cutoffCc)
    {
        if (cutoffCc.Count == 0) return routes;
        var list = routes.Count == 0 ? new List<SamplerModRoute>() : routes.ToList();
        foreach (var cc in cutoffCc)
            list.Add(new SamplerModRoute(SamplerModTarget.CutoffCents, SamplerModSource.Cc, cc.Cc, cc.Depth));
        return list;
    }
}
