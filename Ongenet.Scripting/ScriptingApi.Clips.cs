using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

public sealed partial class ScriptingApi
{
    public IReadOnlyList<ScriptMidiNote> GetMidiNotes(Guid clipId)
    {
        var found = ScriptingApiSupport.FindClip(_project.Current, clipId)
            ?? throw new InvalidOperationException($"Clip '{clipId}' was not found.");
        return found.Clip.Notes.Select(n => new ScriptMidiNote(
            n.Note, n.StartBeat, n.LengthBeats, n.Velocity,
            (int)n.SlideSemitones, n.PortamentoMs, n.NoteGroupId, n.Chance, n.HumanizeTicks)).ToArray();
    }

    public ScriptAudioClipMetadata? GetAudioClipMetadata(Guid clipId)
    {
        var found = ScriptingApiSupport.FindClip(_project.Current, clipId);
        if (found is null || !found.Value.Clip.IsAudio) return null;
        var clip = found.Value.Clip;
        return new ScriptAudioClipMetadata(
            clip.AudioFilePath,
            clip.SourceOffsetSeconds,
            clip.SourceLengthSeconds ?? 0,
            clip.SourceTempo,
            clip.SourceKey,
            clip.StretchToTempo,
            clip.PitchCorrected,
            clip.WarpMode.ToString(),
            clip.WarpMarkers?.Select(w => new ScriptWarpMarker(w.SourceSeconds, w.BeatPosition)).ToArray(),
            clip.UserFadeInBeats,
            clip.UserFadeOutBeats,
            clip.HasAraRegion,
            clip.AraPitchOffsetSemitones);
    }

    public Guid CreateMidiClipWithId(Guid id, Guid trackId, string name, double startBeat, double lengthBeats)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        if (track.IsBus) throw new InvalidOperationException("Cannot add clips to a bus track.");
        if (lengthBeats <= 0) throw new ArgumentOutOfRangeException(nameof(lengthBeats));
        _history.Capture("Add MIDI clip");
        var clip = new Clip
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "Clip" : name,
            StartBeat = Math.Max(0, startBeat),
            LengthBeats = lengthBeats,
            IsAudio = false
        };
        track.Clips.Add(clip);
        _events.Publish(new ClipAddedEvent(track, clip));
        return clip.Id;
    }

    public Guid CreateAudioClip(Guid trackId, string name, double startBeat, double lengthBeats, ScriptAudioClipMetadata metadata)
        => CreateAudioClipWithId(Guid.NewGuid(), trackId, name, startBeat, lengthBeats, metadata);

    public Guid CreateAudioClipWithId(Guid id, Guid trackId, string name, double startBeat, double lengthBeats, ScriptAudioClipMetadata metadata)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        if (track.IsBus) throw new InvalidOperationException("Cannot add clips to a bus track.");
        _history.Capture("Add audio clip");
        var clip = new Clip
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "Audio" : name,
            StartBeat = Math.Max(0, startBeat),
            LengthBeats = lengthBeats,
            IsAudio = true,
            AudioFilePath = metadata.AudioFilePath,
            SourceOffsetSeconds = metadata.SourceOffsetSeconds,
            SourceLengthSeconds = metadata.SourceLengthSeconds,
            SourceTempo = metadata.SourceTempo,
            SourceKey = metadata.SourceKey,
            StretchToTempo = metadata.StretchToTempo,
            PitchCorrected = metadata.PitchCorrected,
            UserFadeInBeats = metadata.UserFadeInBeats,
            UserFadeOutBeats = metadata.UserFadeOutBeats,
            HasAraRegion = metadata.HasAraRegion,
            AraPitchOffsetSemitones = metadata.AraPitchOffsetSemitones
        };
        if (metadata.WarpMarkers is not null)
        {
            foreach (var w in metadata.WarpMarkers)
                clip.WarpMarkers.Add(new WarpMarker { SourceSeconds = w.SourceSeconds, BeatPosition = w.BeatPosition });
        }

        track.Clips.Add(clip);
        _events.Publish(new ClipAddedEvent(track, clip));
        return clip.Id;
    }

    public void ResizeClip(Guid clipId, double lengthBeats)
    {
        var found = ScriptingApiSupport.FindClip(_project.Current, clipId);
        if (found is null || lengthBeats <= 0) return;
        if (Math.Abs(found.Value.Clip.LengthBeats - lengthBeats) < 1e-9) return;
        _history.Capture("Resize clip");
        found.Value.Clip.LengthBeats = lengthBeats;
        _events.Publish(new ClipChangedEvent(found.Value.Clip));
    }

    public void SetClipLinkedGroup(Guid clipId, Guid? groupId)
    {
        var found = ScriptingApiSupport.FindClip(_project.Current, clipId);
        if (found is null || found.Value.Clip.LinkedClipGroupId == groupId) return;
        _history.Capture("Link clip group");
        found.Value.Clip.LinkedClipGroupId = groupId;
        _events.Publish(new ClipChangedEvent(found.Value.Clip));
    }

    public void ClearMidiNotes(Guid clipId)
    {
        var found = ScriptingApiSupport.FindClip(_project.Current, clipId);
        if (found is null || found.Value.Clip.IsAudio) return;
        _history.Capture("Clear MIDI notes");
        found.Value.Clip.Notes.Clear();
        _events.Publish(new ClipNotesChangedEvent(found.Value.Clip));
    }

    public void AddMidiNote(Guid clipId, ScriptMidiNote note) => AddMidiNotes(clipId, new[] { note });

    public void AddMidiNotes(Guid clipId, IReadOnlyList<ScriptMidiNote> notes)
    {
        var found = ScriptingApiSupport.FindClip(_project.Current, clipId);
        if (found is null || found.Value.Clip.IsAudio || notes.Count == 0) return;
        _history.Capture("Add MIDI notes");
        foreach (var n in notes)
        {
            found.Value.Clip.Notes.Add(new MidiNote
            {
                Note = n.Note,
                StartBeat = n.StartBeat,
                LengthBeats = n.LengthBeats,
                Velocity = n.Velocity,
                SlideSemitones = n.SlideSemitones,
                PortamentoMs = n.PortamentoMs,
                NoteGroupId = n.NoteGroupId,
                Chance = n.Chance,
                HumanizeTicks = n.HumanizeTicks
            });
        }

        _events.Publish(new ClipNotesChangedEvent(found.Value.Clip));
    }

    public void AddMidiControlChange(Guid clipId, ScriptMidiControlChange cc)
    {
        var found = ScriptingApiSupport.FindClip(_project.Current, clipId);
        if (found is null || found.Value.Clip.IsAudio) return;
        _history.Capture("Add MIDI CC");
        found.Value.Clip.ControlChanges.Add(new MidiControlChange
        {
            Controller = cc.Controller,
            Value = cc.Value,
            StartBeat = cc.StartBeat,
            LengthBeats = cc.LengthBeats
        });
        _events.Publish(new ClipChangedEvent(found.Value.Clip));
    }

    public void ClearMidiControlChanges(Guid clipId)
    {
        var found = ScriptingApiSupport.FindClip(_project.Current, clipId);
        if (found is null) return;
        _history.Capture("Clear MIDI CC");
        found.Value.Clip.ControlChanges.Clear();
        _events.Publish(new ClipChangedEvent(found.Value.Clip));
    }
}
