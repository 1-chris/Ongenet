using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Scripting;

internal static class ScriptingApiSupport
{
    public static Track? FindTrack(Project project, Guid trackId) =>
        project.Tracks.FirstOrDefault(t => t.Id == trackId);

    public static (Track Track, Clip Clip)? FindClip(Project project, Guid clipId)
    {
        foreach (var track in project.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip is not null) return (track, clip);
        }

        return null;
    }

    public static List<IAudioEffect> GetEffectChain(Track track, int instrumentSlotIndex)
    {
        if (instrumentSlotIndex < 0) return track.Effects;
        if (instrumentSlotIndex >= track.Instruments.Count)
            throw new InvalidOperationException($"Instrument slot {instrumentSlotIndex} does not exist.");
        return track.Instruments[instrumentSlotIndex].Effects;
    }

    public static InstrumentSlot GetInstrumentSlot(Track track, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= track.Instruments.Count)
            throw new InvalidOperationException($"Instrument slot {slotIndex} does not exist on track '{track.Name}'.");
        return track.Instruments[slotIndex];
    }

    public static ScriptTrackInfo ToTrackInfo(Track track) =>
        new(
            track.Id,
            track.Name,
            track.Kind switch
            {
                TrackKind.Audio => ScriptTrackKind.Audio,
                TrackKind.Instrument => ScriptTrackKind.Instrument,
                TrackKind.Hybrid => ScriptTrackKind.Hybrid,
                TrackKind.Group => ScriptTrackKind.Group,
                TrackKind.Return => ScriptTrackKind.Return,
                TrackKind.Master => ScriptTrackKind.Master,
                TrackKind.Midi => ScriptTrackKind.Midi,
                TrackKind.Pattern => ScriptTrackKind.Pattern,
                _ => ScriptTrackKind.Audio
            },
            track.IsMuted,
            track.IsSoloed,
            track.IsArmed,
            track.Volume,
            track.Pan,
            track.ParentId,
            track.ColorKey,
            track.SurroundWidth,
            track.DrumMapId,
            track.AutomationCollapsed,
            track.GroupCollapsed);

    public static ScriptClipInfo ToClipInfo(Track track, Clip clip) =>
        new(
            clip.Id,
            track.Id,
            clip.Name,
            clip.StartBeat,
            clip.LengthBeats,
            clip.IsAudio,
            clip.Notes.Count,
            clip.AudioFilePath,
            clip.LinkedClipGroupId);

    public static ScriptScaleType ToScriptScale(ScaleType scale) =>
        Enum.TryParse<ScriptScaleType>(scale.ToString(), out var s) ? s : ScriptScaleType.Major;

    public static ScaleType ToModelScale(ScriptScaleType scale) =>
        Enum.TryParse<ScaleType>(scale.ToString(), out var s) ? s : ScaleType.Major;

    public static ScriptPlaybackMode ToScriptPlayback(PlaybackMode mode) => mode switch
    {
        PlaybackMode.Session => ScriptPlaybackMode.Session,
        PlaybackMode.Hybrid => ScriptPlaybackMode.Hybrid,
        _ => ScriptPlaybackMode.Arrangement
    };

    public static PlaybackMode ToModelPlayback(ScriptPlaybackMode mode) => mode switch
    {
        ScriptPlaybackMode.Session => PlaybackMode.Session,
        ScriptPlaybackMode.Hybrid => PlaybackMode.Hybrid,
        _ => PlaybackMode.Arrangement
    };

    public static ScriptEffectInfo ToEffectInfo(int index, IAudioEffect effect) =>
        new(index, effect.TypeId, effect.Name, effect.Enabled, ScriptingParameterHelper.Snapshot(effect.Parameters));

    public static ScriptInstrumentInfo ToInstrumentInfo(int index, InstrumentSlot slot) =>
        new(
            index,
            slot.Instrument.TypeId,
            slot.Instrument.Name,
            slot.Enabled,
            slot.OutputBusIndex,
            slot.OutputTrackId,
            slot.Effects.Select((e, i) => ToEffectInfo(i, e)).ToArray());

    public static Track CreateTrack(TrackKind kind, string name, Guid? id, string? colorKey) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Kind = kind,
            ColorKey = colorKey ?? "CatppuccinMauve"
        };
}
