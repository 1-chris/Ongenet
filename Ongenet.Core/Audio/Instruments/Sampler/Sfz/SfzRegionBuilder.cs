using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sfz;

/// <summary>Builds a <see cref="SamplerRegion"/> from flattened SFZ opcodes.</summary>
public static class SfzRegionBuilder
{
    public static SamplerRegion? Build(SfzRegion region, SamplerSample? sample)
    {
        if (sample is null) return null;
        var o = region.Opcodes;

        var key = SfzNote.Parse(o.Get("key"));
        var loKey = o.GetKey("lokey", key ?? 0);
        var hiKey = o.GetKey("hikey", key ?? 127);
        var keycenter = o.GetKey("pitch_keycenter", key ?? 60);
        var volume = o.GetDouble("volume", 0.0);
        var amplitude = o.GetDouble("amplitude", 100.0);
        var loopMode = ParseLoopMode(o.Get("loop_mode") ?? o.Get("loopmode"));
        var frames = sample.FrameCount;
        var offset = Clamp(o.GetInt("offset", 0), 0, frames);
        var end = o.GetInt("end", -1);
        var endFrame = end < 0 ? frames : Clamp(end + 1, 0, frames);
        var loopStart = Clamp((long)o.GetInt("loop_start", o.GetInt("loopstart", 0)), 0, frames);
        var loopEndOp = o.GetInt("loop_end", o.GetInt("loopend", -1));
        var loopEnd = loopEndOp < 0 ? endFrame : Clamp(loopEndOp + 1, 0, frames);

        var cutoff = o.GetDouble("cutoff", -1);
        var hasFilter = cutoff > 0;
        var resonance = o.GetDouble("resonance", 0.0);
        var filterQ = 0.70710678 * AudioMath.Db2Lin(resonance);
        var filEgDepth = o.GetDouble("fileg_depth", 0.0);
        var filLfoFreq = o.GetDouble("fillfo_freq", 0.0);
        var filLfoDepth = o.GetDouble("fillfo_depth", 0.0);
        var ampLfoFreq = o.GetDouble("amplfo_freq", 0.0);
        var ampLfoDepth = o.GetDouble("amplfo_depth", 0.0);
        var pitchLfoFreq = o.GetDouble("pitchlfo_freq", 0.0);
        var pitchLfoDepth = o.GetDouble("pitchlfo_depth", 0.0);
        var pitchEgDepth = o.GetDouble("pitcheg_depth", 0.0);

        var cutoff2 = o.GetDouble("cutoff2", -1);
        var hasFilter2 = cutoff2 > 0;
        var res2 = o.GetDouble("resonance2", 0.0);

        var eqBands = new List<SamplerEqBand>(3);
        for (var i = 1; i <= 3; i++)
        {
            var freq = o.GetDouble($"eq{i}_freq", -1);
            var gain = o.GetDouble($"eq{i}_gain", 0.0);
            if (freq > 0 && gain != 0.0)
                eqBands.Add(new SamplerEqBand(freq, gain, o.GetDouble($"eq{i}_bw", 1.0)));
        }

        var cutoffCc = new List<SamplerCcMod>();
        var routes = new List<SamplerModRoute>();
        var ccGates = new List<SamplerCcGate>();
        var onCc = new List<SamplerCcTrigger>();
        var stopCc = new List<SamplerCcTrigger>();
        var ampVelcurve = new float[128];
        var hasVelcurve = false;
        for (var i = 0; i < 128; i++) ampVelcurve[i] = i / 127f;

        foreach (var kv in o.Raw)
        {
            var name = kv.Key;
            if (!double.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                && !name.StartsWith("amp_velcurve_", StringComparison.Ordinal)
                && !name.StartsWith("xf_", StringComparison.Ordinal)
                && name is not ("off_mode" or "loop_type" or "phase" or "sustain_sw" or "sostenuto_sw"
                    or "note_selfmask" or "xf_keycurve" or "xf_velcurve" or "xf_cccurve"))
            {
                // non-numeric handled below selectively
            }

            void AddRoute(SamplerModTarget target, string[] prefixes, double depthScale = 1.0)
            {
                foreach (var p in prefixes)
                {
                    var cc = ParseCcOpcode(name, p);
                    if (cc is { } n && double.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d != 0)
                        routes.Add(new SamplerModRoute(target, SamplerModSource.Cc, n, d * depthScale));
                }
            }

            AddRoute(SamplerModTarget.ResonanceDb, new[] { "resonance_cc", "resonance_oncc" });
            AddRoute(SamplerModTarget.AmplitudeDb, new[] { "gain_cc", "gain_oncc", "volume_cc", "volume_oncc" });
            AddRoute(SamplerModTarget.Pan, new[] { "pan_cc", "pan_oncc" }, 0.01); // SFZ pan is -100..100
            AddRoute(SamplerModTarget.PitchCents, new[] { "pitch_cc", "pitch_oncc" });
            AddRoute(SamplerModTarget.DelaySeconds, new[] { "delay_cc", "delay_oncc" });
            AddRoute(SamplerModTarget.OffsetFrames, new[] { "offset_cc", "offset_oncc" });

            {
                var cc = ParseCcOpcode(name, "cutoff_cc") ?? ParseCcOpcode(name, "cutoff_oncc");
                if (cc is { } n && double.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var cents) && cents != 0)
                    cutoffCc.Add(new SamplerCcMod(n, cents));
            }

            if (ParseCcOpcode(name, "locc") is { } loCc && int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var loV))
            {
                var hi = o.GetInt("hicc" + loCc, 127);
                ccGates.Add(new SamplerCcGate(loCc, loV, hi));
            }

            void ParseTriggerPair(string loPrefix, string hiPrefix, List<SamplerCcTrigger> list)
            {
                var cc = ParseCcOpcode(name, loPrefix);
                if (cc is null) return;
                if (!int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo)) return;
                var hi = o.GetInt(hiPrefix + cc, 127);
                list.Add(new SamplerCcTrigger(cc.Value, lo, hi));
            }

            ParseTriggerPair("on_locc", "on_hicc", onCc);
            ParseTriggerPair("start_locc", "start_hicc", onCc);
            ParseTriggerPair("stop_locc", "stop_hicc", stopCc);

            if (name.StartsWith("amp_velcurve_", StringComparison.Ordinal)
                && int.TryParse(name.AsSpan("amp_velcurve_".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var vi)
                && vi is >= 0 and <= 127
                && float.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var vv))
            {
                ampVelcurve[vi] = Math.Clamp(vv, 0f, 1f);
                hasVelcurve = true;
            }

            // Smooth / curve / step on continuous routes (attach to last matching route of same target+cc)
            if (name.Contains("smoothcc", StringComparison.Ordinal)
                || name.Contains("curvecc", StringComparison.Ordinal)
                || name.Contains("stepcc", StringComparison.Ordinal))
            {
                // Applied via secondary scan below on routes already present — decode in AnnotateRoutes.
            }
        }

        AnnotateRoutes(o, routes);

        var xfade = BuildXfade(o);
        var bendDown = o.GetDouble("bend_down", -200.0);
        var loRand = o.GetDouble("lorand", 0.0);
        var hiRand = o.GetDouble("hirand", 1.0);

        var flexEgs = BuildFlexEgs(o);
        var flexLfos = BuildFlexLfos(o);

        var swLast = o.Has("sw_last") ? o.GetKey("sw_last", -1) : -1;
        var swDown = o.Has("sw_down") ? o.GetKey("sw_down", -1) : -1;

        var modRoutes = SamplerRegion.MergeCutoffRoutes(routes, cutoffCc);

        return new SamplerRegion
        {
            Sample = sample,
            LoKey = loKey,
            HiKey = hiKey,
            LoVel = o.GetInt("lovel", 0),
            HiVel = o.GetInt("hivel", 127),
            PitchKeycenter = keycenter,
            KeytrackSemisPerKey = o.GetDouble("pitch_keytrack", 100.0) / 100.0,
            TransposeSemis = o.GetDouble("transpose", 0.0),
            TuneCents = o.GetDouble("tune", o.GetDouble("pitch", 0.0)),
            PitchVeltrack = o.GetDouble("pitch_veltrack", 0.0),
            PitchRandom = o.GetDouble("pitch_random", 0.0),
            Gain = AudioMath.Db2Lin(volume) * (amplitude / 100.0),
            Pan = AudioMath.Clamp(o.GetDouble("pan", 0.0) / 100.0, -1.0, 1.0),
            AmpVeltrack = o.GetDouble("amp_veltrack", 100.0),
            AmpKeytrack = o.GetDouble("amp_keytrack", 0.0),
            AmpKeycenter = o.GetKey("amp_keycenter", 60),
            AmpRandom = o.GetDouble("amp_random", 0.0),
            AmpVelcurve = hasVelcurve ? ampVelcurve : null,
            AmpEg = ReadEg(o, "ampeg", 100.0),
            Offset = offset,
            End = endFrame,
            LoopMode = loopMode,
            LoopType = ParseLoopType(o.Get("loop_type")),
            LoopStart = loopStart,
            LoopEnd = loopEnd > loopStart ? loopEnd : endFrame,
            LoopCount = o.GetInt("loop_count", 0),
            LoopCrossfadeSeconds = o.GetDouble("loop_crossfade", 0.0),
            Reverse = (o.Get("direction") ?? "forward") == "reverse",
            InvertPhase = (o.Get("phase") ?? "normal") == "invert",
            SeqLength = Math.Max(1, o.GetInt("seq_length", 1)),
            SeqPosition = o.GetInt("seq_position", 1),
            RoundRobinKey = region.GroupIndex >= 0
                ? region.GroupIndex
                : unchecked(loKey * 1000003 + hiKey * 1009 + o.GetInt("lovel", 0) * 31 + o.GetInt("hivel", 127)),
            LoRand = loRand,
            HiRand = hiRand,
            Group = o.GetInt("group", 0),
            OffBy = o.GetInt("off_by", -1),
            OffMode = (o.Get("off_mode") ?? "fast") == "normal" ? SamplerOffMode.Normal : SamplerOffMode.Fast,
            HasFilter = hasFilter,
            FilterMode = MapFilterType(o.Get("fil_type")),
            Cutoff = cutoff,
            FilterQ = filterQ,
            FilKeytrack = o.GetDouble("fil_keytrack", 0.0),
            FilKeycenter = o.GetKey("fil_keycenter", 60),
            FilVeltrack = o.GetDouble("fil_veltrack", 0.0),
            FilRandom = o.GetDouble("fil_random", 0.0),
            HasFilter2 = hasFilter2,
            Filter2Mode = MapFilterType(o.Get("fil2_type")),
            Cutoff2 = cutoff2,
            Filter2Q = 0.70710678 * AudioMath.Db2Lin(res2),
            HasFilEg = hasFilter && filEgDepth != 0.0,
            FilEgDepth = filEgDepth,
            FilEg = ReadEg(o, "fileg", 100.0),
            HasFilLfo = hasFilter && filLfoFreq > 0.0 && filLfoDepth != 0.0,
            FilLfoFreq = filLfoFreq,
            FilLfoDepth = filLfoDepth,
            FilLfoDelay = o.GetDouble("fillfo_delay", 0.0),
            FilLfoFade = o.GetDouble("fillfo_fade", 0.0),
            HasAmpLfo = ampLfoFreq > 0.0 && ampLfoDepth != 0.0,
            AmpLfoFreq = ampLfoFreq,
            AmpLfoDepthDb = ampLfoDepth,
            AmpLfoDelay = o.GetDouble("amplfo_delay", 0.0),
            AmpLfoFade = o.GetDouble("amplfo_fade", 0.0),
            HasPitchLfo = pitchLfoFreq > 0.0 && pitchLfoDepth != 0.0,
            PitchLfoFreq = pitchLfoFreq,
            PitchLfoDepth = pitchLfoDepth,
            PitchLfoDelay = o.GetDouble("pitchlfo_delay", 0.0),
            PitchLfoFade = o.GetDouble("pitchlfo_fade", 0.0),
            HasPitchEg = pitchEgDepth != 0.0,
            PitchEgDepth = pitchEgDepth,
            PitchEg = ReadEg(o, "pitcheg", 0.0),
            EqBands = eqBands,
            Trigger = ParseTrigger(o.Get("trigger")),
            SwLast = swLast,
            SwDown = swDown,
            SwUp = o.Has("sw_up") ? o.GetKey("sw_up", -1) : -1,
            SwPrevious = o.Has("sw_previous") ? o.GetKey("sw_previous", -1) : -1,
            SwVel = o.GetInt("sw_vel", -1),
            SwLoKey = o.GetKey("sw_lokey", -1),
            SwHiKey = o.GetKey("sw_hikey", -1),
            SwDefault = o.GetKey("sw_default", -1),
            BendUpCents = o.GetDouble("bend_up", 200.0),
            BendDownCents = Math.Abs(bendDown),
            BendStepCents = o.GetDouble("bend_step", o.GetDouble("bend_stepup", 0.0)),
            LoBend = o.GetInt("lobend", -8192),
            HiBend = o.GetInt("hibend", 8191),
            LoChan = o.GetInt("lochan", 1),
            HiChan = o.GetInt("hichan", 16),
            LoChanAft = o.GetInt("lochanaft", 0),
            HiChanAft = o.GetInt("hichanaft", 127),
            LoPolyAft = o.GetInt("lopolyaft", 0),
            HiPolyAft = o.GetInt("hipolyaft", 127),
            LoBpm = o.GetDouble("lobpm", 0.0),
            HiBpm = o.GetDouble("hibpm", 10000.0),
            LoProg = o.GetInt("loprog", 0),
            HiProg = o.GetInt("hiprog", 127),
            CcGates = DedupGates(ccGates),
            OnCcTriggers = onCc,
            StopCcTriggers = stopCc,
            ReverseLoCc = FirstSuffixInt(o, "reverse_locc", -1),
            ReverseHiCc = FirstSuffixInt(o, "reverse_hicc", 127),
            DelaySeconds = o.GetDouble("delay", 0.0),
            DelayRandom = o.GetDouble("delay_random", 0.0),
            DelayBeats = o.GetDouble("delay_beats", 0.0),
            DelaySamples = o.GetInt("delay_samples", 0),
            OffsetRandom = o.GetDouble("offset_random", 0.0),
            Count = o.GetInt("count", 0),
            RtDecayDb = o.GetDouble("rt_decay", 0.0),
            RtDead = o.GetInt("rt_dead", 0) != 0,
            SyncBeats = o.GetDouble("sync_beats", 0.0),
            SyncOffset = o.GetDouble("sync_offset", 0.0),
            StopBeats = o.GetDouble("stop_beats", 0.0),
            SampleFadeout = o.GetDouble("sample_fadeout", 0.0),
            Width = o.GetDouble("width", 100.0),
            Position = o.GetDouble("position", 0.0),
            Polyphony = o.GetInt("polyphony", 0),
            NotePolyphony = o.GetInt("note_polyphony", 0),
            NoteSelfMask = (o.Get("note_selfmask") ?? "on") != "off",
            SustainSw = (o.Get("sustain_sw") ?? "on") != "off",
            SostenutoSw = (o.Get("sostenuto_sw") ?? "on") != "off",
            Xfade = xfade,
            ModRoutes = modRoutes,
            FlexEgs = flexEgs,
            FlexLfos = flexLfos,
            CutoffCc = cutoffCc,
        };
    }

    private static void AnnotateRoutes(SfzOpcodes o, List<SamplerModRoute> routes)
    {
        // Attach curve/smooth/step from companion opcodes like volume_curvecc74=3
        for (var i = 0; i < routes.Count; i++)
        {
            var r = routes[i];
            if (r.Source != SamplerModSource.Cc) continue;
            var prefixes = TargetPrefixes(r.Target);
            foreach (var prefix in prefixes)
            {
                var curve = o.GetInt($"{prefix}curvecc{r.SourceIndex}", -1);
                var smooth = o.GetDouble($"{prefix}smoothcc{r.SourceIndex}", 0);
                var step = o.GetDouble($"{prefix}stepcc{r.SourceIndex}", 0);
                if (curve >= 0 || smooth > 0 || step > 0)
                    routes[i] = r with { CurveId = curve, SmoothSeconds = smooth / 1000.0, Step = step };
            }
        }

        // Aftertouch → cutoff
        if (o.Has("cutoff_chanaft"))
            routes.Add(new SamplerModRoute(SamplerModTarget.CutoffCents, SamplerModSource.ChannelAftertouch, 0,
                o.GetDouble("cutoff_chanaft", 0)));
        if (o.Has("cutoff_polyaft"))
            routes.Add(new SamplerModRoute(SamplerModTarget.CutoffCents, SamplerModSource.PolyAftertouch, 0,
                o.GetDouble("cutoff_polyaft", 0)));
    }

    private static string[] TargetPrefixes(SamplerModTarget t) => t switch
    {
        SamplerModTarget.AmplitudeDb => new[] { "volume_", "gain_" },
        SamplerModTarget.Pan => new[] { "pan_" },
        SamplerModTarget.PitchCents => new[] { "pitch_" },
        SamplerModTarget.CutoffCents => new[] { "cutoff_" },
        SamplerModTarget.ResonanceDb => new[] { "resonance_" },
        _ => Array.Empty<string>()
    };

    private static SamplerXfade? BuildXfade(SfzOpcodes o)
    {
        var xf = new SamplerXfade
        {
            XfinLoKey = o.Has("xfin_lokey") ? o.GetKey("xfin_lokey", -1) : -1,
            XfinHiKey = o.Has("xfin_hikey") ? o.GetKey("xfin_hikey", -1) : -1,
            XfoutLoKey = o.Has("xfout_lokey") ? o.GetKey("xfout_lokey", -1) : -1,
            XfoutHiKey = o.Has("xfout_hikey") ? o.GetKey("xfout_hikey", -1) : -1,
            XfinLoVel = o.GetInt("xfin_lovel", -1),
            XfinHiVel = o.GetInt("xfin_hivel", -1),
            XfoutLoVel = o.GetInt("xfout_lovel", -1),
            XfoutHiVel = o.GetInt("xfout_hivel", -1),
            KeyCurve = ParseXfCurve(o.Get("xf_keycurve")),
            VelCurve = ParseXfCurve(o.Get("xf_velcurve")),
            CcCurve = ParseXfCurve(o.Get("xf_cccurve")),
        };

        // CC xfades: find first xfin_loccN
        foreach (var kv in o.Raw)
        {
            var cc = ParseCcOpcode(kv.Key, "xfin_locc");
            if (cc is null) continue;
            xf = new SamplerXfade
            {
                XfinLoKey = xf.XfinLoKey,
                XfinHiKey = xf.XfinHiKey,
                XfoutLoKey = xf.XfoutLoKey,
                XfoutHiKey = xf.XfoutHiKey,
                XfinLoVel = xf.XfinLoVel,
                XfinHiVel = xf.XfinHiVel,
                XfoutLoVel = xf.XfoutLoVel,
                XfoutHiVel = xf.XfoutHiVel,
                XfadeCc = cc.Value,
                XfinLoCc = o.GetInt("xfin_locc" + cc, -1),
                XfinHiCc = o.GetInt("xfin_hicc" + cc, -1),
                XfoutLoCc = o.GetInt("xfout_locc" + cc, -1),
                XfoutHiCc = o.GetInt("xfout_hicc" + cc, -1),
                KeyCurve = xf.KeyCurve,
                VelCurve = xf.VelCurve,
                CcCurve = xf.CcCurve,
            };
            break;
        }

        return xf.IsActive ? xf : null;
    }

    private static SamplerXfadeCurve ParseXfCurve(string? v)
        => v == "power" ? SamplerXfadeCurve.Power : SamplerXfadeCurve.Gain;

    private static List<SamplerFlexEg> BuildFlexEgs(SfzOpcodes o)
    {
        var ids = new HashSet<int>();
        foreach (var key in o.Raw.Keys)
        {
            if (key.Length > 3 && key.StartsWith("eg", StringComparison.Ordinal) && char.IsDigit(key[2]))
            {
                var n = 0;
                var i = 2;
                while (i < key.Length && char.IsDigit(key[i])) { n = n * 10 + (key[i] - '0'); i++; }
                ids.Add(n);
            }
        }

        var list = new List<SamplerFlexEg>();
        foreach (var id in ids.OrderBy(x => x))
        {
            var times = new List<double>();
            var levels = new List<double>();
            for (var s = 0; s < 16; s++)
            {
                if (!o.Has($"eg{id}_time{s}") && !o.Has($"eg{id}_level{s}")) break;
                times.Add(o.GetDouble($"eg{id}_time{s}", 0));
                levels.Add(o.GetDouble($"eg{id}_level{s}", 0) / 100.0);
            }
            if (times.Count == 0) continue;
            var dests = new List<SamplerFlexEgDest>();
            AddFlexDest(o, $"eg{id}_volume", SamplerModTarget.AmplitudeDb, dests);
            AddFlexDest(o, $"eg{id}_amplitude", SamplerModTarget.AmplitudeDb, dests);
            AddFlexDest(o, $"eg{id}_pitch", SamplerModTarget.PitchCents, dests);
            AddFlexDest(o, $"eg{id}_cutoff", SamplerModTarget.CutoffCents, dests);
            AddFlexDest(o, $"eg{id}_pan", SamplerModTarget.Pan, dests, 0.01);
            if (dests.Count == 0) continue;
            list.Add(new SamplerFlexEg
            {
                Times = times.ToArray(),
                Levels = levels.ToArray(),
                SustainPoint = o.GetInt($"eg{id}_sustain", -1),
                LoopStart = o.GetInt($"eg{id}_loop", -1),
                Dests = dests
            });
        }
        return list;
    }

    private static List<SamplerFlexLfo> BuildFlexLfos(SfzOpcodes o)
    {
        var ids = new HashSet<int>();
        foreach (var key in o.Raw.Keys)
        {
            if (key.Length > 4 && key.StartsWith("lfo", StringComparison.Ordinal) && char.IsDigit(key[3]))
            {
                var n = 0;
                var i = 3;
                while (i < key.Length && char.IsDigit(key[i])) { n = n * 10 + (key[i] - '0'); i++; }
                ids.Add(n);
            }
        }

        var list = new List<SamplerFlexLfo>();
        foreach (var id in ids.OrderBy(x => x))
        {
            var freq = o.GetDouble($"lfo{id}_freq", 0);
            if (freq <= 0) continue;
            var dests = new List<SamplerFlexEgDest>();
            AddFlexDest(o, $"lfo{id}_volume", SamplerModTarget.AmplitudeDb, dests);
            AddFlexDest(o, $"lfo{id}_amplitude", SamplerModTarget.AmplitudeDb, dests);
            AddFlexDest(o, $"lfo{id}_pitch", SamplerModTarget.PitchCents, dests);
            AddFlexDest(o, $"lfo{id}_cutoff", SamplerModTarget.CutoffCents, dests);
            AddFlexDest(o, $"lfo{id}_pan", SamplerModTarget.Pan, dests, 0.01);
            if (dests.Count == 0) continue;
            list.Add(new SamplerFlexLfo
            {
                Freq = freq,
                Delay = o.GetDouble($"lfo{id}_delay", 0),
                Fade = o.GetDouble($"lfo{id}_fade", 0),
                Wave = o.GetInt($"lfo{id}_wave", 0),
                Phase = o.GetDouble($"lfo{id}_phase", 0),
                Dests = dests
            });
        }
        return list;
    }

    private static void AddFlexDest(SfzOpcodes o, string name, SamplerModTarget target,
        List<SamplerFlexEgDest> dests, double scale = 1.0)
    {
        if (!o.Has(name)) return;
        var d = o.GetDouble(name, 0) * scale;
        if (d != 0) dests.Add(new SamplerFlexEgDest(target, d));
    }

    private static SamplerEgSpec ReadEg(SfzOpcodes o, string prefix, double defaultSustain) => new()
    {
        Delay = o.GetDouble(prefix + "_delay", 0.0),
        Start = o.GetDouble(prefix + "_start", 0.0) / 100.0,
        Attack = o.GetDouble(prefix + "_attack", 0.0),
        Hold = o.GetDouble(prefix + "_hold", 0.0),
        Decay = o.GetDouble(prefix + "_decay", 0.0),
        Sustain = AudioMath.Clamp(o.GetDouble(prefix + "_sustain", defaultSustain) / 100.0, 0.0, 1.0),
        Release = o.GetDouble(prefix + "_release", 0.0),
        Vel2Delay = o.GetDouble(prefix + "_vel2delay", 0.0),
        Vel2Attack = o.GetDouble(prefix + "_vel2attack", 0.0),
        Vel2Hold = o.GetDouble(prefix + "_vel2hold", 0.0),
        Vel2Decay = o.GetDouble(prefix + "_vel2decay", 0.0),
        Vel2Sustain = o.GetDouble(prefix + "_vel2sustain", 0.0) / 100.0,
        Vel2Release = o.GetDouble(prefix + "_vel2release", 0.0),
    };

    private static SamplerTrigger ParseTrigger(string? value) => value switch
    {
        "release" => SamplerTrigger.Release,
        "first" => SamplerTrigger.First,
        "legato" => SamplerTrigger.Legato,
        _ => SamplerTrigger.Attack
    };

    private static SamplerLoopMode ParseLoopMode(string? value) => value switch
    {
        "one_shot" => SamplerLoopMode.OneShot,
        "loop_continuous" => SamplerLoopMode.LoopContinuous,
        "loop_sustain" => SamplerLoopMode.LoopSustain,
        _ => SamplerLoopMode.NoLoop
    };

    private static SamplerLoopType ParseLoopType(string? value) => value switch
    {
        "backward" => SamplerLoopType.Backward,
        "alternate" => SamplerLoopType.Alternate,
        _ => SamplerLoopType.Forward
    };

    private static FilterMode MapFilterType(string? filType) => filType switch
    {
        "hpf_1p" or "hpf_2p" or "hpf_4p" or "hpf_6p" => FilterMode.HighPass,
        "bpf_1p" or "bpf_2p" => FilterMode.BandPass,
        "brf_1p" or "brf_2p" => FilterMode.Notch,
        _ => FilterMode.LowPass
    };

    public static int? ParseCcOpcode(string opcode, string prefix)
    {
        if (!opcode.StartsWith(prefix, StringComparison.Ordinal)) return null;
        return int.TryParse(opcode.AsSpan(prefix.Length), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static int FirstSuffixInt(SfzOpcodes o, string prefix, int fallback)
    {
        foreach (var kv in o.Raw)
        {
            if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;
        }
        return fallback;
    }

    private static IReadOnlyList<SamplerCcGate> DedupGates(List<SamplerCcGate> gates)
    {
        if (gates.Count == 0) return Array.Empty<SamplerCcGate>();
        var map = new Dictionary<int, SamplerCcGate>();
        foreach (var g in gates) map[g.Cc] = g;
        return map.Values.ToList();
    }

    private static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;
    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
}
