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

    public IReadOnlyList<ScriptVideoLayerInfo> GetVideoLayers() =>
        _project.Current.VideoLayers.Select(l => new ScriptVideoLayerInfo(
            l.Id, l.Name, l.ZOrder, l.Opacity, l.DefaultVisible,
            l.OffsetSeconds, l.Fps, l.Muted, l.InPointSeconds, l.OutPointSeconds,
            l.SyncClipId, l.AudioSourceTrackId,
            (ScriptVideoWaveformStyle)l.WaveformStyle, l.WaveformFollowPlayhead,
            l.WaveformColorArgb, l.WaveformX, l.WaveformY, l.WaveformWidth, l.WaveformHeight,
            l.Scope3DCameraYaw, l.Scope3DCameraPitch, l.Scope3DCameraDistance,
            l.Scope3DLineThickness, l.Scope3DTrailCount, l.Scope3DTransparentBackground,
            l.Engine3DEffectKind is { } fx ? (ScriptVideoEngine3DEffectKind)fx : null,
            l.Engine3DAudioSourceTrackId, l.Engine3DImagePath,
            l.Engine3DX, l.Engine3DY, l.Engine3DWidth, l.Engine3DHeight,
            l.Engine3DCameraYaw, l.Engine3DCameraPitch, l.Engine3DCameraDistance,
            l.Engine3DParticleCount, l.Engine3DParticleSize, l.Engine3DParticleColorArgb,
            (ScriptVideoEngine3DParticleShape)l.Engine3DParticleShape, l.Engine3DTransparentBackground)).ToArray();

    public void AddVideoLayer(ScriptVideoLayerInfo layer)
    {
        if (_project.Current.VideoLayers.Any(l => l.Id == layer.Id)) return;
        _history.Capture("Add video layer");
        _project.Current.VideoLayers.Add(new VideoLayer
        {
            Id = layer.Id,
            Name = layer.Name,
            ZOrder = layer.ZOrder,
            Opacity = layer.Opacity,
            DefaultVisible = layer.DefaultVisible,
            OffsetSeconds = layer.OffsetSeconds,
            Fps = layer.Fps,
            Muted = layer.Muted,
            InPointSeconds = layer.InPointSeconds,
            OutPointSeconds = layer.OutPointSeconds,
            SyncClipId = layer.SyncClipId,
            AudioSourceTrackId = layer.AudioSourceTrackId,
            WaveformStyle = (VideoWaveformStyle)layer.WaveformStyle,
            WaveformFollowPlayhead = layer.WaveformFollowPlayhead,
            WaveformColorArgb = layer.WaveformColorArgb,
            WaveformX = layer.WaveformX,
            WaveformY = layer.WaveformY,
            WaveformWidth = layer.WaveformWidth,
            WaveformHeight = layer.WaveformHeight,
            Scope3DCameraYaw = layer.Scope3DCameraYaw,
            Scope3DCameraPitch = layer.Scope3DCameraPitch,
            Scope3DCameraDistance = layer.Scope3DCameraDistance,
            Scope3DLineThickness = layer.Scope3DLineThickness,
            Scope3DTrailCount = layer.Scope3DTrailCount,
            Scope3DTransparentBackground = layer.Scope3DTransparentBackground,
            Engine3DEffectKind = layer.Engine3DEffectKind is { } fx
                ? (VideoEngine3DEffectKind)fx
                : null,
            Engine3DAudioSourceTrackId = layer.Engine3DAudioSourceTrackId,
            Engine3DImagePath = layer.Engine3DImagePath,
            Engine3DX = layer.Engine3DX,
            Engine3DY = layer.Engine3DY,
            Engine3DWidth = layer.Engine3DWidth,
            Engine3DHeight = layer.Engine3DHeight,
            Engine3DCameraYaw = layer.Engine3DCameraYaw,
            Engine3DCameraPitch = layer.Engine3DCameraPitch,
            Engine3DCameraDistance = layer.Engine3DCameraDistance,
            Engine3DParticleCount = layer.Engine3DParticleCount,
            Engine3DParticleSize = layer.Engine3DParticleSize,
            Engine3DParticleColorArgb = layer.Engine3DParticleColorArgb,
            Engine3DParticleShape = (VideoEngine3DParticleShape)layer.Engine3DParticleShape,
            Engine3DTransparentBackground = layer.Engine3DTransparentBackground
        });
    }

    public IReadOnlyList<ScriptVideoLayerItemInfo> GetVideoLayerItems() =>
        _project.Current.VideoLayers.SelectMany(l => l.Items.Select(i => new ScriptVideoLayerItemInfo(
            i.Id, l.Id, (ScriptVideoElementKind)i.Kind, i.SourcePath,
            i.X, i.Y, i.Width, i.Height, i.Rotation, i.Opacity,
            i.TextContent, i.FontSizePx, i.TextColorArgb))).ToArray();

    public void AddVideoLayerItem(ScriptVideoLayerItemInfo item)
    {
        var layer = _project.Current.VideoLayers.FirstOrDefault(l => l.Id == item.LayerId);
        if (layer is null || layer.Items.Any(i => i.Id == item.Id)) return;
        _history.Capture("Add video layer item");
        layer.Items.Add(new VideoLayerItem
        {
            Id = item.Id,
            Kind = (VideoElementKind)item.Kind,
            SourcePath = item.SourcePath,
            X = item.X,
            Y = item.Y,
            Width = item.Width,
            Height = item.Height,
            Rotation = item.Rotation,
            Opacity = item.Opacity,
            TextContent = item.TextContent,
            FontSizePx = item.FontSizePx,
            TextColorArgb = item.TextColorArgb
        });
    }

    public bool GetVideoEnabled() => _project.Current.VideoEnabled;

    public void SetVideoEnabled(bool enabled)
    {
        _history.Capture("Set video enabled");
        _project.Current.VideoEnabled = enabled;
    }

    public IReadOnlyList<ScriptVideoTriggerInfo> GetVideoTriggers() =>
        _project.Current.VideoTriggers.Select(t => new ScriptVideoTriggerInfo(
            t.Id, t.TargetLayerId, (ScriptVideoTriggerSource)t.Source, t.TrackId, t.ClipId, t.MidiNote,
            (ScriptVideoTriggerMoment)t.Moment, (ScriptVideoTriggerAction)t.Action, t.FadeDurationSeconds)).ToArray();

    public void AddVideoTrigger(ScriptVideoTriggerInfo trigger)
    {
        _history.Capture("Add video trigger");
        _project.Current.VideoTriggers.Add(new VideoTrigger
        {
            Id = trigger.Id,
            TargetLayerId = trigger.TargetLayerId,
            Source = (VideoTriggerSource)trigger.Source,
            TrackId = trigger.TrackId,
            ClipId = trigger.ClipId,
            MidiNote = trigger.MidiNote,
            Moment = (VideoTriggerMoment)trigger.Moment,
            Action = (VideoTriggerAction)trigger.Action,
            FadeDurationSeconds = trigger.FadeDurationSeconds
        });
    }

    public IReadOnlyList<ScriptVideoVisibilityRegionInfo> GetVideoVisibilityRegions() =>
        _project.Current.VideoVisibilityRegions.Select(r => new ScriptVideoVisibilityRegionInfo(
            r.Id, r.LayerId, r.StartBeat, r.EndBeat)).ToArray();

    public void AddVideoVisibilityRegion(ScriptVideoVisibilityRegionInfo region)
    {
        if (_project.Current.VideoVisibilityRegions.Any(r => r.Id == region.Id)) return;
        _history.Capture("Add video visibility region");
        _project.Current.VideoVisibilityRegions.Add(new VideoVisibilityRegion
        {
            Id = region.Id,
            LayerId = region.LayerId,
            StartBeat = region.StartBeat,
            EndBeat = region.EndBeat
        });
    }

    public ScriptVideoCanvasInfo GetVideoCanvasSize() =>
        new(_project.Current.VideoCanvasWidth, _project.Current.VideoCanvasHeight, _project.Current.VideoExportFps);

    public void SetVideoCanvasSize(ScriptVideoCanvasInfo size)
    {
        _history.Capture("Set video canvas size");
        _project.Current.VideoCanvasWidth = Math.Clamp(size.Width, 320, 4096);
        _project.Current.VideoCanvasHeight = Math.Clamp(size.Height, 320, 4096);
        if (size.ExportFps > 0)
            _project.Current.VideoExportFps = Math.Clamp(size.ExportFps, 1, 120);
    }

    public double GetVideoExportFps() => _project.Current.VideoExportFps;

    public void SetVideoExportFps(double fps)
    {
        _history.Capture("Set video export FPS");
        _project.Current.VideoExportFps = Math.Clamp(fps, 1, 120);
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
