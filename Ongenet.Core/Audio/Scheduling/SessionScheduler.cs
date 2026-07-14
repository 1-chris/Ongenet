using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Audio.Scheduling;

/// <summary>Session clip launch modes (Ableton-style).</summary>
public enum SessionLaunchMode
{
    Trigger,
    Gate,
    Toggle,
    Repeat
}

/// <summary>What happens when a one-shot session clip finishes playing.</summary>
public enum FollowAction
{
    Stop,
    PlayNext,
    PlayPrevious,
    PlayRandom,
    PlayFirst,
    PlayAgain
}

/// <summary>A clip slot in the session view grid.</summary>
public sealed class SessionClip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TrackId { get; set; }
    public int SceneIndex { get; set; }
    public string Name { get; set; } = "Clip";
    public double LengthBeats { get; set; } = 4;
    public SessionLaunchMode LaunchMode { get; set; } = SessionLaunchMode.Trigger;
    public FollowAction FollowAction { get; set; } = FollowAction.Stop;
    /// <summary>Per-clip launch quantize in beats; 0 uses the project default.</summary>
    public double LaunchQuantizeBeats { get; set; }
    public Guid? SourceClipId { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsQueued { get; set; }
}

/// <summary>Schedules launched session clips (session view only — no arrangement timeline).</summary>
public sealed class SessionScheduler : IPlaybackScheduler
{
    private readonly IReadOnlyList<SessionClip> _sessionClips;
    private readonly IReadOnlyDictionary<Guid, SessionClipLaunchState> _launches;

    public SessionScheduler(
        IReadOnlyList<SessionClip> sessionClips,
        IReadOnlyDictionary<Guid, SessionClipLaunchState>? launches = null)
    {
        _sessionClips = sessionClips;
        _launches = launches ?? new Dictionary<Guid, SessionClipLaunchState>();
    }

    public PlaybackSchedule Build(PlaybackScheduleContext context)
    {
        var notes = new List<ScheduledNoteEvent>();
        var clips = new List<ScheduledAudioClip>();
        var startBeat = context.StartBeat;
        var channels = context.Channels < 1 ? 1 : context.Channels;
        var sampleRate = context.SampleRate;
        var trackById = context.Tracks.ToDictionary(t => t.Id);
        var sessionById = _sessionClips.ToDictionary(c => c.Id);
        var horizon = ScheduleHorizon(context, _launches);

        foreach (var launch in _launches.Values)
        {
            if (!sessionById.TryGetValue(launch.Clip.Id, out var sc)) continue;
            if (!trackById.TryGetValue(sc.TrackId, out var track)) continue;
            if (sc.SourceClipId is not { } srcId) continue;
            var src = track.Clips.FirstOrDefault(c => c.Id == srcId);
            if (src is null) continue;

            var loop = launch.Looping || sc.LaunchMode == SessionLaunchMode.Repeat;
            var iteration = 0;
            var baseLaunch = launch.LaunchBeat;

            while (baseLaunch < horizon)
            {
                if (baseLaunch + sc.LengthBeats <= startBeat && !loop) break;
                if (baseLaunch + sc.LengthBeats <= startBeat && loop)
                {
                    iteration++;
                    baseLaunch = launch.LaunchBeat + iteration * sc.LengthBeats;
                    continue;
                }

                ScheduleSourceClip(notes, clips, track, src, sc, baseLaunch, startBeat, channels, sampleRate,
                    context.Bpm, context.Project, context.Project.ActiveGroove);

                if (!loop) break;
                iteration++;
                baseLaunch = launch.LaunchBeat + iteration * sc.LengthBeats;
            }
        }

        notes.Sort((a, b) => (a.OnBeat + a.TimingOffsetBeats).CompareTo(b.OnBeat + b.TimingOffsetBeats));
        return new PlaybackSchedule
        {
            Notes = notes.ToArray(),
            AudioClips = clips.ToArray(),
            ArrangementEndBeat = horizon
        };
    }

    internal static void ScheduleSourceClip(
        List<ScheduledNoteEvent> notes,
        List<ScheduledAudioClip> clips,
        Track track,
        Clip src,
        SessionClip sc,
        double launchBeat,
        double startBeat,
        int channels,
        int sampleRate,
        double bpm,
        Project project,
        GrooveTemplate? groove)
    {
        if (src.IsMidi && track.Kind == TrackKind.Instrument)
        {
            var slots = track.ActiveInstruments;
            var midiAwareFx = MidiEffectsOf(track);
            var midiFxChain = new MidiEffectChain(track.ActiveMidiEffects);
            var sources = new List<MidiSourceNote>();
            foreach (var note in src.Notes)
            {
                var relOn = note.StartBeat;
                var relOff = relOn + note.LengthBeats;
                if (relOn >= sc.LengthBeats) continue;
                if (relOff > sc.LengthBeats) relOff = sc.LengthBeats;

                var onBeat = GrooveMath.Apply(launchBeat + relOn, groove);
                var offBeat = onBeat + (relOff - relOn);
                if (offBeat <= startBeat) continue;
                var (mappedNote, mappedVel) = DrumMapProcessor.Apply(project, track, note.Note, note.Velocity);
                sources.Add(new MidiSourceNote(onBeat, offBeat, mappedNote, mappedVel, note.HumanizeTicks));
            }

            if (midiFxChain.IsEmpty)
            {
                foreach (var srcNote in sources)
                {
                    var noteSlots = InstrumentRackRouting.ResolveSlots(track, slots, srcNote.Note);
                    if (noteSlots is null || srcNote.OffBeat <= startBeat) continue;
                    notes.Add(MidiFxScheduleHelper.ToEvent(track.Id, srcNote, noteSlots, midiAwareFx));
                }
            }
            else
            {
                foreach (var expanded in midiFxChain.ExpandNotes(sources, bpm))
                {
                    if (expanded.OffBeat <= startBeat) continue;
                    var noteSlots = InstrumentRackRouting.ResolveSlots(track, slots, expanded.Note);
                    if (noteSlots is null) continue;
                    notes.Add(MidiFxScheduleHelper.ToEvent(track.Id, expanded, noteSlots, midiAwareFx));
                }
            }
        }
        else if (src.IsAudio && src.Samples is not null)
        {
            var prepared = ClipPlaybackSource.Prepare(src, bpm);
            var shifters = AudioClipPitch.CreateShiftersIfNeeded(src, prepared.Warp, channels, sampleRate);
            clips.Add(new ScheduledAudioClip
            {
                Track = track,
                StartBeat = launchBeat,
                LengthBeats = sc.LengthBeats,
                Samples = prepared.Samples,
                StretchToTempo = prepared.StretchToTempo,
                SourceDurSeconds = prepared.SourceDurSeconds,
                SourceOffsetSeconds = src.SourceOffsetSeconds,
                FadeInBeats = 0,
                FadeOutBeats = 0,
                PitchShifters = shifters,
                Warp = prepared.Warp,
                WarpMode = src.WarpMode,
                PitchCorrected = src.PitchCorrected,
                AraPitchOffsetSemitones = src.AraPitchOffsetSemitones,
                PitchSegments = src.PitchSegments
            });
        }
    }

    /// <summary>
    /// Returns the session clip to launch when <paramref name="clip"/> ends, or null when the follow
    /// action is <see cref="FollowAction.Stop"/> or the clip should keep playing (repeat/toggle/gate).
    /// </summary>
    public static SessionClip? ResolveFollowTarget(SessionClip clip, IReadOnlyList<SessionClip> allClips,
        Random? random = null)
    {
        if (clip.LaunchMode is SessionLaunchMode.Repeat or SessionLaunchMode.Toggle or SessionLaunchMode.Gate)
            return null;
        if (clip.FollowAction == FollowAction.Stop) return null;

        var onTrack = allClips.Where(c => c.TrackId == clip.TrackId).ToList();
        if (onTrack.Count == 0) return null;

        return clip.FollowAction switch
        {
            FollowAction.PlayAgain => clip,
            FollowAction.PlayFirst => onTrack.MinBy(c => c.SceneIndex),
            FollowAction.PlayNext => onTrack.Where(c => c.SceneIndex > clip.SceneIndex).MinBy(c => c.SceneIndex),
            FollowAction.PlayPrevious => onTrack.Where(c => c.SceneIndex < clip.SceneIndex).MaxBy(c => c.SceneIndex),
            FollowAction.PlayRandom => onTrack[random?.Next(onTrack.Count) ?? 0],
            _ => null
        };
    }

    /// <summary>True when a non-looping launch has reached its natural end at <paramref name="playheadBeat"/>.</summary>
    public static bool HasClipEnded(SessionClip clip, double launchBeat, double playheadBeat)
    {
        if (clip.LaunchMode == SessionLaunchMode.Repeat) return false;
        return playheadBeat >= launchBeat + clip.LengthBeats - 1e-9;
    }

    private static double ScheduleHorizon(
        PlaybackScheduleContext context,
        IReadOnlyDictionary<Guid, SessionClipLaunchState> launches)
    {
        var beatsPerBar = Math.Max(1, context.Project.TimeSignature.Numerator);
        var contentEnd = 0.0;
        foreach (var track in context.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.EndBeat > contentEnd) contentEnd = clip.EndBeat;
            }
        }

        foreach (var launch in launches.Values)
        {
            var end = launch.LaunchBeat + launch.Clip.LengthBeats;
            if (launch.Looping) end = Math.Max(end, context.StartBeat + beatsPerBar * 64);
            if (end > contentEnd) contentEnd = end;
        }

        return Math.Max(context.Project.BarCount * (double)beatsPerBar, contentEnd);
    }

    private static IMidiAwareEffect[] MidiEffectsOf(Track track)
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

/// <summary>Combines arrangement timeline with live session overdub.</summary>
public sealed class HybridScheduler : IPlaybackScheduler
{
    private readonly SessionScheduler _session;

    public HybridScheduler(
        IReadOnlyList<SessionClip> sessionClips,
        IReadOnlyDictionary<Guid, SessionClipLaunchState>? launches = null)
        => _session = new SessionScheduler(sessionClips, launches);

    public PlaybackSchedule Build(PlaybackScheduleContext context)
    {
        var arrangement = new ArrangementScheduler().Build(context);
        var session = _session.Build(context);
        return MergeSchedules(arrangement, session);
    }

    internal static PlaybackSchedule MergeSchedules(PlaybackSchedule baseSchedule, PlaybackSchedule overlay)
    {
        var notes = baseSchedule.Notes.Concat(overlay.Notes).OrderBy(n => n.OnBeat).ToArray();
        var clips = baseSchedule.AudioClips.Concat(overlay.AudioClips).ToArray();
        return new PlaybackSchedule
        {
            Notes = notes,
            ControlChanges = baseSchedule.ControlChanges.Concat(overlay.ControlChanges).OrderBy(c => c.Beat).ToArray(),
            AudioClips = clips,
            ArrangementEndBeat = Math.Max(baseSchedule.ArrangementEndBeat, overlay.ArrangementEndBeat)
        };
    }
}
