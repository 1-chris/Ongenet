using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Scheduling;

/// <summary>Linear arrangement scheduler — timeline clips only (current default behaviour).</summary>
public sealed class ArrangementScheduler : IPlaybackScheduler
{
    public PlaybackSchedule Build(PlaybackScheduleContext context)
    {
        var notes = new List<ScheduledNoteEvent>();
        var controlChanges = new List<ScheduledControlChangeEvent>();
        var clips = new List<ScheduledAudioClip>();
        var startBeat = context.StartBeat;
        var channels = context.Channels < 1 ? 1 : context.Channels;
        var sampleRate = context.SampleRate;
        var groove = context.Project.ActiveGroove;

        foreach (var track in context.Tracks)
        {
            var midiAwareFx = MidiAwareEffectsOf(track);
            var midiFxChain = new MidiEffectChain(track.ActiveMidiEffects);
            if (track.Kind == TrackKind.Instrument && track.ActiveInstruments.Length > 0)
            {
                EmitMidiForTrack(context, track, notes, controlChanges, startBeat, groove, track.ActiveInstruments);
            }
            else if (track.Kind == TrackKind.Hybrid && track.ActiveInstruments.Length > 0)
            {
                EmitMidiForTrack(context, track, notes, controlChanges, startBeat, groove, track.ActiveInstruments);
            }
            else if ((!midiFxChain.IsEmpty || midiAwareFx.Length > 0) && track.Kind != TrackKind.Audio)
            {
                foreach (var clip in TakeLanePlayback.ActiveClips(track))
                {
                    if (!clip.IsMidi) continue;
                    EmitControlChanges(controlChanges, clip, track, null, startBeat, groove);
                    foreach (var note in clip.Notes)
                    {
                        var onBeat = GrooveMath.Apply(clip.StartBeat + note.StartBeat, groove);
                        var offBeat = onBeat + note.LengthBeats;
                        if (offBeat <= startBeat) continue;
                        var (mappedNote, mappedVel) = DrumMapProcessor.Apply(context.Project, track, note.Note, note.Velocity);

                        if (midiFxChain.IsEmpty)
                        {
                            notes.Add(new ScheduledNoteEvent(track.Id, onBeat, offBeat, null, midiAwareFx,
                                mappedNote, mappedVel));
                        }
                        else
                        {
                            foreach (var expanded in midiFxChain.ExpandNote(onBeat, offBeat, mappedNote, mappedVel))
                            {
                                if (expanded.OffBeat <= startBeat) continue;
                                notes.Add(new ScheduledNoteEvent(track.Id, expanded.OnBeat, expanded.OffBeat, null,
                                    midiAwareFx, expanded.Note, expanded.Velocity));
                            }
                        }
                    }
                }
            }

            if (track.Kind == TrackKind.Audio || track.Kind == TrackKind.Hybrid)
            {
                var activeClips = TakeLanePlayback.ActiveClips(track).ToList();
                var fades = Crossfade.Compute(activeClips);
                foreach (var clip in activeClips)
                {
                    if (clip.Samples is not null && clip.EndBeat > startBeat)
                    {
                        var prepared = ClipPlaybackSource.Prepare(clip, context.Bpm);
                        var fade = fades.TryGetValue(clip, out var f) ? f : (FadeInBeats: 0.0, FadeOutBeats: 0.0);
                        var shifters = AudioClipPitch.CreateShiftersIfNeeded(clip, prepared.Warp, channels, sampleRate);
                        clips.Add(new ScheduledAudioClip
                        {
                            Track = track,
                            StartBeat = clip.StartBeat,
                            LengthBeats = clip.LengthBeats,
                            Samples = prepared.Samples,
                            StretchToTempo = prepared.StretchToTempo,
                            SourceDurSeconds = prepared.SourceDurSeconds,
                            SourceOffsetSeconds = clip.SourceOffsetSeconds,
                            FadeInBeats = fade.FadeInBeats,
                            FadeOutBeats = fade.FadeOutBeats,
                            PitchShifters = shifters,
                            Warp = prepared.Warp,
                            WarpMode = clip.WarpMode,
                            PitchCorrected = clip.PitchCorrected,
                            AraPitchOffsetSemitones = clip.AraPitchOffsetSemitones,
                            PitchSegments = clip.PitchSegments
                        });
                    }
                }
            }
        }

        notes.Sort((a, b) => a.OnBeat.CompareTo(b.OnBeat));
        controlChanges.Sort((a, b) => a.Beat.CompareTo(b.Beat));

        var beatsPerBar = Math.Max(1, context.Project.TimeSignature.Numerator);
        var contentEnd = 0.0;
        foreach (var track in context.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.EndBeat > contentEnd) contentEnd = clip.EndBeat;
            }
        }

        return new PlaybackSchedule
        {
            Notes = notes.ToArray(),
            ControlChanges = controlChanges.ToArray(),
            AudioClips = clips.ToArray(),
            ArrangementEndBeat = Math.Max(context.Project.BarCount * (double)beatsPerBar, contentEnd)
        };
    }

    private static void EmitMidiForTrack(PlaybackScheduleContext context, Track track,
        List<ScheduledNoteEvent> notes, List<ScheduledControlChangeEvent> controlChanges,
        double startBeat, GrooveTemplate? groove, InstrumentSlot[] slots)
    {
        var midiAwareFx = MidiAwareEffectsOf(track);
        var midiFxChain = new MidiEffectChain(track.ActiveMidiEffects);
        foreach (var clip in TakeLanePlayback.ActiveClips(track))
        {
            if (!clip.IsMidi) continue;
            EmitControlChanges(controlChanges, clip, track, slots, startBeat, groove);
            foreach (var note in clip.Notes)
            {
                var onBeat = GrooveMath.Apply(clip.StartBeat + note.StartBeat, groove);
                var offBeat = onBeat + note.LengthBeats;
                if (offBeat <= startBeat) continue;
                var (mappedNote, mappedVel) = DrumMapProcessor.Apply(context.Project, track, note.Note, note.Velocity);

                if (midiFxChain.IsEmpty)
                {
                    notes.Add(new ScheduledNoteEvent(track.Id, onBeat, offBeat, slots, midiAwareFx,
                        mappedNote, mappedVel));
                }
                else
                {
                    foreach (var expanded in midiFxChain.ExpandNote(onBeat, offBeat, mappedNote, mappedVel))
                    {
                        if (expanded.OffBeat <= startBeat) continue;
                        notes.Add(new ScheduledNoteEvent(track.Id, expanded.OnBeat, expanded.OffBeat, slots,
                            midiAwareFx, expanded.Note, expanded.Velocity));
                    }
                }
            }
        }
    }

    private static void EmitControlChanges(
        List<ScheduledControlChangeEvent> controlChanges,
        Clip clip,
        Track track,
        InstrumentSlot[]? slots,
        double startBeat,
        GrooveTemplate? groove)
    {
        foreach (var cc in clip.ControlChanges)
        {
            var beat = GrooveMath.Apply(clip.StartBeat + cc.StartBeat, groove);
            if (beat <= startBeat) continue;
            controlChanges.Add(new ScheduledControlChangeEvent(
                track.Id, beat, slots, cc.Controller, cc.Value));
        }
    }

    private static IMidiAwareEffect[] MidiAwareEffectsOf(Track track)
    {
        var list = new List<IMidiAwareEffect>();
        foreach (var fx in track.ActiveEffects)
            if (fx is IMidiAwareEffect m) list.Add(m);
        return list.ToArray();
    }

    private static PitchShifter[] BuildPitchShifters(int channels, int sampleRate)
    {
        var shifters = new PitchShifter[channels];
        for (var i = 0; i < channels; i++)
        {
            shifters[i] = new PitchShifter();
            shifters[i].Configure(sampleRate);
        }

        return shifters;
    }
}
