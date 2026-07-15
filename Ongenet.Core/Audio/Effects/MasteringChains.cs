using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Shared mastering insert chains so factory song masters and the FX Chains library stay identical.
/// </summary>
public static class MasteringChains
{
    /// <summary>
    /// Canonical Full Master order: corrective EQ → mid/side → glue → width → clipper →
    /// Peak Limiter (Streaming) → Spectrum analyser.
    /// Multiband OTT is intentionally omitted here (bus-level / Techno Master / Full Master+ instead).
    /// </summary>
    public static IAudioEffect[] CreateFullMaster()
    {
        var masterEq = new EqEffect();
        masterEq.Bands[0].Type = EqBandType.HighPass;
        masterEq.Bands[0].Frequency = 26;
        masterEq.Bands[0].Q = 0.7;
        masterEq.Bands[1].Type = EqBandType.LowPass;
        masterEq.Bands[1].Frequency = 19500;
        masterEq.Bands[1].Q = 0.7;
        masterEq.Bands[2].Type = EqBandType.HighShelf;
        masterEq.Bands[2].Frequency = 12000;
        masterEq.Bands[2].GainDb = 0.5;
        masterEq.CommitBands();

        const int streamingPresetIndex = 1;
        var streaming = MasteringPresetBank.GetLimiter(streamingPresetIndex);
        var peakLimiter = new PeakLimiterEffect
        {
            MasteringPresetIndex = streamingPresetIndex,
            ThresholdDb = streaming.ThresholdDb,
            CeilingDb = streaming.CeilingDb,
            ReleaseMs = streaming.ReleaseMs,
            SpectralLimiter = streaming.Spectral,
            OversampleIndex = 2 // 4× FIR for ISP / true-peak control
        };

        return
        [
            masterEq,
            new MidSideEqEffect { SideLowCutHz = 120, SideAirHz = 9000, SideAirDb = 1.2 },
            new CompressorEffect
            {
                ThresholdDb = -14, Ratio = 2.0, AttackMs = 30, ReleaseMs = 110, MakeupDb = 1.5
            },
            new StereoWidthEffect { Width = 1.1 },
            new ClipperEffect { DriveDb = 1.5, CeilingDb = -0.5, OversampleIndex = 1 }, // 2× FIR
            peakLimiter,
            new SpectrumEffect()
        ];
    }

    /// <summary>Full Master with Multiband OTT before width (trance wall-of-sound variant).</summary>
    public static IAudioEffect[] CreateFullMasterPlus()
    {
        var chain = new List<IAudioEffect>(CreateFullMaster());
        // Insert Multiband after glue compressor (index 3 before width was shifted).
        chain.Insert(3, new MultibandCompressorEffect { MasteringPresetIndex = 2, Depth = 0.55, HighBoostDb = 3 });
        return chain.ToArray();
    }

    /// <summary>Streaming-focused: EQ → glue → Peak Limiter (Streaming) — no clipper.</summary>
    public static IAudioEffect[] CreateStreamingMaster()
    {
        var eq = new EqEffect();
        eq.Bands[0].Type = EqBandType.HighPass;
        eq.Bands[0].Frequency = 30;
        eq.Bands[0].Q = 0.7;
        eq.CommitBands();
        var lim = MasteringPresetBank.GetLimiter(1);
        return
        [
            eq,
            new CompressorEffect { ThresholdDb = -16, Ratio = 2.0, AttackMs = 25, ReleaseMs = 120, MakeupDb = 1 },
            new PeakLimiterEffect
            {
                MasteringPresetIndex = 1,
                ThresholdDb = lim.ThresholdDb,
                CeilingDb = lim.CeilingDb,
                ReleaseMs = lim.ReleaseMs,
                OversampleIndex = 2
            },
            new SpectrumEffect()
        ];
    }

    /// <summary>Pre-master: DC blocker + corrective EQ + glue only (no limiter / clipper).</summary>
    public static IAudioEffect[] CreatePreMaster()
    {
        var eq = new EqEffect();
        eq.Bands[0].Type = EqBandType.HighPass;
        eq.Bands[0].Frequency = 28;
        eq.Bands[0].Q = 0.7;
        eq.CommitBands();
        return
        [
            new DcOffsetEffect(),
            eq,
            new MidSideEqEffect { SideLowCutHz = 100, SideAirDb = 0.8 },
            new CompressorEffect { ThresholdDb = -14, Ratio = 1.8, AttackMs = 35, ReleaseMs = 140, MakeupDb = 0.5 }
        ];
    }

    /// <summary>Club-loud master using Peak Limiter Master preset + clipper.</summary>
    public static IAudioEffect[] CreateClubLoud()
    {
        var lim = MasteringPresetBank.GetLimiter(3);
        return
        [
            new MultibandCompressorEffect { MasteringPresetIndex = 3 },
            new StereoWidthEffect { Width = 1.15 },
            new OverEffect { Drive = 2.2, Tone = 0.55, Mix = 0.18 },
            new ClipperEffect { DriveDb = 2.5, CeilingDb = -0.3, OversampleIndex = 1 },
            new PeakLimiterEffect
            {
                MasteringPresetIndex = 3,
                ThresholdDb = lim.ThresholdDb,
                CeilingDb = lim.CeilingDb,
                ReleaseMs = lim.ReleaseMs,
                SpectralLimiter = lim.Spectral,
                OversampleIndex = 1
            }
        ];
    }

    /// <summary>Podcast / speech: HPF + de-esser + gentle glue + Peak Limiter Safety.</summary>
    public static IAudioEffect[] CreatePodcastMaster()
    {
        var lim = MasteringPresetBank.GetLimiter(4);
        var eq = new EqEffect();
        eq.Bands[0].Type = EqBandType.HighPass;
        eq.Bands[0].Frequency = 80;
        eq.Bands[0].Q = 0.7;
        eq.CommitBands();
        return
        [
            eq,
            new DeEsserEffect(),
            new CompressorEffect { ThresholdDb = -20, Ratio = 3.5, AttackMs = 10, ReleaseMs = 80, MakeupDb = 3 },
            new PeakLimiterEffect
            {
                MasteringPresetIndex = 4,
                ThresholdDb = lim.ThresholdDb,
                CeilingDb = lim.CeilingDb,
                ReleaseMs = lim.ReleaseMs,
                OversampleIndex = 1
            }
        ];
    }

    /// <summary>Type ids for a named mastering chain without constructing effect instances.</summary>
    public static string[] TypeIds(string name) => name.Trim().ToLowerInvariant() switch
    {
        "full" or "full master" =>
        [
            EqEffect.TypeId, MidSideEqEffect.TypeId, CompressorEffect.TypeId, StereoWidthEffect.TypeId,
            ClipperEffect.TypeId, PeakLimiterEffect.TypeId, SpectrumEffect.TypeId
        ],
        "full+" or "full master+" or "fullplus" =>
        [
            EqEffect.TypeId, MidSideEqEffect.TypeId, CompressorEffect.TypeId, MultibandCompressorEffect.TypeId,
            StereoWidthEffect.TypeId, ClipperEffect.TypeId, PeakLimiterEffect.TypeId, SpectrumEffect.TypeId
        ],
        "streaming" or "streaming master" =>
        [
            EqEffect.TypeId, CompressorEffect.TypeId, PeakLimiterEffect.TypeId, SpectrumEffect.TypeId
        ],
        "premaster" or "pre-master" or "pre master" =>
        [
            DcOffsetEffect.TypeId, EqEffect.TypeId, MidSideEqEffect.TypeId, CompressorEffect.TypeId
        ],
        "club" or "club loud" =>
        [
            MultibandCompressorEffect.TypeId, StereoWidthEffect.TypeId, OverEffect.TypeId,
            ClipperEffect.TypeId, PeakLimiterEffect.TypeId
        ],
        "podcast" or "speech" =>
        [
            EqEffect.TypeId, DeEsserEffect.TypeId, CompressorEffect.TypeId, PeakLimiterEffect.TypeId
        ],
        "glue" or "master glue" =>
        [
            CompressorEffect.TypeId, StereoWidthEffect.TypeId, PeakLimiterEffect.TypeId
        ],
        "techno" or "techno master" =>
        [
            FilterEffect.TypeId, MultibandCompressorEffect.TypeId, StereoWidthEffect.TypeId,
            ExciterEffect.TypeId, PeakLimiterEffect.TypeId
        ],
        "audiophile" or "audiophile master" or "linear" =>
        [
            LinearPhaseEqEffect.TypeId, MidSideEqEffect.TypeId, CompressorEffect.TypeId, StereoWidthEffect.TypeId,
            ClipperEffect.TypeId, PeakLimiterEffect.TypeId, SpectrumEffect.TypeId
        ],
        "reference" or "reference master" or "match" =>
        [
            EqEffect.TypeId, MatchEqEffect.TypeId, CompressorEffect.TypeId,
            PeakLimiterEffect.TypeId, SpectrumEffect.TypeId
        ],
        _ => TypeIds("full")
    };

    public static void AddFullMaster(IList<IAudioEffect> effects)
    {
        foreach (var fx in CreateFullMaster())
            effects.Add(fx);
    }

    /// <summary>Appends a named mastering chain (same names as <see cref="Create"/>).</summary>
    public static void Add(IList<IAudioEffect> effects, string name)
    {
        foreach (var fx in Create(name))
            effects.Add(fx);
    }

    /// <summary>Builds a named mastering chain: full, full+, glue, techno, streaming, premaster, club, podcast.</summary>
    public static IAudioEffect[] Create(string name) => name.Trim().ToLowerInvariant() switch
    {
        "full" or "full master" => CreateFullMaster(),
        "full+" or "full master+" or "fullplus" => CreateFullMasterPlus(),
        "streaming" or "streaming master" => CreateStreamingMaster(),
        "premaster" or "pre-master" or "pre master" => CreatePreMaster(),
        "club" or "club loud" => CreateClubLoud(),
        "podcast" or "speech" => CreatePodcastMaster(),
        "glue" or "master glue" => CreateMasterGlue(),
        "techno" or "techno master" => CreateTechnoMaster(),
        "audiophile" or "audiophile master" or "linear" => CreateAudiophileMaster(),
        "reference" or "reference master" or "match" => CreateReferenceMaster(),
        _ => CreateFullMaster()
    };

    /// <summary>
    /// Reference-oriented master: corrective EQ → Match EQ (capture via UI) → glue → Peak Limiter → Spectrum.
    /// </summary>
    public static IAudioEffect[] CreateReferenceMaster()
    {
        var masterEq = new EqEffect();
        masterEq.Bands[0].Type = EqBandType.HighPass;
        masterEq.Bands[0].Frequency = 26;
        masterEq.Bands[0].Q = 0.7;
        masterEq.Bands[1].Type = EqBandType.LowPass;
        masterEq.Bands[1].Frequency = 19500;
        masterEq.Bands[1].Q = 0.7;
        masterEq.CommitBands();

        const int streamingPresetIndex = 1;
        var streaming = MasteringPresetBank.GetLimiter(streamingPresetIndex);
        return
        [
            masterEq,
            new MatchEqEffect { Blend = 0.35 },
            new CompressorEffect
            {
                ThresholdDb = -14, Ratio = 2.0, AttackMs = 30, ReleaseMs = 110, MakeupDb = 1.5
            },
            new PeakLimiterEffect
            {
                MasteringPresetIndex = streamingPresetIndex,
                ThresholdDb = streaming.ThresholdDb,
                CeilingDb = streaming.CeilingDb,
                ReleaseMs = streaming.ReleaseMs,
                SpectralLimiter = streaming.Spectral,
                OversampleIndex = 2
            },
            new SpectrumEffect()
        ];
    }

    /// <summary>
    /// Full Master with Linear-Phase EQ replacing the minimum-phase corrective EQ — higher latency,
    /// better phase coherence for archival / audiophile delivery. Marked experimental in the registry.
    /// </summary>
    public static IAudioEffect[] CreateAudiophileMaster()
    {
        var chain = new List<IAudioEffect>(CreateFullMaster());
        chain[0] = new LinearPhaseEqEffect
        {
            LowFreq = 80, LowGainDb = 0,
            LowMidFreq = 400, LowMidGainDb = 0,
            HighMidFreq = 3000, HighMidGainDb = 0,
            HighFreq = 12000, HighGainDb = 0.5
        };
        return chain.ToArray();
    }

    private static IAudioEffect[] CreateMasterGlue()
    {
        const int loudPreset = 2;
        var loud = MasteringPresetBank.GetLimiter(loudPreset);
        return
        [
            new CompressorEffect { ThresholdDb = -14, Ratio = 2.0, AttackMs = 30, ReleaseMs = 200, MakeupDb = 2 },
            new StereoWidthEffect { Width = 1.1 },
            new PeakLimiterEffect
            {
                MasteringPresetIndex = loudPreset,
                ThresholdDb = loud.ThresholdDb,
                CeilingDb = loud.CeilingDb,
                ReleaseMs = loud.ReleaseMs,
                SpectralLimiter = loud.Spectral
            }
        ];
    }

    private static IAudioEffect[] CreateTechnoMaster()
    {
        const int masterPreset = 3;
        var mastering = MasteringPresetBank.GetLimiter(masterPreset);
        return
        [
            new FilterEffect { Mode = FilterMode.HighPass, Frequency = 30, Resonance = 0.4 },
            new MultibandCompressorEffect(),
            new StereoWidthEffect { Width = 1.15 },
            new ExciterEffect { Drive = 3.0, Mix = 0.12, ToneHz = 4500 },
            new PeakLimiterEffect
            {
                MasteringPresetIndex = masterPreset,
                ThresholdDb = mastering.ThresholdDb,
                CeilingDb = mastering.CeilingDb,
                ReleaseMs = mastering.ReleaseMs,
                SpectralLimiter = mastering.Spectral
            }
        ];
    }
}
