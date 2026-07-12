using System;
using System.IO;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Offline bounce helpers including bounce-in-place on the source track.</summary>
public static class BounceInPlaceService
{
    /// <summary>
    /// Renders <paramref name="clip"/> through the full scoped FX chain and replaces it with the
    /// resulting audio on the same track at the same timeline position.
    /// </summary>
    public static Clip BounceClipInPlace(Project project, Track track, Clip clip, AudioFormat format, double bpm,
        int bitDepth = 24)
    {
        var scope = ClipRenderScope.ForClip(project, track, clip);
        var buffer = new OfflineRenderer().RenderScopeToBuffer(project, format, bpm, scope);

        var baked = new Clip
        {
            Name = clip.Name,
            StartBeat = clip.StartBeat,
            LengthBeats = clip.LengthBeats,
            IsAudio = true,
            Samples = buffer,
            Waveform = AudioWaveform.Build(buffer)
        };

        var idx = track.Clips.IndexOf(clip);
        if (idx >= 0) track.Clips[idx] = baked;
        else track.Clips.Add(baked);

        if (track.Kind == TrackKind.Instrument || track.Kind == TrackKind.Midi)
            track.Kind = TrackKind.Audio;

        return baked;
    }
}
