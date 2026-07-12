using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

public sealed partial class ScriptingApi
{
    public IReadOnlyList<ScriptMarkerInfo> GetMarkers() =>
        _project.Current.Markers.Select(m => new ScriptMarkerInfo(m.Id, m.Name, m.Beat)).ToArray();

    public void AddMarker(ScriptMarkerInfo marker)
    {
        if (_project.Current.Markers.Any(m => m.Id == marker.Id)) return;
        _history.Capture("Add marker");
        _project.Current.Markers.Add(new ArrangementMarker { Id = marker.Id, Name = marker.Name, Beat = marker.Beat });
    }

    public IReadOnlyList<ScriptSectionInfo> GetSections() =>
        _project.Current.ArrangementSections.Select(s => new ScriptSectionInfo(s.Id, s.MarkerId)).ToArray();

    public void AddSection(ScriptSectionInfo section)
    {
        if (_project.Current.ArrangementSections.Any(s => s.Id == section.Id)) return;
        _history.Capture("Add section");
        _project.Current.ArrangementSections.Add(new ArrangementSection { Id = section.Id, MarkerId = section.MarkerId });
    }

    public IReadOnlyList<ScriptChordRegionInfo> GetChordRegions() =>
        _project.Current.ChordTrack.Regions.Select(r => new ScriptChordRegionInfo(
            r.StartBeat, r.LengthBeats, r.Symbol,
            r.Quality switch
            {
                ChordQuality.Minor => ScriptChordQuality.Minor,
                ChordQuality.Dominant7 => ScriptChordQuality.Dominant7,
                ChordQuality.Major7 => ScriptChordQuality.Major7,
                ChordQuality.Minor7 => ScriptChordQuality.Minor7,
                _ => ScriptChordQuality.Major
            })).ToArray();

    public void SetChordTrackEnabled(bool enabled)
    {
        if (_project.Current.ChordTrack.Enabled == enabled) return;
        _history.Capture("Toggle chord track");
        _project.Current.ChordTrack.Enabled = enabled;
    }

    public void AddChordRegion(ScriptChordRegionInfo region)
    {
        _history.Capture("Add chord region");
        _project.Current.ChordTrack.Regions.Add(new ChordRegion
        {
            StartBeat = region.StartBeat,
            LengthBeats = region.LengthBeats,
            Symbol = region.Symbol,
            Quality = region.Quality switch
            {
                ScriptChordQuality.Minor => ChordQuality.Minor,
                ScriptChordQuality.Dominant7 => ChordQuality.Dominant7,
                ScriptChordQuality.Major7 => ChordQuality.Major7,
                ScriptChordQuality.Minor7 => ChordQuality.Minor7,
                _ => ChordQuality.Major
            }
        });
    }

    public IReadOnlyList<ScriptDrumMapInfo> GetDrumMaps() =>
        _project.Current.DrumMaps.Select(m => new ScriptDrumMapInfo(
            m.Id, m.Name,
            m.Entries.Select(e => new ScriptDrumMapEntryInfo(e.Note, e.Label, e.SampleClipId, e.VelocityScale)).ToArray())).ToArray();

    public void AddDrumMap(ScriptDrumMapInfo map)
    {
        if (_project.Current.DrumMaps.Any(m => m.Id == map.Id)) return;
        _history.Capture("Add drum map");
        var dm = new DrumMap { Id = map.Id, Name = map.Name };
        foreach (var e in map.Entries)
        {
            dm.Entries.Add(new DrumMapEntry
            {
                Note = e.Note,
                Label = e.Label,
                SampleClipId = e.SampleClipId,
                VelocityScale = (float)e.VelocityScale
            });
        }

        _project.Current.DrumMaps.Add(dm);
    }

    public IReadOnlyList<ScriptExpressionMapInfo> GetExpressionMaps() =>
        _project.Current.ExpressionMaps.Select(m => new ScriptExpressionMapInfo(
            m.Name,
            m.Entries.Select(e => new ScriptExpressionMapEntryInfo(
                e.Articulation, e.KeyswitchNote, e.CcNumber, e.CcValue)).ToArray())).ToArray();

    public void AddExpressionMap(ScriptExpressionMapInfo map)
    {
        _history.Capture("Add expression map");
        var em = new VstExpressionMap { Name = map.Name };
        foreach (var e in map.Entries)
        {
            em.Entries.Add(new ExpressionMapEntry
            {
                Articulation = e.Articulation,
                KeyswitchNote = e.KeyswitchNote,
                CcNumber = e.CcNumber,
                CcValue = e.CcValue
            });
        }

        _project.Current.ExpressionMaps.Add(em);
    }

    public IReadOnlyList<ScriptVideoTrackInfo> GetVideoTracks() =>
        _project.Current.VideoTracks.Select(v => new ScriptVideoTrackInfo(
            v.Id, v.FilePath, v.OffsetSeconds, v.Fps, v.Muted, v.InPointSeconds, v.OutPointSeconds)).ToArray();

    public void AddVideoTrack(ScriptVideoTrackInfo track)
    {
        if (_project.Current.VideoTracks.Any(v => v.Id == track.Id)) return;
        _history.Capture("Add video track");
        _project.Current.VideoTracks.Add(new VideoTrack
        {
            Id = track.Id,
            FilePath = track.FilePath,
            OffsetSeconds = track.OffsetSeconds,
            Fps = track.Fps,
            Muted = track.Muted,
            InPointSeconds = track.InPointSeconds,
            OutPointSeconds = track.OutPointSeconds
        });
    }

    public IReadOnlyList<ScriptControlRoomProfileInfo> GetControlRoomProfiles() =>
        _project.Current.ControlRoomProfiles.Select(p => new ScriptControlRoomProfileInfo(
            p.Name, p.CueVolume, p.MainVolume, p.DimEnabled, p.DimAmountDb)).ToArray();

    public void AddControlRoomProfile(ScriptControlRoomProfileInfo profile)
    {
        _history.Capture("Add control room profile");
        _project.Current.ControlRoomProfiles.Add(new ControlRoomProfile
        {
            Name = profile.Name,
            CueVolume = profile.CueVolume,
            MainVolume = profile.MainVolume,
            DimEnabled = profile.DimEnabled,
            DimAmountDb = profile.DimAmountDb
        });
    }
}
