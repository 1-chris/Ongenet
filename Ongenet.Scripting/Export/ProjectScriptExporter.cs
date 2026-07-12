using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services;

namespace Ongenet.Scripting.Export;

/// <summary>Generates portable C# scripts that rebuild a project via api calls.</summary>
public sealed class ProjectScriptExporter : IProjectScriptExporter
{
    private readonly Dictionary<Guid, string> _trackVars = new();

    public string Export(Project project, ExportScriptOptions? options = null)
    {
        options ??= new ExportScriptOptions();
        _trackVars.Clear();
        var sb = new StringBuilder();
        sb.Append(ScriptCodeGenerator.Header("project", project.Name));

        sb.AppendLine("api.ClearProject();");
        sb.AppendLine($"api.SetProjectName({ScriptCodeGenerator.StringLiteral(project.Name)});");
        sb.AppendLine($"api.SetTempo({ScriptCodeGenerator.DoubleLiteral(project.Tempo.BeatsPerMinute)});");
        sb.AppendLine($"api.SetTimeSignature({project.TimeSignature.Numerator}, {project.TimeSignature.Denominator});");
        sb.AppendLine($"api.SetBarCount({project.BarCount});");
        sb.AppendLine($"api.SetKeySignature({project.KeyRootPitchClass}, ScriptScaleType.{project.KeyScale});");
        sb.AppendLine($"api.SetPlaybackMode(ScriptPlaybackMode.{project.PlaybackMode});");
        sb.AppendLine($"api.SetLaunchQuantizeBeats({ScriptCodeGenerator.DoubleLiteral(project.LaunchQuantizeBeats)});");

        var mpe = project.Mpe;
        sb.AppendLine($"api.SetMpeSettings(new ScriptMpeSettings({(mpe.Enabled ? "true" : "false")}, {mpe.MasterChannel}, {mpe.MemberChannelStart}, {mpe.MemberChannelCount}));");

        if (project.ActiveGroove is not null)
        {
            var g = project.ActiveGroove;
            sb.AppendLine($"api.SetActiveGroove(new ScriptGrooveTemplate({ScriptCodeGenerator.GuidLiteral(g.Id)}, {ScriptCodeGenerator.StringLiteral(g.Name)}, {ScriptCodeGenerator.DoubleLiteral(g.SwingAmount)}, {g.Division}));");
        }

        foreach (var map in project.DrumMaps)
            EmitDrumMap(sb, map);

        foreach (var map in project.ExpressionMaps)
            EmitExpressionMap(sb, map);

        foreach (var track in project.Tracks.OrderBy(t => t.Kind == TrackKind.Master ? 0 : 1))
            EmitTrackCreate(sb, track, options);

        foreach (var track in project.Tracks)
        {
            if (track.Kind == TrackKind.Master) continue;
            EmitTrackState(sb, track);
        }

        foreach (var track in project.Tracks)
            EmitDevices(sb, track, options);

        foreach (var track in project.Tracks.Where(t => !t.IsBus))
            EmitClips(sb, track, options);

        foreach (var track in project.Tracks)
            EmitAutomation(sb, track);

        foreach (var pattern in project.Patterns)
            EmitPattern(sb, pattern);

        foreach (var pc in project.PatternClips)
            sb.AppendLine($"api.AddPatternClip(new ScriptPatternClipInfo({ScriptCodeGenerator.GuidLiteral(pc.Id)}, {ScriptCodeGenerator.GuidLiteral(pc.PatternId)}, {ScriptCodeGenerator.GuidLiteral(pc.TrackId)}, {ScriptCodeGenerator.DoubleLiteral(pc.StartBeat)}, {ScriptCodeGenerator.DoubleLiteral(pc.LengthBeats)}));");

        foreach (var sc in project.SessionClips)
            EmitSessionClip(sb, sc);

        foreach (var marker in project.Markers)
            sb.AppendLine($"api.AddMarker(new ScriptMarkerInfo({ScriptCodeGenerator.GuidLiteral(marker.Id)}, {ScriptCodeGenerator.StringLiteral(marker.Name)}, {ScriptCodeGenerator.DoubleLiteral(marker.Beat)}));");

        foreach (var section in project.ArrangementSections)
            sb.AppendLine($"api.AddSection(new ScriptSectionInfo({ScriptCodeGenerator.GuidLiteral(section.Id)}, {ScriptCodeGenerator.GuidLiteral(section.MarkerId)}));");

        if (project.ChordTrack.Enabled || project.ChordTrack.Regions.Count > 0)
        {
            sb.AppendLine($"api.SetChordTrackEnabled({(project.ChordTrack.Enabled ? "true" : "false")});");
            foreach (var r in project.ChordTrack.Regions)
                sb.AppendLine($"api.AddChordRegion(new ScriptChordRegionInfo({ScriptCodeGenerator.DoubleLiteral(r.StartBeat)}, {ScriptCodeGenerator.DoubleLiteral(r.LengthBeats)}, {ScriptCodeGenerator.StringLiteral(r.Symbol)}, ScriptChordQuality.{r.Quality}));");
        }

        foreach (var vt in project.VideoTracks)
            sb.AppendLine($"api.AddVideoTrack(new ScriptVideoTrackInfo({ScriptCodeGenerator.GuidLiteral(vt.Id)}, {ScriptCodeGenerator.StringLiteral(vt.FilePath)}, {ScriptCodeGenerator.DoubleLiteral(vt.OffsetSeconds)}, {ScriptCodeGenerator.DoubleLiteral(vt.Fps)}, {(vt.Muted ? "true" : "false")}, {ScriptCodeGenerator.DoubleLiteral(vt.InPointSeconds)}, {ScriptCodeGenerator.DoubleLiteral(vt.OutPointSeconds)}));");

        foreach (var route in project.MultiOutputRoutes)
            sb.AppendLine($"api.AddMultiOutputRoute(new ScriptMultiOutputRouteInfo({ScriptCodeGenerator.GuidLiteral(route.SourceTrackId)}, {route.SlotIndex}, {route.PluginOutputBus}, {ScriptCodeGenerator.GuidLiteral(route.DestinationTrackId)}, {ScriptCodeGenerator.DoubleLiteral(route.Level)}));");

        foreach (var profile in project.ControlRoomProfiles)
            sb.AppendLine($"api.AddControlRoomProfile(new ScriptControlRoomProfileInfo({ScriptCodeGenerator.StringLiteral(profile.Name)}, {ScriptCodeGenerator.DoubleLiteral(profile.CueVolume)}, {ScriptCodeGenerator.DoubleLiteral(profile.MainVolume)}, {(profile.DimEnabled ? "true" : "false")}, {ScriptCodeGenerator.DoubleLiteral(profile.DimAmountDb)}));");

        sb.AppendLine("api.Log(\"Project script applied.\");");
        return sb.ToString();
    }

    private void EmitTrackCreate(StringBuilder sb, Track track, ExportScriptOptions options)
    {
        var varName = TrackVar(track.Id);
        var color = ScriptCodeGenerator.StringLiteral(track.ColorKey);
        var id = options.PreserveStableIds ? ScriptCodeGenerator.GuidLiteral(track.Id) : "Guid.NewGuid()";
        switch (track.Kind)
        {
            case TrackKind.Master:
                sb.AppendLine($"var {varName} = {id};");
                sb.AppendLine($"api.AddMasterTrackWithId({varName}, {ScriptCodeGenerator.StringLiteral(track.Name)});");
                break;
            case TrackKind.Instrument:
                sb.AppendLine($"var {varName} = {id};");
                sb.AppendLine($"api.AddInstrumentTrackWithId({varName}, {ScriptCodeGenerator.StringLiteral(track.Name)}, {color});");
                break;
            case TrackKind.Audio:
                sb.AppendLine($"var {varName} = {id};");
                sb.AppendLine($"api.AddAudioTrackWithId({varName}, {ScriptCodeGenerator.StringLiteral(track.Name)}, {color});");
                break;
            case TrackKind.Group:
                sb.AppendLine($"var {varName} = {id};");
                sb.AppendLine($"api.AddGroupTrackWithId({varName}, {ScriptCodeGenerator.StringLiteral(track.Name)}, {color});");
                break;
            case TrackKind.Return:
                sb.AppendLine($"var {varName} = {id};");
                sb.AppendLine($"api.AddReturnTrackWithId({varName}, {ScriptCodeGenerator.StringLiteral(track.Name)}, {color});");
                break;
            case TrackKind.Hybrid:
                sb.AppendLine($"var {varName} = {id};");
                sb.AppendLine($"api.AddHybridTrackWithId({varName}, {ScriptCodeGenerator.StringLiteral(track.Name)}, {color});");
                break;
            default:
                sb.AppendLine($"var {varName} = {id};");
                sb.AppendLine($"api.AddAudioTrackWithId({varName}, {ScriptCodeGenerator.StringLiteral(track.Name)}, {color});");
                break;
        }
    }

    private void EmitTrackState(StringBuilder sb, Track track)
    {
        var v = TrackVar(track.Id);
        if (track.IsMuted) sb.AppendLine($"api.SetTrackMuted({v}, true);");
        if (track.IsSoloed) sb.AppendLine($"api.SetTrackSoloed({v}, true);");
        if (track.IsArmed) sb.AppendLine($"api.SetTrackArmed({v}, true);");
        if (Math.Abs(track.Volume - Track.DefaultVolume) > 1e-6)
            sb.AppendLine($"api.SetTrackVolume({v}, {ScriptCodeGenerator.DoubleLiteral(track.Volume)});");
        if (Math.Abs(track.Pan) > 1e-6)
            sb.AppendLine($"api.SetTrackPan({v}, {ScriptCodeGenerator.DoubleLiteral(track.Pan)});");
        if (track.ParentId is Guid pid)
            sb.AppendLine($"api.SetTrackParent({v}, {ScriptCodeGenerator.GuidLiteral(pid)});");
        if (Math.Abs(track.SurroundWidth - 1.0) > 1e-6)
            sb.AppendLine($"api.SetTrackSurroundWidth({v}, {ScriptCodeGenerator.DoubleLiteral(track.SurroundWidth)});");
        if (track.DrumMapId is Guid dm)
            sb.AppendLine($"api.SetTrackDrumMapId({v}, {ScriptCodeGenerator.GuidLiteral(dm)});");

        foreach (var send in track.Sends)
        {
            sb.AppendLine($"api.AddSend({v}, {ScriptCodeGenerator.GuidLiteral(send.Id)}, {ScriptCodeGenerator.GuidLiteral(send.TargetTrackId)}, {ScriptCodeGenerator.DoubleLiteral(send.Level)}, {(send.PreFader ? "true" : "false")}, {(send.Enabled ? "true" : "false")});");
        }
    }

    private void EmitDevices(StringBuilder sb, Track track, ExportScriptOptions options)
    {
        var v = TrackVar(track.Id);
        for (var i = 0; i < track.Instruments.Count; i++)
        {
            var slot = track.Instruments[i];
            if (i > 0 || slot.Instrument.TypeId != InstrumentRegistry.DefaultInstrumentId || track.Instruments.Count > 1)
                ComponentScriptEmitter.EmitInstrumentSlot(sb, v, i, slot, options);
        }

        ComponentScriptEmitter.EmitEffectChain(sb, v, track.Effects, -1, options);
    }

    private void EmitClips(StringBuilder sb, Track track, ExportScriptOptions options)
    {
        var tv = TrackVar(track.Id);
        foreach (var clip in track.Clips)
        {
            var clipId = options.PreserveStableIds ? ScriptCodeGenerator.GuidLiteral(clip.Id) : "Guid.NewGuid()";
            if (clip.IsAudio)
            {
                if (clip.Samples is not null && string.IsNullOrEmpty(clip.AudioFilePath))
                    sb.AppendLine("// TODO: recorded audio clip — PCM omitted; re-record or replace");
                var meta = new ScriptAudioClipMetadata(
                    clip.AudioFilePath,
                    clip.SourceOffsetSeconds,
                    clip.SourceLengthSeconds ?? 0,
                    clip.SourceTempo,
                    clip.SourceKey,
                    clip.StretchToTempo,
                    clip.PitchCorrected,
                    clip.WarpMode.ToString(),
                    clip.WarpMarkers.Select(w => new ScriptWarpMarker(w.SourceSeconds, w.BeatPosition)).ToArray(),
                    clip.UserFadeInBeats,
                    clip.UserFadeOutBeats,
                    clip.HasAraRegion,
                    (int)clip.AraPitchOffsetSemitones);
                sb.AppendLine($"var clip_{clip.Id:N} = {clipId};");
                sb.AppendLine($"api.CreateAudioClipWithId(clip_{clip.Id:N}, {tv}, {ScriptCodeGenerator.StringLiteral(clip.Name)}, {ScriptCodeGenerator.DoubleLiteral(clip.StartBeat)}, {ScriptCodeGenerator.DoubleLiteral(clip.LengthBeats)}, new ScriptAudioClipMetadata({ScriptCodeGenerator.StringLiteral(meta.AudioFilePath)}, {ScriptCodeGenerator.DoubleLiteral(meta.SourceOffsetSeconds)}, {ScriptCodeGenerator.DoubleLiteral(meta.SourceLengthSeconds)}, {(meta.SourceTempo.HasValue ? ScriptCodeGenerator.DoubleLiteral(meta.SourceTempo.Value) : "null")}, {ScriptCodeGenerator.StringLiteral(meta.SourceKey)}, {(meta.StretchToTempo ? "true" : "false")}, {(meta.PitchCorrected ? "true" : "false")}, {ScriptCodeGenerator.StringLiteral(meta.WarpMode)}));");
            }
            else
            {
                sb.AppendLine($"var clip_{clip.Id:N} = {clipId};");
                sb.AppendLine($"api.CreateMidiClipWithId(clip_{clip.Id:N}, {tv}, {ScriptCodeGenerator.StringLiteral(clip.Name)}, {ScriptCodeGenerator.DoubleLiteral(clip.StartBeat)}, {ScriptCodeGenerator.DoubleLiteral(clip.LengthBeats)});");
                EmitMidiNotes(sb, clip, options);
                foreach (var cc in clip.ControlChanges)
                    sb.AppendLine($"api.AddMidiControlChange(clip_{clip.Id:N}, new ScriptMidiControlChange({cc.Controller}, {cc.Value}, {ScriptCodeGenerator.DoubleLiteral(cc.StartBeat)}, {ScriptCodeGenerator.DoubleLiteral(cc.LengthBeats)}));");
            }

            if (clip.LinkedClipGroupId is Guid g)
                sb.AppendLine($"api.SetClipLinkedGroup(clip_{clip.Id:N}, {ScriptCodeGenerator.GuidLiteral(g)});");
        }
    }

    private static void EmitMidiNotes(StringBuilder sb, Clip clip, ExportScriptOptions options)
    {
        if (clip.Notes.Count == 0) return;
        var batch = new List<string>();
        foreach (var n in clip.Notes)
        {
            batch.Add($"new ScriptMidiNote({n.Note}, {ScriptCodeGenerator.DoubleLiteral(n.StartBeat)}, {ScriptCodeGenerator.DoubleLiteral(n.LengthBeats)}, {n.Velocity}f)");
            if (batch.Count >= options.MaxNotesPerBatch)
            {
                sb.AppendLine($"api.AddMidiNotes(clip_{clip.Id:N}, new[] {{ {string.Join(", ", batch)} }});");
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            sb.AppendLine($"api.AddMidiNotes(clip_{clip.Id:N}, new[] {{ {string.Join(", ", batch)} }});");
    }

    private static void EmitAutomation(StringBuilder sb, Track track)
    {
        var v = TrackVarStatic(track.Id);
        foreach (var lane in track.AutoLanes)
        {
            if (lane.Binding is null) continue;
            var binding = $"new ScriptAutomationBinding(ScriptAutomationTargetKind.{lane.Binding.Kind}, {lane.Binding.EffectIndex}, {lane.Binding.ParamIndex})";
            sb.AppendLine($"api.AddAutomationLane({v}, {binding});");
            foreach (var p in lane.Points)
                sb.AppendLine($"api.AddAutomationPoint({v}, {binding}, new ScriptAutomationPoint({ScriptCodeGenerator.DoubleLiteral(p.Beat)}, {ScriptCodeGenerator.DoubleLiteral(p.Value)}, {ScriptCodeGenerator.DoubleLiteral(p.Curve)}));");
        }
    }

    private static void EmitPattern(StringBuilder sb, Pattern pattern)
    {
        sb.AppendLine($"api.AddPatternWithId({ScriptCodeGenerator.GuidLiteral(pattern.Id)}, {ScriptCodeGenerator.StringLiteral(pattern.Name)}, {ScriptCodeGenerator.DoubleLiteral(pattern.LengthBeats)}, {pattern.ColorIndex});");
        foreach (var ch in pattern.Channels)
        {
            var steps = pattern.StepSequences.FirstOrDefault(s => s.PatternChannelId == ch.Id);
            sb.AppendLine($"api.AddPatternChannel({ScriptCodeGenerator.GuidLiteral(pattern.Id)}, new ScriptPatternChannelInfo({ScriptCodeGenerator.GuidLiteral(ch.Id)}, {ch.Order}, ScriptPatternRowSourceKind.{ch.SourceKind}, {ScriptCodeGenerator.GuidLiteral(ch.TrackId)}, {(ch.SampleClipId.HasValue ? ScriptCodeGenerator.GuidLiteral(ch.SampleClipId.Value) : "null")}, {ScriptCodeGenerator.StringLiteral(ch.Name)}, {(ch.Muted ? "true" : "false")}, {ScriptCodeGenerator.DoubleLiteral(ch.Volume)}, {ScriptCodeGenerator.DoubleLiteral(ch.Pan)}));");
            if (steps is not null && steps.Steps.Count > 0)
            {
                var stepLiterals = steps.Steps.Select(s =>
                    $"new ScriptStepData({(s.Active ? "true" : "false")}, {s.Note}, {s.Velocity}f, {s.Pan}f, {s.Probability}f, {s.MicroTimingTicks})");
                sb.AppendLine($"api.SetPatternSteps({ScriptCodeGenerator.GuidLiteral(pattern.Id)}, {ScriptCodeGenerator.GuidLiteral(ch.Id)}, new[] {{ {string.Join(", ", stepLiterals)} }});");
            }
        }
    }

    private static void EmitSessionClip(StringBuilder sb, SessionClip sc)
    {
        sb.AppendLine($"api.AddSessionClip(new ScriptSessionClipInfo({ScriptCodeGenerator.GuidLiteral(sc.Id)}, {ScriptCodeGenerator.GuidLiteral(sc.TrackId)}, {sc.SceneIndex}, {ScriptCodeGenerator.StringLiteral(sc.Name)}, {ScriptCodeGenerator.DoubleLiteral(sc.LengthBeats)}, ScriptSessionLaunchMode.{sc.LaunchMode}, ScriptFollowAction.{sc.FollowAction}, {ScriptCodeGenerator.DoubleLiteral(sc.LaunchQuantizeBeats)}, {(sc.SourceClipId.HasValue ? ScriptCodeGenerator.GuidLiteral(sc.SourceClipId.Value) : "null")}));");
    }

    private static void EmitDrumMap(StringBuilder sb, DrumMap map)
    {
        var entries = string.Join(", ", map.Entries.Select(e =>
            $"new ScriptDrumMapEntryInfo({e.Note}, {ScriptCodeGenerator.StringLiteral(e.Label)}, {(e.SampleClipId.HasValue ? ScriptCodeGenerator.GuidLiteral(e.SampleClipId.Value) : "null")}, {ScriptCodeGenerator.DoubleLiteral(e.VelocityScale)})"));
        sb.AppendLine($"api.AddDrumMap(new ScriptDrumMapInfo({ScriptCodeGenerator.GuidLiteral(map.Id)}, {ScriptCodeGenerator.StringLiteral(map.Name)}, new[] {{ {entries} }}));");
    }

    private static void EmitExpressionMap(StringBuilder sb, VstExpressionMap map)
    {
        var entries = string.Join(", ", map.Entries.Select(e =>
            $"new ScriptExpressionMapEntryInfo({ScriptCodeGenerator.StringLiteral(e.Articulation)}, {e.KeyswitchNote}, {e.CcNumber}, {e.CcValue})"));
        sb.AppendLine($"api.AddExpressionMap(new ScriptExpressionMapInfo({ScriptCodeGenerator.StringLiteral(map.Name)}, new[] {{ {entries} }}));");
    }

    private string TrackVar(Guid id)
    {
        if (!_trackVars.TryGetValue(id, out var name))
        {
            name = "track_" + id.ToString("N")[..8];
            _trackVars[id] = name;
        }

        return name;
    }

    private static string TrackVarStatic(Guid id) => "track_" + id.ToString("N")[..8];
}
