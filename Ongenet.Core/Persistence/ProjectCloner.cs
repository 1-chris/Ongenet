using System.IO;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Persistence;

/// <summary>
/// Produces an in-memory deep copy of a <see cref="Project"/> for the undo/redo history. The structural
/// graph (project → tracks → clips → notes, automation lanes/points) is copied, but immutable heavy data —
/// decoded <see cref="AudioSampleBuffer"/>s and precomputed <see cref="AudioWaveform"/>s — is SHARED by
/// reference (it's never mutated in place), so a snapshot is cheap in time and memory. Instruments and
/// effects are rebuilt from the registries and have their parameter values + custom state copied across;
/// automation lanes are re-bound to the cloned track via the same logic project load uses.
/// </summary>
public static class ProjectCloner
{
    public static Project Clone(Project src, IInstrumentRegistry instruments, IEffectRegistry effects)
    {
        var dst = new Project
        {
            Name = src.Name,
            Tempo = src.Tempo,                 // readonly record struct — value copy
            TimeSignature = src.TimeSignature, // ditto
            BarCount = src.BarCount
        };

        foreach (var t in src.Tracks)
            dst.Tracks.Add(CloneTrack(t, instruments, effects, dst));

        // MIDI mappings re-point to the cloned owner track (matched by preserved Id); the runtime target
        // is left null and rebuilt from the binding by the mapping service after the snapshot is restored.
        foreach (var m in src.MidiMappings)
        {
            var owner = FindTrack(dst, m.Owner.Id);
            if (owner is null) continue;
            dst.MidiMappings.Add(new MidiMapping
            {
                Owner = owner,
                Channel = m.Channel,
                Controller = m.Controller,
                Binding = m.Binding,
            });
        }

        return dst;
    }

    private static Track? FindTrack(Project p, System.Guid id)
    {
        foreach (var t in p.Tracks)
            if (t.Id == id) return t;
        return null;
    }

    private static Track CloneTrack(Track s, IInstrumentRegistry instruments, IEffectRegistry effects, Project dst)
    {
        var t = new Track
        {
            Id = s.Id, // preserve identity so selection can be re-resolved after a restore
            Name = s.Name,
            Kind = s.Kind,
            ParentId = s.ParentId,
            IsMuted = s.IsMuted,
            IsSoloed = s.IsSoloed,
            IsArmed = s.IsArmed,
            Volume = s.Volume,
            Pan = s.Pan,
            ColorKey = s.ColorKey,
            AutomationCollapsed = s.AutomationCollapsed,
            GroupCollapsed = s.GroupCollapsed
        };

        // Clone the instrument rack: a fresh instrument + its own effect chain per slot.
        foreach (var srcSlot in s.Instruments)
        {
            var inst = CloneComponent(srcSlot.Instrument, instruments.Create(srcSlot.Instrument.TypeId));
            var slot = new InstrumentSlot(inst) { Enabled = srcSlot.Enabled };
            foreach (var fx in srcSlot.Effects) slot.Effects.Add((IAudioEffect)CloneComponent(fx, effects.Create(fx.TypeId)));
            slot.CommitEffects();
            t.Instruments.Add(slot);
        }

        foreach (var clip in s.Clips) t.Clips.Add(CloneClip(clip));
        foreach (var fx in s.Effects) t.Effects.Add((IAudioEffect)CloneComponent(fx, effects.Create(fx.TypeId)));
        foreach (var lane in s.AutoLanes)
            if (CloneAutoLane(lane, t, dst) is { } cloned)
                t.AutoLanes.Add(cloned);

        t.CommitInstruments();
        t.CommitEffects();
        t.CommitAutoLanes();
        return t;
    }

    private static Clip CloneClip(Clip s)
    {
        var c = new Clip
        {
            Id = s.Id,
            Name = s.Name,
            StartBeat = s.StartBeat,
            LengthBeats = s.LengthBeats,
            AudioFilePath = s.AudioFilePath,
            Waveform = s.Waveform, // immutable — shared by reference
            Samples = s.Samples,   // immutable PCM — shared by reference (keeps snapshots cheap)
            SourceTempo = s.SourceTempo,
            StretchToTempo = s.StretchToTempo,
            PitchCorrected = s.PitchCorrected,
            IsAudio = s.IsAudio,
            SourceOffsetSeconds = s.SourceOffsetSeconds,
            SourceLengthSeconds = s.SourceLengthSeconds
        };

        foreach (var n in s.Notes)
            c.Notes.Add(new MidiNote { Note = n.Note, StartBeat = n.StartBeat, LengthBeats = n.LengthBeats, Velocity = n.Velocity });

        return c;
    }

    // Rebuilds the automation lane against the cloned track, re-binding its delegate target from the
    // serializable Binding (same approach as project load). Lanes without a binding can't be re-bound.
    private static AutomationLane? CloneAutoLane(AutomationLane s, Track clonedTrack, Project clonedProject)
    {
        if (s.Binding is not { } b) return null;
        var target = ProjectFile.BuildTarget(clonedTrack, (int)b.Kind, b.EffectIndex, b.ParamIndex, clonedProject);
        if (target is null) return null;

        var lane = new AutomationLane(target) { IsArmed = s.IsArmed, Binding = b };
        foreach (var p in s.Points) lane.Points.Add(new AutomationPoint(p.Beat, p.Value, p.Curve));
        lane.Sort();
        return lane;
    }

    // Copies the live parameter values + any extra (IProjectStatefulComponent) state from src onto a freshly
    // created component of the same type. Works for both instruments and effects (same Parameters contract).
    private static T CloneComponent<T>(T src, T dst) where T : class
    {
        CopyParameters(GetParameters(src), GetParameters(dst));

        // Heavy in-memory state (e.g. the SFZ sampler's decoded library) is shared by reference rather
        // than serialized — the serialize path would re-read it from disk, freezing the UI on every edit.
        if (src is IRuntimeCloneable && dst is IRuntimeCloneable dr)
            dr.CopyRuntimeStateFrom(src);
        else
            CopyCustomState(src, dst);

        if (src is ISampleHost sh && dst is ISampleHost dh && sh.CurrentSample is { } buf)
            dh.LoadSample(buf, sh.SampleName ?? ""); // share the (immutable) sample buffer by reference

        return dst;
    }

    private static System.Collections.Generic.IReadOnlyList<Parameter> GetParameters(object component) => component switch
    {
        IInstrument i => i.Parameters,
        IAudioEffect e => e.Parameters,
        _ => System.Array.Empty<Parameter>()
    };

    private static void CopyParameters(System.Collections.Generic.IReadOnlyList<Parameter> from,
        System.Collections.Generic.IReadOnlyList<Parameter> to)
    {
        // Same type => identical parameter list shape, matched by index.
        var n = System.Math.Min(from.Count, to.Count);
        for (var i = 0; i < n; i++)
        {
            switch (from[i])
            {
                case FloatParameter f when to[i] is FloatParameter df: df.Value = f.Value; break;
                case BoolParameter b when to[i] is BoolParameter db: db.Value = b.Value; break;
                case ChoiceParameter c when to[i] is ChoiceParameter dc: dc.SelectedIndex = c.SelectedIndex; break;
            }
        }
    }

    private static void CopyCustomState(object src, object dst)
    {
        if (src is not IProjectStatefulComponent ss || dst is not IProjectStatefulComponent ds) return;

        using var ms = new MemoryStream();
        using (var w = new OngenWriter(ms)) ss.WriteProjectState(w);
        ms.Position = 0;
        using var r = new OngenReader(ms);
        ds.ReadProjectState(r);
    }
}
