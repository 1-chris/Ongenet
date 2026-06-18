using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>
/// Renders a whole project to a WAV file offline (faster than real time), using the same mixing
/// maths as the live <see cref="AudioEngine"/>. It works on <b>clones</b> of each track's instrument
/// and effects, so rendering never disturbs live playback or shares voice/effect state.
/// </summary>
public sealed class OfflineRenderer
{
    private const int BlockFrames = 512;
    private const double TailSeconds = 2.0; // let instrument/effect tails ring out

    /// <summary>Renders the project to <paramref name="path"/> as a 16-bit PCM WAV.</summary>
    public void RenderToWav(Project project, AudioFormat format, double bpm, string path)
    {
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var sampleRate = format.SampleRate;
        var samplesPerBeat = bpm > 0 ? sampleRate * 60.0 / bpm : sampleRate;
        var beatsPerBar = Math.Max(1, project.TimeSignature.Numerator);

        // Render at least the arrangement length, extended to cover any clips beyond it.
        var contentEnd = project.Tracks.SelectMany(t => t.Clips).Select(c => c.EndBeat).DefaultIfEmpty(0).Max();
        var renderBeats = Math.Max(project.BarCount * beatsPerBar, contentEnd);
        var totalFrames = (long)(renderBeats * samplesPerBeat) + (long)(TailSeconds * sampleRate);

        var soloActive = project.Tracks.Any(t => t.IsSoloed);

        // Build per-track render state from clones + the merged, sorted note schedule.
        var renderTracks = new List<RenderTrack>();
        var events = new List<ScheduledNote>();
        foreach (var track in project.Tracks)
        {
            var rt = new RenderTrack(track);

            if (track is { Kind: TrackKind.Instrument, Instrument: { } instrument })
            {
                rt.Instrument = instrument.Clone();
                rt.Instrument.Prepare(format);
                foreach (var clip in track.Clips.Where(c => c.IsMidi))
                {
                    foreach (var note in clip.Notes)
                    {
                        var onBeat = clip.StartBeat + note.StartBeat;
                        events.Add(new ScheduledNote(onBeat, onBeat + note.LengthBeats, rt.Instrument, note.Note, note.Velocity));
                    }
                }
            }
            else if (track.Kind == TrackKind.Audio)
            {
                foreach (var clip in track.Clips)
                {
                    if (clip.Samples is not { } samples) continue;
                    var stretch = clip.StretchToTempo
                        ? TempoSync.Stretch(samples.FrameCount / (double)samples.SampleRate, bpm, clip.LengthBeats)
                        : 1.0;
                    rt.AudioClips.Add((clip.StartBeat, clip.LengthBeats, samples, stretch));
                }
            }

            rt.Effects = track.Effects.Select(e => { var c = e.Clone(); c.Prepare(format); return c; }).ToArray();
            renderTracks.Add(rt);
        }

        events.Sort((a, b) => a.OnBeat.CompareTo(b.OnBeat));

        var block = new float[BlockFrames * channels];
        var temp = new float[BlockFrames * channels];
        var active = new List<ScheduledNote>();
        var nextEvent = 0;
        var currentBeat = 0.0;
        long framesWritten = 0;

        using var writer = new WavWriter(path, channels, sampleRate);

        while (framesWritten < totalFrames)
        {
            var framesThisBlock = (int)Math.Min(BlockFrames, totalFrames - framesWritten);
            var sampleCount = framesThisBlock * channels;
            var blockSpan = block.AsSpan(0, sampleCount);
            blockSpan.Clear();

            var prevBeat = currentBeat;
            currentBeat = prevBeat + framesThisBlock / samplesPerBeat;

            // Fire note on/off for this block.
            while (nextEvent < events.Count && events[nextEvent].OnBeat < currentBeat)
            {
                var ev = events[nextEvent++];
                ev.Instrument.NoteOn(ev.Note, ev.Velocity);
                active.Add(ev);
            }

            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].OffBeat <= currentBeat)
                {
                    active[i].Instrument.NoteOff(active[i].Note);
                    active.RemoveAt(i);
                }
            }

            // Mix each track into the block.
            foreach (var rt in renderTracks)
            {
                if (rt.Source.IsMuted || (soloActive && !rt.Source.IsSoloed)) continue;

                var tempSpan = temp.AsSpan(0, sampleCount);
                tempSpan.Clear();
                var hasContent = false;

                if (rt.Instrument is not null)
                {
                    rt.Instrument.Render(tempSpan);
                    hasContent = true;
                }
                else
                {
                    foreach (var (start, length, samples, stretch) in rt.AudioClips)
                    {
                        Mixing.RenderAudioClip(tempSpan, samples, start, length, prevBeat, samplesPerBeat, sampleRate, channels, stretch);
                        hasContent = true;
                    }
                }

                if (rt.Effects.Length > 0)
                {
                    foreach (var fx in rt.Effects) if (fx.Enabled) fx.Process(tempSpan);
                    hasContent = true;
                }

                if (!hasContent) continue;

                var (leftGain, rightGain) = Mixing.StripGains(rt.Source.Volume, rt.Source.Pan);
                for (var frame = 0; frame < framesThisBlock; frame++)
                {
                    var i = frame * channels;
                    for (var c = 0; c < channels; c++)
                    {
                        blockSpan[i + c] += tempSpan[i + c] * Mixing.ChannelGain(c, leftGain, rightGain);
                    }
                }
            }

            writer.Write(blockSpan);
            framesWritten += framesThisBlock;
        }
    }

    private sealed class RenderTrack
    {
        public RenderTrack(Track source) => Source = source;
        public Track Source { get; }
        public IInstrument? Instrument { get; set; }
        public IAudioEffect[] Effects { get; set; } = Array.Empty<IAudioEffect>();
        public List<(double Start, double Length, AudioSampleBuffer Samples, double Stretch)> AudioClips { get; } = new();
    }

    private readonly record struct ScheduledNote(double OnBeat, double OffBeat, IInstrument Instrument, int Note, float Velocity);
}
