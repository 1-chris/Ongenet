using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;

namespace Ongenet.Core.Services;

/// <summary>Runtime visibility/opacity state for video layers driven by triggers.</summary>
public sealed class VideoCompositionRuntime
{
    private readonly Dictionary<Guid, double> _opacity = new();
    private readonly Dictionary<Guid, double> _fadeTarget = new();
    private readonly Dictionary<Guid, double> _fadeSpeed = new();
    private readonly Dictionary<Guid, bool> _regionGated = new();

    private static double RegionOpacityAtBeat(VideoVisibilityRegion region, double beat, double layerOpacity)
    {
        if (beat < region.StartBeat || beat >= region.EndBeat) return 0;
        var opacity = layerOpacity;
        if (region.FadeInBeats > 1e-6 && beat < region.StartBeat + region.FadeInBeats)
        {
            var t = (beat - region.StartBeat) / region.FadeInBeats;
            opacity *= Math.Clamp(t, 0, 1);
        }

        if (region.FadeOutBeats > 1e-6 && beat >= region.EndBeat - region.FadeOutBeats)
        {
            var t = (region.EndBeat - beat) / region.FadeOutBeats;
            opacity *= Math.Clamp(t, 0, 1);
        }

        return opacity;
    }

    private static double ComputeRegionOpacity(Project project, VideoLayer layer, double beat)
    {
        var regions = project.VideoVisibilityRegions.Where(r => r.LayerId == layer.Id).ToList();
        if (regions.Count == 0) return layer.DefaultVisible ? layer.Opacity : 0;
        var max = 0.0;
        foreach (var region in regions)
            max = Math.Max(max, RegionOpacityAtBeat(region, beat, layer.Opacity));
        return max;
    }

    public void Reset(Project project, double beat)
    {
        _opacity.Clear();
        _fadeTarget.Clear();
        _fadeSpeed.Clear();
        _regionGated.Clear();
        foreach (var layer in project.VideoLayers)
        {
            var regions = project.VideoVisibilityRegions.Where(r => r.LayerId == layer.Id).ToList();
            var inRegion = regions.Count == 0 || regions.Any(r => beat >= r.StartBeat && beat < r.EndBeat);
            _regionGated[layer.Id] = inRegion;
            _opacity[layer.Id] = inRegion
                ? ComputeRegionOpacity(project, layer, beat)
                : 0;
        }
    }

    public void ApplyRegionGate(Project project, double beat)
    {
        foreach (var layer in project.VideoLayers)
        {
            var regions = project.VideoVisibilityRegions.Where(r => r.LayerId == layer.Id).ToList();
            if (regions.Count == 0)
            {
                _regionGated[layer.Id] = true;
                if (!_opacity.ContainsKey(layer.Id))
                    _opacity[layer.Id] = layer.DefaultVisible ? layer.Opacity : 0;
                continue;
            }

            var inRegion = regions.Any(r => beat >= r.StartBeat && beat < r.EndBeat);
            _regionGated[layer.Id] = inRegion;
            if (!inRegion)
            {
                _fadeTarget.Remove(layer.Id);
                _fadeSpeed.Remove(layer.Id);
                _opacity[layer.Id] = 0;
            }
            else if (!_fadeTarget.ContainsKey(layer.Id))
                _opacity[layer.Id] = ComputeRegionOpacity(project, layer, beat);
        }
    }

    public double GetOpacity(Guid layerId) =>
        _opacity.TryGetValue(layerId, out var o) ? o : 0;

    public void Tick(double deltaSeconds)
    {
        foreach (var id in _fadeTarget.Keys.ToArray())
        {
            if (!_opacity.TryGetValue(id, out var cur)) continue;
            if (_regionGated.TryGetValue(id, out var gated) && !gated) continue;
            var target = _fadeTarget[id];
            var speed = _fadeSpeed[id];
            if (Math.Abs(cur - target) < 1e-4)
            {
                _opacity[id] = target;
                _fadeTarget.Remove(id);
                _fadeSpeed.Remove(id);
                continue;
            }

            var step = speed * deltaSeconds;
            _opacity[id] = cur < target ? Math.Min(target, cur + step) : Math.Max(target, cur - step);
        }
    }

    public void ApplyTrigger(VideoTrigger trigger, VideoLayer layer, double nowSeconds)
    {
        var id = layer.Id;
        if (_regionGated.TryGetValue(id, out var gated) && !gated) return;
        switch (trigger.Action)
        {
            case VideoTriggerAction.Show:
                _fadeTarget.Remove(id);
                _fadeSpeed.Remove(id);
                _opacity[id] = layer.Opacity;
                break;
            case VideoTriggerAction.Hide:
                _fadeTarget.Remove(id);
                _fadeSpeed.Remove(id);
                _opacity[id] = 0;
                break;
            case VideoTriggerAction.Toggle:
                var visible = GetOpacity(id) > 0.01;
                if (visible) _opacity[id] = 0;
                else _opacity[id] = layer.Opacity;
                break;
            case VideoTriggerAction.FadeIn:
                StartFade(id, layer.Opacity, trigger.FadeDurationSeconds);
                break;
            case VideoTriggerAction.FadeOut:
                StartFade(id, 0, trigger.FadeDurationSeconds);
                break;
        }
    }

    private void StartFade(Guid id, double target, double duration)
    {
        if (!_opacity.ContainsKey(id)) _opacity[id] = 0;
        _fadeTarget[id] = target;
        _fadeSpeed[id] = duration > 1e-6
            ? Math.Abs(target - _opacity[id]) / duration
            : Math.Abs(target - _opacity[id]) * 1000;
    }
}

/// <summary>Evaluates clip/MIDI triggers against the project.</summary>
public sealed class VideoTriggerEngine
{
    private double _lastBeat;
    private readonly HashSet<(Guid clipId, VideoTriggerMoment moment)> _fired = new();

    public VideoCompositionRuntime Runtime { get; } = new();

    public void Reset(Project project)
    {
        _lastBeat = 0;
        _fired.Clear();
        Runtime.Reset(project, 0);
    }

    public void Tick(Project project, double prevBeat, double curBeat, double deltaSeconds)
    {
        Runtime.ApplyRegionGate(project, curBeat);
        Runtime.Tick(deltaSeconds);
        if (Math.Abs(curBeat - prevBeat) > 1e-9)
            ProcessArrangementCrossings(project, prevBeat, curBeat);
        _lastBeat = curBeat;
    }

    public void OnSessionClipEvent(Project project, Guid sessionClipId, VideoTriggerMoment moment)
    {
        foreach (var trigger in project.VideoTriggers.Where(t =>
                     t.Source == VideoTriggerSource.SessionClip && t.ClipId == sessionClipId && t.Moment == moment))
        {
            var layer = project.VideoLayers.FirstOrDefault(e => e.Id == trigger.TargetLayerId);
            if (layer is not null) Runtime.ApplyTrigger(trigger, layer, 0);
        }
    }

    public void OnMidiNote(Project project, int note, bool on)
    {
        var moment = on ? VideoTriggerMoment.NoteOn : VideoTriggerMoment.NoteOff;
        foreach (var trigger in project.VideoTriggers.Where(t =>
                     t.Source == VideoTriggerSource.MidiNote && t.MidiNote == note && t.Moment == moment))
        {
            var layer = project.VideoLayers.FirstOrDefault(e => e.Id == trigger.TargetLayerId);
            if (layer is not null) Runtime.ApplyTrigger(trigger, layer, 0);
        }
    }

    public void OnMidiCc(Project project, int channel, int cc, int value)
    {
        foreach (var trigger in project.VideoTriggers.Where(t =>
                     t.Source == VideoTriggerSource.MidiCc
                     && t.MidiCcChannel == channel
                     && t.MidiCcNumber == cc
                     && value >= t.MidiCcThreshold))
        {
            var layer = project.VideoLayers.FirstOrDefault(e => e.Id == trigger.TargetLayerId);
            if (layer is not null) Runtime.ApplyTrigger(trigger, layer, 0);
        }
    }

    private void ProcessArrangementCrossings(Project project, double prevBeat, double curBeat)
    {
        foreach (var track in project.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                CheckCrossing(project, clip.Id, clip.StartBeat, VideoTriggerMoment.ClipStart, prevBeat, curBeat);
                CheckCrossing(project, clip.Id, clip.EndBeat, VideoTriggerMoment.ClipEnd, prevBeat, curBeat);
            }
        }
    }

    private void CheckCrossing(Project project, Guid clipId, double beat, VideoTriggerMoment moment,
        double prevBeat, double curBeat)
    {
        if (!Crossed(prevBeat, curBeat, beat)) return;
        var key = (clipId, moment);
        if (!_fired.Add(key)) return;

        foreach (var trigger in project.VideoTriggers.Where(t =>
                     t.Source == VideoTriggerSource.ArrangementClip && t.ClipId == clipId && t.Moment == moment))
        {
            var layer = project.VideoLayers.FirstOrDefault(e => e.Id == trigger.TargetLayerId);
            if (layer is not null) Runtime.ApplyTrigger(trigger, layer, 0);
        }
    }

    private static bool Crossed(double prev, double cur, double point)
    {
        if (cur >= prev) return prev < point && cur >= point;
        return prev >= point && cur < point;
    }

    public void Seek(Project project, double beat)
    {
        _lastBeat = beat;
        _fired.Clear();
        Runtime.Reset(project, beat);
    }
}
