using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>
/// Renders a whole project to a WAV file offline (faster than real time), using the same mixing
/// maths as the live <see cref="AudioEngine"/>. It works on <b>clones</b> of each track's instrument
/// and effects, so rendering never disturbs live playback or shares voice/effect state.
/// </summary>
public sealed class OfflineRenderer
{
    private const int BlockFrames = 512;
    private const double TailSeconds = 2.0; // let instrument/effect tails ring out

    /// <summary>Renders the project to <paramref name="path"/> as a 16-bit PCM WAV.</summary>
    public void RenderToWav(Project project, AudioFormat format, double bpm, string path)
    {
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var sampleRate = format.SampleRate;
        var samplesPerBeat = bpm > 0 ? sampleRate * 60.0 / bpm : sampleRate;
        var beatsPerBar = Math.Max(1, project.TimeSignature.Numerator);

        // Render at least the arrangement length, extended to cover any clips beyond it.
        var contentEnd = project.Tracks.SelectMany(t => t.Clips).Select(c => c.EndBeat).DefaultIfEmpty(0).Max();
        var renderBeats = Math.Max(project.BarCount * beatsPerBar, contentEnd);
        var totalFrames = (long)(renderBeats * samplesPerBeat) + (long)(TailSeconds * sampleRate);

        var soloActive = project.Tracks.Any(t => t.IsSoloed);

        // Build per-track render state from clones + the merged, sorted note schedule.
        var renderTracks = new List<RenderTrack>();
        var events = new List<ScheduledNote>();
        foreach (var track in project.Tracks)
        {
            var rt = new RenderTrack(track);

            // Clone the effects up front so the note schedule can target the cloned MIDI-aware ones.
            rt.Effects = track.Effects.Select(e => { var c = e.Clone(); c.Prepare(format); return c; }).ToArray();
            var midiFx = MidiEffectsOf(rt.Effects);

            if (track.Kind == TrackKind.Instrument && track.Instruments.Count > 0)
            {
                foreach (var s in track.Instruments)
                {
                    var inst = s.Instrument.Clone();
                    inst.Prepare(format);
                    var slotFx = s.Effects.Select(e => { var c = e.Clone(); c.Prepare(format); return c; }).ToArray();
                    rt.Slots.Add(new RenderSlot(inst, s.Enabled, slotFx));
                }

                var slots = rt.Slots.ToArray();
                foreach (var clip in track.Clips.Where(c => c.IsMidi))
                {
                    foreach (var note in clip.Notes)
                    {
                        var onBeat = clip.StartBeat + note.StartBeat;
                        events.Add(new ScheduledNote(onBeat, onBeat + note.LengthBeats, slots, midiFx, note.Note, note.Velocity));
                    }
                }
            }
            else if (midiFx.Length > 0 && track.Kind != TrackKind.Audio)
            {
                foreach (var clip in track.Clips.Where(c => c.IsMidi))
                {
                    foreach (var note in clip.Notes)
                    {
                        var onBeat = clip.StartBeat + note.StartBeat;
                        events.Add(new ScheduledNote(onBeat, onBeat + note.LengthBeats, null, midiFx, note.Note, note.Velocity));
                    }
                }
            }

            if (track.Kind == TrackKind.Audio)
            {
                foreach (var clip in track.Clips)
                {
                    if (clip.Samples is not { } samples) continue;
                    // A sliced clip spans only part of the source (SourceLengthSeconds); stretch off that
                    // window so a tempo-synced piece plays just its portion of the loop.
                    var sourceDur = clip.SourceLengthSeconds
                        ?? Math.Max(0.0, samples.FrameCount / (double)samples.SampleRate - clip.SourceOffsetSeconds);
                    var stretch = clip.StretchToTempo
                        ? TempoSync.Stretch(sourceDur, bpm, clip.LengthBeats)
                        : 1.0;
                    rt.AudioClips.Add((clip.StartBeat, clip.LengthBeats, samples, stretch, clip.SourceOffsetSeconds));
                }
            }

            renderTracks.Add(rt);
        }

        events.Sort((a, b) => a.OnBeat.CompareTo(b.OnBeat));

        // Build the bus graph (mirrors AudioEngine): a RenderBus per group/master, linked to its parent,
        // ordered deepest-first so a block mixes children → groups → master in one pass.
        var trackById = project.Tracks.ToDictionary(t => t.Id);
        var busByTrackId = new Dictionary<Guid, RenderBus>();
        var buses = new List<RenderBus>();
        foreach (var rt in renderTracks)
        {
            if (!rt.Source.IsBus) continue;
            var rb = new RenderBus(rt) { Buffer = new float[BlockFrames * channels] };
            busByTrackId[rt.Source.Id] = rb;
            buses.Add(rb);
        }

        var masterBus = buses.FirstOrDefault(b => b.Track.Source.Kind == TrackKind.Master);
        var masterTrack = masterBus?.Track.Source;
        foreach (var rb in buses)
        {
            rb.Parent = rb.Track.Source.Kind == TrackKind.Master ? null
                : rb.Track.Source.ParentId is { } pid && busByTrackId.TryGetValue(pid, out var p) ? p : masterBus;
        }

        foreach (var rb in buses)
        {
            var d = 0;
            var p = rb.Parent;
            while (p is not null && d < 64) { d++; p = p.Parent; }
            rb.Depth = d;
        }

        buses.Sort((a, b) => b.Depth.CompareTo(a.Depth)); // deepest first

        Track? ParentTrack(Track t)
        {
            if (t.Kind == TrackKind.Master) return null;
            if (t.ParentId is { } id && trackById.TryGetValue(id, out var p)) return p;
            return masterTrack;
        }

        bool AncestorSoloed(Track t)
        {
            var p = ParentTrack(t);
            var n = 0;
            while (p is not null && n++ < 64) { if (p.IsSoloed) return true; p = ParentTrack(p); }
            return false;
        }

        var block = new float[BlockFrames * channels];
        var temp = new float[BlockFrames * channels];
        var slotTemp = new float[BlockFrames * channels];
        var active = new List<ScheduledNote>();
        var nextEvent = 0;
        var currentBeat = 0.0;
        long framesWritten = 0;

        // Mirror the live engine's tempo/sidechain context so tempo-synced + sidechain effects render too.
        var sidechain = new SidechainBus();
        var ctx = new EffectContext { Format = format, Bpm = bpm, Playing = true, Sidechain = sidechain };

        using var writer = new WavWriter(path, channels, sampleRate);

        while (framesWritten < totalFrames)
        {
            var framesThisBlock = (int)Math.Min(BlockFrames, totalFrames - framesWritten);
            var sampleCount = framesThisBlock * channels;
            var blockSpan = block.AsSpan(0, sampleCount);
            blockSpan.Clear();

            var prevBeat = currentBeat;
            currentBeat = prevBeat + framesThisBlock / samplesPerBeat;
            ctx.PlayheadBeats = prevBeat;

            // Fire note on/off for this block.
            while (nextEvent < events.Count && events[nextEvent].OnBeat < currentBeat)
            {
                var ev = events[nextEvent++];
                ev.Fire(on: true);
                active.Add(ev);
            }

            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].OffBeat <= currentBeat)
                {
                    active[i].Fire(on: false);
                    active.RemoveAt(i);
                }
            }

            foreach (var rb in buses) Array.Clear(rb.Buffer, 0, sampleCount);

            // 1) Content tracks: render → effects → strip → sum into their parent bus.
            foreach (var rt in renderTracks)
            {
                if (rt.Source.IsBus) continue;
                if (rt.Source.IsMuted || (soloActive && !(rt.Source.IsSoloed || AncestorSoloed(rt.Source)))) continue;

                var tempSpan = temp.AsSpan(0, sampleCount);
                tempSpan.Clear();
                var hasContent = false;

                if (rt.Slots.Count > 0)
                {
                    var slotSpan = slotTemp.AsSpan(0, sampleCount);
                    foreach (var slot in rt.Slots)
                    {
                        if (!slot.Enabled) continue;
                        slotSpan.Clear();
                        slot.Instrument.Render(slotSpan);
                        foreach (var fx in slot.Effects)
                        {
                            if (!fx.Enabled) continue;
                            if (fx is IContextualEffect cae) cae.SetContext(ctx);
                            fx.Process(slotSpan);
                        }

                        for (var i = 0; i < slotSpan.Length; i++) tempSpan[i] += slotSpan[i];
                        hasContent = true;
                    }
                }
                else
                {
                    foreach (var (start, length, samples, stretch, sourceOffset) in rt.AudioClips)
                    {
                        Mixing.RenderAudioClip(tempSpan, samples, start, length, prevBeat, samplesPerBeat, sampleRate, channels, stretch, sourceOffset);
                        hasContent = true;
                    }
                }

                if (rt.Effects.Length > 0)
                {
                    foreach (var fx in rt.Effects)
                    {
                        if (!fx.Enabled) continue;
                        if (fx is IContextualEffect cae) cae.SetContext(ctx);
                        fx.Process(tempSpan);
                    }

                    hasContent = true;
                }

                if (sidechain.IsRequested(rt.Source.Id)) sidechain.Publish(rt.Source.Id, tempSpan, channels);

                if (!hasContent) continue;

                var parent = rt.Source.ParentId is { } pid && busByTrackId.TryGetValue(pid, out var pb) ? pb : masterBus;
                var target = parent is not null ? parent.Buffer.AsSpan(0, sampleCount) : blockSpan;
                var (lg, rg) = Mixing.StripGains(rt.Source.Volume, rt.Source.Pan);
                MixIntoBlock(target, tempSpan, lg, rg, channels, framesThisBlock);
            }

            // 2) Buses deepest-first: effects on the summed input → strip → into parent (master → block).
            foreach (var rb in buses)
            {
                var bt = rb.Track.Source;
                if (bt.IsMuted) continue;

                var busSpan = rb.Buffer.AsSpan(0, sampleCount);
                if (rb.Track.Effects.Length > 0)
                {
                    foreach (var fx in rb.Track.Effects)
                    {
                        if (!fx.Enabled) continue;
                        if (fx is IContextualEffect cae) cae.SetContext(ctx);
                        fx.Process(busSpan);
                    }
                }

                if (sidechain.IsRequested(rb.Track.Source.Id)) sidechain.Publish(rb.Track.Source.Id, busSpan, channels);

                var target = rb.Parent is not null ? rb.Parent.Buffer.AsSpan(0, sampleCount) : blockSpan;
                var (lg, rg) = Mixing.BusGains(bt.Volume, bt.Pan);
                MixIntoBlock(target, busSpan, lg, rg, channels, framesThisBlock);
            }

            writer.Write(blockSpan);
            framesWritten += framesThisBlock;
        }
    }

    private static void MixIntoBlock(Span<float> target, Span<float> source, float leftGain, float rightGain,
        int channels, int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                target[i + c] += source[i + c] * Mixing.ChannelGain(c, leftGain, rightGain);
            }
        }
    }

    private sealed class RenderTrack
    {
        public RenderTrack(Track source) => Source = source;
        public Track Source { get; }
        public List<RenderSlot> Slots { get; } = new();
        public IAudioEffect[] Effects { get; set; } = Array.Empty<IAudioEffect>();
        public List<(double Start, double Length, AudioSampleBuffer Samples, double Stretch, double SourceOffset)> AudioClips { get; } = new();
    }

    // A cloned instrument-rack slot for offline render: the instrument, its bypass flag, and its own
    // (cloned) effect chain processed before the track-level effects.
    private sealed class RenderSlot
    {
        public RenderSlot(IInstrument instrument, bool enabled, IAudioEffect[] effects)
        {
            Instrument = instrument;
            Enabled = enabled;
            Effects = effects;
        }

        public IInstrument Instrument { get; }
        public bool Enabled { get; }
        public IAudioEffect[] Effects { get; }
    }

    private sealed class RenderBus
    {
        public RenderBus(RenderTrack track) => Track = track;
        public RenderTrack Track { get; }
        public RenderBus? Parent { get; set; }
        public float[] Buffer { get; set; } = Array.Empty<float>();
        public int Depth { get; set; }
    }

    // The track's MIDI-aware effects (cloned), for delivering gesture-triggering notes during render.
    private static IMidiAwareEffect[] MidiEffectsOf(IAudioEffect[] effects)
    {
        var aware = effects.OfType<IMidiAwareEffect>().ToArray();
        return aware;
    }

    private readonly record struct ScheduledNote(double OnBeat, double OffBeat, RenderSlot[]? Slots,
        IMidiAwareEffect[] MidiEffects, int Note, float Velocity)
    {
        public void Fire(bool on)
        {
            if (Slots is not null)
            {
                foreach (var slot in Slots)
                {
                    if (!slot.Enabled) continue;
                    if (on) slot.Instrument.NoteOn(Note, Velocity);
                    else slot.Instrument.NoteOff(Note);
                }
            }

            if (MidiEffects.Length == 0) return;
            var vel = (byte)Math.Clamp((int)(Velocity * 127f), 0, 127);
            var msg = new MidiMessage(on ? MidiMessageKind.NoteOn : MidiMessageKind.NoteOff, 0, (byte)Note, on ? vel : (byte)0);
            foreach (var fx in MidiEffects) fx.HandleMidi(msg);
        }
    }
}
