using System;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Offline-freeze a track and restore its pre-freeze state on unfreeze.</summary>
public static class TrackFreezeService
{
    public static Clip FreezeTrack(Project project, Track track, AudioFormat format, double bpm, int bitDepth = 24)
    {
        if (track.FreezeBackup is null)
            track.FreezeBackup = CaptureSnapshot(track);

        var startBeat = track.Clips.Count > 0 ? track.Clips.Min(c => c.StartBeat) : 0;
        var endBeat = track.Clips.Count > 0
            ? track.Clips.Max(c => c.EndBeat)
            : project.BarCount * Math.Max(1, project.TimeSignature.Numerator);
        var lengthBeats = Math.Max(0.25, endBeat - startBeat);

        var stemProject = ExportService.CloneProjectForTrackExport(project, track);
        var temp = Path.Combine(Path.GetTempPath(), $"ongenet-freeze-{Guid.NewGuid():N}.wav");
        try
        {
            new OfflineRenderer().RenderToWav(stemProject, format, bpm, temp, bitDepth: bitDepth,
                regionStartBeat: startBeat, regionEndBeat: endBeat);
            using var stream = File.OpenRead(temp);
            var buffer = WavParser.Parse(stream);

            var clip = new Clip
            {
                Name = $"{track.Name} (frozen)",
                StartBeat = startBeat,
                LengthBeats = lengthBeats,
                IsAudio = true,
                Samples = buffer,
                Waveform = AudioWaveform.Build(buffer)
            };

            track.Clips.Clear();
            track.Clips.Add(clip);
            track.Kind = TrackKind.Audio;
            track.IsFrozen = true;
            track.Instruments.Clear();
            track.Effects.Clear();
            track.MidiEffects.Clear();
            track.CommitInstruments();
            track.CommitEffects();
            track.CommitMidiEffects();
            return clip;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    public static void UnfreezeTrack(Track track)
    {
        if (track.FreezeBackup is not { } snap) { track.IsFrozen = false; return; }

        track.Clips.Clear();
        track.Clips.AddRange(snap.Clips);

        track.Instruments.Clear();
        track.Instruments.AddRange(snap.Instruments);

        track.Effects.Clear();
        track.Effects.AddRange(snap.Effects);

        track.MidiEffects.Clear();
        track.MidiEffects.AddRange(snap.MidiEffects);

        track.Kind = snap.Kind;
        track.IsFrozen = false;
        track.FreezeBackup = null;
        track.CommitInstruments();
        track.CommitEffects();
        track.CommitMidiEffects();
    }

    private static FreezeSnapshot CaptureSnapshot(Track track)
    {
        var snap = new FreezeSnapshot { Kind = track.Kind };
        snap.Clips.AddRange(track.Clips);
        snap.Instruments.AddRange(track.Instruments);
        snap.Effects.AddRange(track.Effects);
        snap.MidiEffects.AddRange(track.MidiEffects);
        return snap;
    }
}
