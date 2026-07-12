using System;
using System.Linq;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

public sealed partial class ScriptingApi
{
    public ScriptTrackInfo? GetTrack(Guid trackId)
    {
        var track = FindTrack(trackId);
        return track is null ? null : ToTrackInfo(track);
    }

    public Guid AddInstrumentTrackWithId(Guid id, string name, string? colorKey = null)
        => AddTrackWithId(id, name, TrackKind.Instrument, colorKey);

    public Guid AddAudioTrack(string name) => AddTrackWithId(Guid.NewGuid(), name, TrackKind.Audio, null);

    public Guid AddAudioTrackWithId(Guid id, string name, string? colorKey = null)
        => AddTrackWithId(id, name, TrackKind.Audio, colorKey);

    public Guid AddGroupTrack(string name) => AddTrackWithId(Guid.NewGuid(), name, TrackKind.Group, null);

    public Guid AddGroupTrackWithId(Guid id, string name, string? colorKey = null)
        => AddTrackWithId(id, name, TrackKind.Group, colorKey);

    public Guid AddReturnTrack(string name) => AddTrackWithId(Guid.NewGuid(), name, TrackKind.Return, null);

    public Guid AddReturnTrackWithId(Guid id, string name, string? colorKey = null)
        => AddTrackWithId(id, name, TrackKind.Return, colorKey);

    public Guid AddHybridTrack(string name) => AddTrackWithId(Guid.NewGuid(), name, TrackKind.Hybrid, null);

    public Guid AddHybridTrackWithId(Guid id, string name, string? colorKey = null)
        => AddTrackWithId(id, name, TrackKind.Hybrid, colorKey);

    public Guid AddMasterTrackWithId(Guid id, string name)
    {
        if (_project.Current.Tracks.Any(t => t.Kind == TrackKind.Master))
            throw new InvalidOperationException("Project already has a master track.");
        return AddTrackWithId(id, name, TrackKind.Master, "CatppuccinSubtext0", volume: 1.0);
    }

    public void SetTrackMuted(Guid trackId, bool muted)
    {
        var track = FindTrack(trackId);
        if (track is null || track.IsMuted == muted) return;
        _history.Capture("Change track mute");
        track.IsMuted = muted;
        _events.Publish(new TrackChangedEvent(track));
    }

    public void SetTrackSoloed(Guid trackId, bool soloed)
    {
        var track = FindTrack(trackId);
        if (track is null || track.IsSoloed == soloed) return;
        _history.Capture("Change track solo");
        track.IsSoloed = soloed;
        _events.Publish(new TrackChangedEvent(track));
    }

    public void SetTrackArmed(Guid trackId, bool armed)
    {
        var track = FindTrack(trackId);
        if (track is null || track.IsArmed == armed) return;
        _history.Capture("Change track arm");
        track.IsArmed = armed;
        _events.Publish(new TrackChangedEvent(track));
    }

    public void SetTrackColor(Guid trackId, string colorKey)
    {
        var track = FindTrack(trackId);
        if (track is null || track.ColorKey == colorKey) return;
        _history.Capture("Change track color");
        track.ColorKey = colorKey;
        _events.Publish(new TrackChangedEvent(track));
    }

    public void SetTrackParent(Guid trackId, Guid? parentId)
    {
        var track = FindTrack(trackId);
        if (track is null || track.ParentId == parentId) return;
        _history.Capture("Change track parent");
        track.ParentId = parentId;
        _events.Publish(new TrackChangedEvent(track));
    }

    public void SetTrackSurroundWidth(Guid trackId, double width)
    {
        var track = FindTrack(trackId);
        if (track is null) return;
        width = Math.Clamp(width, 0, 1);
        if (Math.Abs(track.SurroundWidth - width) < 1e-9) return;
        _history.Capture("Change surround width");
        track.SurroundWidth = width;
        _events.Publish(new TrackChangedEvent(track));
    }

    public void SetTrackDrumMapId(Guid trackId, Guid? drumMapId)
    {
        var track = FindTrack(trackId);
        if (track is null || track.DrumMapId == drumMapId) return;
        _history.Capture("Change drum map");
        track.DrumMapId = drumMapId;
        _events.Publish(new TrackChangedEvent(track));
    }

    private Guid AddTrackWithId(Guid id, string name, TrackKind kind, string? colorKey, double? volume = null)
    {
        if (FindTrack(id) is not null)
            throw new InvalidOperationException($"Track id '{id}' already exists.");

        if (string.IsNullOrWhiteSpace(name))
            name = kind switch
            {
                TrackKind.Audio => "Audio",
                TrackKind.Group => "Group",
                TrackKind.Return => "Return",
                TrackKind.Hybrid => "Hybrid",
                TrackKind.Master => "Master",
                _ => $"Instrument {NextInstrumentTrackNumber()}"
            };

        _history.Capture($"Add {kind} track");
        var track = ScriptingApiSupport.CreateTrack(kind, name, id, colorKey);
        if (volume.HasValue) track.Volume = volume.Value;

        if (kind == TrackKind.Instrument)
        {
            try
            {
                var instrument = _instruments.Create(InstrumentRegistry.DefaultInstrumentId);
                track.Instruments.Add(new InstrumentSlot(instrument));
                track.CommitInstruments();
            }
            catch
            {
                // Empty rack is valid for exported scripts that set instruments explicitly.
            }
        }

        var index = kind == TrackKind.Master ? 0 : (_project.Current.Master is not null ? 1 : 0);
        if (index > _project.Current.Tracks.Count) index = _project.Current.Tracks.Count;
        _project.Current.Tracks.Insert(index, track);
        _events.Publish(new TracksChangedEvent());
        return track.Id;
    }
}
