using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

public sealed partial class ScriptingApi
{
    public IReadOnlyList<ScriptAutomationLaneInfo> GetAutomationLanes(Guid trackId)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        return track.AutoLanes.Select(lane => new ScriptAutomationLaneInfo(
            ToScriptBinding(lane.Binding),
            lane.IsArmed,
            lane.Points.Select(p => new ScriptAutomationPoint(p.Beat, p.Value, p.Curve)).ToArray())).ToArray();
    }

    public void AddAutomationLane(Guid trackId, ScriptAutomationBinding binding)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        var modelBinding = ToModelBinding(binding);
        if (track.AutoLanes.Any(l => l.Binding == modelBinding)) return;
        var target = ProjectFile.BuildTarget(track, (int)binding.Kind, binding.EffectIndex, binding.ParamIndex, _project.Current);
        if (target is null) throw new InvalidOperationException("Could not bind automation target.");
        _history.Capture("Add automation lane");
        var lane = new AutomationLane(target) { Binding = modelBinding };
        track.AutoLanes.Add(lane);
    }

    public void ClearAutomationLane(Guid trackId, ScriptAutomationBinding binding)
    {
        var track = FindTrack(trackId);
        if (track is null) return;
        var modelBinding = ToModelBinding(binding);
        var lane = track.AutoLanes.FirstOrDefault(l => l.Binding == modelBinding);
        if (lane is null) return;
        _history.Capture("Clear automation lane");
        lane.Points.Clear();
    }

    public void AddAutomationPoint(Guid trackId, ScriptAutomationBinding binding, ScriptAutomationPoint point)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        var modelBinding = ToModelBinding(binding);
        var lane = track.AutoLanes.FirstOrDefault(l => l.Binding == modelBinding);
        if (lane is null)
        {
            AddAutomationLane(trackId, binding);
            lane = track.AutoLanes.First(l => l.Binding == modelBinding);
        }

        _history.Capture("Add automation point");
        lane.AddPoint(new AutomationPoint(point.Beat, point.Value, point.Curve));
    }

    public void AddTrackModulator(Guid trackId, ScriptTrackModulatorInfo modulator)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        _history.Capture("Add track modulator");
        track.Modulators.Add(new TrackModulator
        {
            Id = modulator.Id,
            Kind = modulator.Kind switch
            {
                ScriptModulatorKind.EnvelopeFollower => TrackModulatorKind.EnvelopeFollower,
                _ => TrackModulatorKind.Lfo
            },
            Enabled = modulator.Enabled,
            RateHz = modulator.RateHz,
            Depth = modulator.Depth,
            Wave = Enum.TryParse<LfoWave>(modulator.Wave.ToString(), out var w) ? w : LfoWave.Sine,
            Target = ToModelBinding(modulator.Target),
            TempoSync = modulator.TempoSync,
            AttackSeconds = modulator.AttackSeconds,
            ReleaseSeconds = modulator.ReleaseSeconds
        });
    }

    public IReadOnlyList<ScriptTrackSendInfo> GetSends(Guid trackId)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        return track.Sends.Select(s => new ScriptTrackSendInfo(s.Id, s.TargetTrackId, s.Level, s.PreFader, s.Enabled)).ToArray();
    }

    public void AddSend(Guid trackId, Guid sendId, Guid targetTrackId, double level, bool preFader, bool enabled)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        if (track.Sends.Any(s => s.Id == sendId)) return;
        _history.Capture("Add send");
        track.Sends.Add(new TrackSend
        {
            Id = sendId,
            TargetTrackId = targetTrackId,
            Level = level,
            PreFader = preFader,
            Enabled = enabled
        });
    }

    public void SetSendLevel(Guid trackId, Guid sendId, double level)
    {
        var track = FindTrack(trackId);
        var send = track?.Sends.FirstOrDefault(s => s.Id == sendId);
        if (send is null) return;
        _history.Capture("Change send level");
        send.Level = Math.Clamp(level, 0, 1);
    }

    public IReadOnlyList<ScriptMultiOutputRouteInfo> GetMultiOutputRoutes() =>
        _project.Current.MultiOutputRoutes.Select(r => new ScriptMultiOutputRouteInfo(
            r.SourceTrackId, r.SlotIndex, r.PluginOutputBus, r.DestinationTrackId, r.Level)).ToArray();

    public void AddMultiOutputRoute(ScriptMultiOutputRouteInfo route)
    {
        _history.Capture("Add multi-output route");
        _project.Current.MultiOutputRoutes.Add(new MultiOutputRoute
        {
            SourceTrackId = route.SourceTrackId,
            SlotIndex = route.SlotIndex,
            PluginOutputBus = route.PluginOutputBus,
            DestinationTrackId = route.DestinationTrackId,
            Level = route.Level
        });
    }

    private static ScriptAutomationBinding ToScriptBinding(AutomationBinding? binding) =>
        binding is null
            ? new ScriptAutomationBinding(ScriptAutomationTargetKind.TrackVolume, -1, -1)
            : new ScriptAutomationBinding((ScriptAutomationTargetKind)binding.Kind, binding.EffectIndex, binding.ParamIndex);

    private static AutomationBinding ToModelBinding(ScriptAutomationBinding binding) =>
        new((AutomationTargetKind)binding.Kind, binding.EffectIndex, binding.ParamIndex);
}
