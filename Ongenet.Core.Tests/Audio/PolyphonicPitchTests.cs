using System;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class PolyphonicPitchTests
{
    [Fact]
    public void PitchSegments_RoundTripInProjectFile()
    {
        var project = new Project { Name = "V7 pitch" };
        var track = new Track { Name = "Audio", Kind = TrackKind.Audio };
        var clip = new Clip { Name = "Clip", IsAudio = true, LengthBeats = 4 };
        clip.PitchSegments.Add(new PitchNoteSegment
        {
            StartSample = 100,
            EndSample = 5000,
            PitchCents = 50,
            Amplitude = 0.8f
        });
        clip.PitchSegments.Add(new PitchNoteSegment
        {
            StartSample = 6000,
            EndSample = 12000,
            PitchCents = -25,
            Amplitude = 0.6f
        });
        track.Clips.Add(clip);
        project.Tracks.Add(track);

        using var ms = new System.IO.MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 16, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        var loadedClip = loaded.Tracks.Single().Clips.Single();
        Assert.Equal(2, loadedClip.PitchSegments.Count);
        Assert.Equal(100, loadedClip.PitchSegments[0].StartSample);
        Assert.Equal(5000, loadedClip.PitchSegments[0].EndSample);
        Assert.Equal(50, loadedClip.PitchSegments[0].PitchCents, 3);
        Assert.Equal(0.8f, loadedClip.PitchSegments[0].Amplitude, 3);
        Assert.Equal(-25, loadedClip.PitchSegments[1].PitchCents, 3);
    }

    [Fact]
    public void SegmentPitchRatio_UsesFallbackWhenNoSegmentCoversFrame()
    {
        var segments = new[]
        {
            new PitchNoteSegment { StartSample = 1000, EndSample = 2000, PitchCents = 100, Amplitude = 1f }
        };

        var ratio = AudioClipPitch.SegmentPitchRatio(500, segments, araFallbackSemitones: 12);
        Assert.Equal(MusicalMath.SemitonesToRatio(12), ratio, 4);
    }

    [Fact]
    public void SegmentPitchRatio_AppliesCentsInsideSegment()
    {
        var segments = new[]
        {
            new PitchNoteSegment { StartSample = 0, EndSample = 10_000, PitchCents = 1200, Amplitude = 1f }
        };

        var ratio = AudioClipPitch.SegmentPitchRatio(5000, segments, araFallbackSemitones: 0);
        Assert.Equal(MusicalMath.CentsToRatio(1200), ratio, 4);
    }

    [Fact]
    public void SegmentWeight_CrossfadesAtEdges()
    {
        var seg = new PitchNoteSegment
        {
            StartSample = 0,
            EndSample = AudioClipPitch.SegmentCrossfadeSamples * 4,
            PitchCents = 0,
            Amplitude = 1f
        };

        var edge = AudioClipPitch.SegmentWeight(0, seg);
        var mid = AudioClipPitch.SegmentWeight(AudioClipPitch.SegmentCrossfadeSamples * 2, seg);
        Assert.True(edge < mid);
        Assert.True(mid > 0.9);
    }

    [Fact]
    public void NeedsShifters_WhenPitchSegmentsPresent()
    {
        var clip = new Clip();
        clip.PitchSegments.Add(new PitchNoteSegment { StartSample = 0, EndSample = 100 });
        Assert.True(AudioClipPitch.NeedsShifters(clip, warp: null));
    }

    [Fact]
    public void PitchSegments_ChangeBakedOutput()
    {
        const int sampleRate = 44100;
        const int channels = 1;
        const double bpm = 120;
        const int frames = sampleRate;
        var tone = new float[frames];
        for (var i = 0; i < frames; i++)
            tone[i] = (float)Math.Sin(2 * Math.PI * 440.0 * i / sampleRate);

        var samples = new AudioSampleBuffer(tone, channels, sampleRate);
        var clip = new Clip { Name = "Tone", IsAudio = true, LengthBeats = 2, Samples = samples };
        var flat = ClipBake.Bake(clip, bpm, sampleRate, channels);

        clip.PitchSegments.Add(new PitchNoteSegment
        {
            StartSample = 0,
            EndSample = frames,
            PitchCents = 1200,
            Amplitude = 1f
        });
        var shifted = ClipBake.Bake(clip, bpm, sampleRate, channels);

        var diff = 0.0;
        for (var i = 0; i < flat.Samples.Length; i++)
            diff += Math.Abs(flat.Samples[i] - shifted.Samples[i]);
        Assert.True(diff / flat.Samples.Length > 0.01, "Pitch segments should alter baked audio");
    }
}
