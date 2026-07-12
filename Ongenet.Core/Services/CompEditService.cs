using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Comp take-lane editing: promote, flatten, and split at playhead.</summary>
public static class CompEditService
{
    private const double DefaultCrossfadeBeats = 0.05;

    /// <summary>Replaces the track content with the selected take's clip and clears take lanes.</summary>
    public static Clip? PromoteTake(Track track, TakeLane lane)
    {
        var take = lane.Takes.FirstOrDefault(t => t.IsSelected) ?? lane.Takes.LastOrDefault();
        if (take is null) return null;

        var clip = track.Clips.FirstOrDefault(c => c.Id == take.ClipId);
        if (clip is null) return null;

        var keepId = clip.Id;
        track.Clips.RemoveAll(c => c.Id != keepId);
        clip.StartBeat = take.StartBeat;
        clip.LengthBeats = take.LengthBeats;
        track.TakeLanes.Clear();
        track.ActiveTakeLaneId = null;
        return clip;
    }

    /// <summary>Stitches take regions into one audio clip (warp-aware bake) and removes the take lane.</summary>
    public static Clip? FlattenComp(Track track, TakeLane lane, double bpm, int sampleRate = 48000, int channels = 2)
    {
        if (lane.Takes.Count == 0) return null;

        var takes = lane.Takes.OrderBy(t => t.StartBeat).ToList();
        var startBeat = takes.Min(t => t.StartBeat);
        var endBeat = takes.Max(t => t.StartBeat + t.LengthBeats);
        var lengthBeats = endBeat - startBeat;
        if (lengthBeats <= 0) return null;

        var refClip = ResolveClip(track, takes[0]);
        if (refClip?.Samples is null) return null;

        sampleRate = refClip.Samples.SampleRate > 0 ? refClip.Samples.SampleRate : sampleRate;
        channels = Math.Max(1, refClip.Samples.Channels);

        var samplesPerBeat = bpm > 0 ? sampleRate * 60.0 / bpm : sampleRate;
        var totalFrames = Math.Max(1, (int)Math.Ceiling(lengthBeats * samplesPerBeat));
        var data = new float[totalFrames * channels];

        for (var i = 0; i < takes.Count; i++)
        {
            var take = takes[i];
            var srcClip = ResolveClip(track, take);
            if (srcClip?.Samples is not { } src) continue;

            var baked = NeedsBake(srcClip)
                ? ClipBake.Bake(srcClip, bpm, sampleRate, channels)
                : src;

            var fadeIn = i > 0 ? DefaultCrossfadeBeats : 0;
            var fadeOut = i < takes.Count - 1 ? DefaultCrossfadeBeats : 0;
            var localStart = (int)Math.Round((take.StartBeat - startBeat) * samplesPerBeat);
            CopyTakeRegion(baked, srcClip, take, data, channels, localStart, bpm, fadeIn, fadeOut);
        }

        var flattened = new Clip
        {
            Name = track.Name,
            IsAudio = true,
            StartBeat = startBeat,
            LengthBeats = lengthBeats,
            Samples = new AudioSampleBuffer(data, channels, sampleRate),
            Waveform = AudioWaveform.Build(new AudioSampleBuffer(data, channels, sampleRate))
        };

        track.Clips.Clear();
        track.Clips.Add(flattened);
        track.TakeLanes.Clear();
        track.ActiveTakeLaneId = null;
        return flattened;
    }

    /// <summary>Splits every take crossing <paramref name="playheadBeat"/> into two regions.</summary>
    public static void SplitAtPlayhead(TakeLane lane, double playheadBeat)
    {
        var additions = new List<Take>();

        foreach (var take in lane.Takes.ToList())
        {
            var end = take.StartBeat + take.LengthBeats;
            if (playheadBeat <= take.StartBeat + 1e-9 || playheadBeat >= end - 1e-9) continue;

            var leftLen = playheadBeat - take.StartBeat;
            var rightLen = end - playheadBeat;

            take.LengthBeats = leftLen;
            additions.Add(new Take
            {
                ClipId = take.ClipId,
                StartBeat = playheadBeat,
                LengthBeats = rightLen,
                IsSelected = take.IsSelected
            });
        }

        lane.Takes.AddRange(additions);
        lane.Takes.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
    }

    /// <summary>Returns the take lane that should receive the next recording pass.</summary>
    public static TakeLane EnsureRecordLane(Track track, bool createOnLoop = false)
    {
        if (track.ActiveTakeLaneId is { } id &&
            track.TakeLanes.FirstOrDefault(l => l.Id == id) is { } active)
            return active;

        var armed = track.TakeLanes.FirstOrDefault(l => l.IsArmedForRecord);
        if (armed is not null) return armed;

        if (track.TakeLanes.Count > 0 && !createOnLoop)
            return track.TakeLanes[0];

        var lane = new TakeLane
        {
            Name = $"Take {track.TakeLanes.Count + 1}",
            IsArmedForRecord = true
        };
        track.TakeLanes.Add(lane);
        track.ActiveTakeLaneId = lane.Id;
        foreach (var other in track.TakeLanes)
            if (other.Id != lane.Id) other.IsArmedForRecord = false;
        return lane;
    }

    /// <summary>Advances to the next take lane for loop comp recording.</summary>
    public static TakeLane AdvanceRecordLane(Track track)
    {
        foreach (var lane in track.TakeLanes) lane.IsArmedForRecord = false;
        return EnsureRecordLane(track, createOnLoop: true);
    }

    private static Clip? ResolveClip(Track track, Take take)
        => track.Clips.FirstOrDefault(c => c.Id == take.ClipId);

    private static bool NeedsBake(Clip clip)
        => clip is { IsAudio: true, StretchToTempo: true } or { WarpMarkers.Count: > 0 }
           || clip.WarpMode is not WarpMode.Beats and not WarpMode.Repitch;

    private static void CopyTakeRegion(AudioSampleBuffer src, Clip srcClip, Take take, float[] dest,
        int destChannels, int destStartFrame, double bpm, double fadeInBeats, double fadeOutBeats)
    {
        var srcChannels = src.Channels;
        var samplesPerBeat = src.SampleRate * 60.0 / bpm;
        var regionFrames = Math.Max(1, (int)Math.Round(take.LengthBeats * samplesPerBeat));
        var offsetFrames = (long)Math.Round(srcClip.SourceOffsetSeconds * src.SampleRate);

        for (var f = 0; f < regionFrames; f++)
        {
            var srcFrame = offsetFrames + f;
            if (srcFrame < 0 || srcFrame >= src.FrameCount) continue;
            var dstFrame = destStartFrame + f;
            if (dstFrame < 0 || dstFrame >= dest.Length / destChannels) continue;

            var localBeat = f / samplesPerBeat;
            var gain = Crossfade.Gain(localBeat, take.LengthBeats, fadeInBeats, fadeOutBeats);

            for (var c = 0; c < destChannels; c++)
            {
                var sc = c < srcChannels ? c : srcChannels - 1;
                dest[dstFrame * destChannels + c] += src.Sample(srcFrame, sc) * gain;
            }
        }
    }
}
