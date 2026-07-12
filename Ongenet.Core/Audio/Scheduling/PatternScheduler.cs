using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Scheduling;

/// <summary>Expands playlist <see cref="PatternClip"/> blocks into scheduled MIDI notes.</summary>
public sealed class PatternScheduler : IPlaybackScheduler
{
    public PlaybackSchedule Build(PlaybackScheduleContext context)
    {
        var notes = new List<ScheduledNoteEvent>();
        var startBeat = context.StartBeat;
        var groove = context.Project.ActiveGroove;
        var patternById = context.Project.Patterns.ToDictionary(p => p.Id);
        var trackById = context.Tracks.ToDictionary(t => t.Id);

        foreach (var pc in context.Project.PatternClips)
        {
            if (pc.LengthBeats <= 0 || pc.StartBeat + pc.LengthBeats <= startBeat) continue;
            if (!patternById.TryGetValue(pc.PatternId, out var pattern)) continue;
            if (!trackById.TryGetValue(pc.TrackId, out var hostTrack)) continue;
            if (hostTrack.Kind != TrackKind.Pattern) continue;

            var seqByChannel = pattern.StepSequences.ToDictionary(s => s.PatternChannelId);
            var patternRepeats = pc.LengthBeats / Math.Max(1e-9, pattern.LengthBeats);
            var repeatCount = patternRepeats < 1 ? 1 : (int)Math.Ceiling(patternRepeats);

            for (var rep = 0; rep < repeatCount; rep++)
            {
                var repOffset = rep * pattern.LengthBeats;
                if (repOffset >= pc.LengthBeats) break;

                foreach (var channel in pattern.OrderedChannels)
                {
                    if (channel.Muted) continue;
                    if (!trackById.TryGetValue(channel.TrackId, out var track)) continue;
                    if (track.Kind != TrackKind.Instrument || track.ActiveInstruments.Length == 0) continue;
                    if (!seqByChannel.TryGetValue(channel.Id, out var seq)) continue;

                    var stepCount = Math.Max(1, seq.StepCount > 0 ? seq.StepCount : seq.Steps.Count);
                    var stepBeats = pattern.LengthBeats / stepCount;
                    var slots = track.ActiveInstruments;
                    var midiFxChain = new MidiEffectChain(track.ActiveMidiEffects);
                    var midiAwareFx = MidiEffectsOf(track);

                    for (var i = 0; i < seq.Steps.Count && i < stepCount; i++)
                    {
                        var step = seq.Steps[i];
                        if (!step.Active) continue;
                        if (step.Probability < 1f && Random.Shared.NextDouble() > step.Probability) continue;

                        var relBeat = i * stepBeats;
                        if (step.MicroTimingTicks != 0)
                        {
                            const double ppq = 480.0;
                            relBeat += step.MicroTimingTicks / ppq * stepBeats;
                        }

                        var onBeat = GrooveMath.Apply(pc.StartBeat + repOffset + relBeat, groove);
                        if (onBeat >= pc.StartBeat + pc.LengthBeats) continue;
                        var offBeat = onBeat + stepBeats;
                        if (offBeat <= startBeat) continue;

                        var velocity = (float)Math.Clamp(step.Velocity * channel.Volume, 0f, 1f);
                        var stepPan = Math.Clamp(step.Pan, -1f, 1f);
                        var (mappedNote, mappedVel) = DrumMapProcessor.Apply(context.Project, track, step.Note, velocity);

                        if (midiFxChain.IsEmpty)
                        {
                            notes.Add(new ScheduledNoteEvent(track.Id, onBeat, offBeat, slots, midiAwareFx, mappedNote, mappedVel, 1f, stepPan));
                        }
                        else
                        {
                            foreach (var expanded in midiFxChain.ExpandNote(onBeat, offBeat, mappedNote, mappedVel))
                            {
                                if (expanded.OffBeat <= startBeat) continue;
                                notes.Add(new ScheduledNoteEvent(track.Id, expanded.OnBeat, expanded.OffBeat, slots,
                                    midiAwareFx, expanded.Note, expanded.Velocity, 1f, stepPan));
                            }
                        }
                    }
                }
            }
        }

        notes.Sort((a, b) => a.OnBeat.CompareTo(b.OnBeat));
        return new PlaybackSchedule { Notes = notes.ToArray() };
    }

    private static IMidiAwareEffect[] MidiEffectsOf(Track track)
    {
        var list = new List<IMidiAwareEffect>();
        foreach (var fx in track.ActiveEffects)
            if (fx is IMidiAwareEffect m) list.Add(m);
        return list.ToArray();
    }
}
