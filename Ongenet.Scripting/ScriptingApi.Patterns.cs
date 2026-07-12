using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

public sealed partial class ScriptingApi
{
    public IReadOnlyList<ScriptPatternInfo> GetPatterns() =>
        _project.Current.Patterns.Select(p => new ScriptPatternInfo(
            p.Id,
            p.Name,
            p.LengthBeats,
            p.ColorIndex,
            p.Channels.Select(ch =>
            {
                var seq = p.StepSequences.FirstOrDefault(s => s.PatternChannelId == ch.Id);
                var steps = seq?.Steps.Select(s => new ScriptStepData(
                    s.Active, s.Note, s.Velocity, s.Pan, s.Probability, s.MicroTimingTicks)).ToArray();
                return new ScriptPatternChannelInfo(
                    ch.Id, ch.Order,
                    ch.SourceKind == PatternRowSourceKind.AudioSample
                        ? ScriptPatternRowSourceKind.AudioSample
                        : ScriptPatternRowSourceKind.InstrumentTrack,
                    ch.TrackId, ch.SampleClipId, ch.Name, ch.Muted, ch.Volume, ch.Pan, steps);
            }).ToArray())).ToArray();

    public Guid AddPatternWithId(Guid id, string name, double lengthBeats, int colorIndex)
    {
        if (_project.Current.Patterns.Any(p => p.Id == id))
            throw new InvalidOperationException($"Pattern id '{id}' already exists.");
        _history.Capture("Add pattern");
        var pattern = new Pattern { Id = id, Name = name, LengthBeats = lengthBeats, ColorIndex = colorIndex };
        _project.Current.Patterns.Add(pattern);
        return pattern.Id;
    }

    public void AddPatternChannel(Guid patternId, ScriptPatternChannelInfo channel)
    {
        var pattern = _project.Current.Patterns.FirstOrDefault(p => p.Id == patternId)
            ?? throw new InvalidOperationException($"Pattern '{patternId}' was not found.");
        _history.Capture("Add pattern channel");
        var ch = new PatternChannel
        {
            Id = channel.Id,
            Order = channel.Order,
            SourceKind = channel.SourceKind == ScriptPatternRowSourceKind.AudioSample
                ? PatternRowSourceKind.AudioSample
                : PatternRowSourceKind.InstrumentTrack,
            TrackId = channel.TrackId,
            SampleClipId = channel.SampleClipId,
            Name = channel.Name,
            Muted = channel.Muted,
            Volume = channel.Volume,
            Pan = channel.Pan
        };
        pattern.Channels.Add(ch);
        if (channel.Steps is not null)
            SetPatternSteps(patternId, ch.Id, channel.Steps);
    }

    public void SetPatternSteps(Guid patternId, Guid channelId, IReadOnlyList<ScriptStepData> steps)
    {
        var pattern = _project.Current.Patterns.FirstOrDefault(p => p.Id == patternId)
            ?? throw new InvalidOperationException($"Pattern '{patternId}' was not found.");
        var channel = pattern.Channels.FirstOrDefault(c => c.Id == channelId)
            ?? throw new InvalidOperationException($"Pattern channel '{channelId}' was not found.");
        _history.Capture("Set pattern steps");
        var seq = pattern.GetOrCreateSequence(channel, steps.Count);
        seq.Steps.Clear();
        foreach (var s in steps)
        {
            seq.Steps.Add(new StepData
            {
                Active = s.Active,
                Note = s.Note,
                Velocity = s.Velocity,
                Pan = s.Pan,
                Probability = s.Probability,
                MicroTimingTicks = s.MicroTimingTicks
            });
        }
    }

    public IReadOnlyList<ScriptPatternClipInfo> GetPatternClips() =>
        _project.Current.PatternClips.Select(c => new ScriptPatternClipInfo(c.Id, c.PatternId, c.TrackId, c.StartBeat, c.LengthBeats)).ToArray();

    public void AddPatternClip(ScriptPatternClipInfo clip)
    {
        _history.Capture("Add pattern clip");
        _project.Current.PatternClips.Add(new PatternClip
        {
            Id = clip.Id,
            PatternId = clip.PatternId,
            TrackId = clip.TrackId,
            StartBeat = clip.StartBeat,
            LengthBeats = clip.LengthBeats
        });
    }

    public IReadOnlyList<ScriptSessionClipInfo> GetSessionClips() =>
        _project.Current.SessionClips.Select(c => new ScriptSessionClipInfo(
            c.Id, c.TrackId, c.SceneIndex, c.Name, c.LengthBeats,
            c.LaunchMode switch
            {
                SessionLaunchMode.Gate => ScriptSessionLaunchMode.Gate,
                SessionLaunchMode.Toggle => ScriptSessionLaunchMode.Toggle,
                _ => ScriptSessionLaunchMode.Trigger
            },
            c.FollowAction switch
            {
                FollowAction.PlayNext => ScriptFollowAction.PlayNext,
                FollowAction.PlayPrevious => ScriptFollowAction.PlayPrevious,
                FollowAction.PlayRandom => ScriptFollowAction.PlayRandom,
                FollowAction.PlayFirst => ScriptFollowAction.PlayFirst,
                FollowAction.PlayAgain => ScriptFollowAction.PlayAgain,
                _ => ScriptFollowAction.Stop
            },
            c.LaunchQuantizeBeats,
            c.SourceClipId)).ToArray();

    public void AddSessionClip(ScriptSessionClipInfo clip)
    {
        _history.Capture("Add session clip");
        _project.Current.SessionClips.Add(new SessionClip
        {
            Id = clip.Id,
            TrackId = clip.TrackId,
            SceneIndex = clip.SceneIndex,
            Name = clip.Name,
            LengthBeats = clip.LengthBeats,
            LaunchMode = clip.LaunchMode switch
            {
                ScriptSessionLaunchMode.Gate => SessionLaunchMode.Gate,
                ScriptSessionLaunchMode.Toggle => SessionLaunchMode.Toggle,
                _ => SessionLaunchMode.Trigger
            },
            FollowAction = clip.FollowAction switch
            {
                ScriptFollowAction.PlayNext => FollowAction.PlayNext,
                ScriptFollowAction.PlayPrevious => FollowAction.PlayPrevious,
                ScriptFollowAction.PlayRandom => FollowAction.PlayRandom,
                ScriptFollowAction.PlayFirst => FollowAction.PlayFirst,
                ScriptFollowAction.PlayAgain => FollowAction.PlayAgain,
                _ => FollowAction.Stop
            },
            LaunchQuantizeBeats = clip.LaunchQuantizeBeats,
            SourceClipId = clip.SourceClipId
        });
    }
}
