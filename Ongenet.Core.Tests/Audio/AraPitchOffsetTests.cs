using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class AraPitchOffsetTests
{
    [Fact]
    public void AraPitchOffset_ChangesBakedOutput()
    {
        const int sampleRate = 44100;
        const int channels = 1;
        const double bpm = 120;
        const int frames = sampleRate;
        var tone = new float[frames];
        for (var i = 0; i < frames; i++)
            tone[i] = (float)Math.Sin(2 * Math.PI * 440.0 * i / sampleRate);

        var samples = new AudioSampleBuffer(tone, channels, sampleRate);
        var clip = new Clip { Name = "Tone", IsAudio = true, LengthBeats = 2, Samples = samples, AraPitchOffsetSemitones = 0 };
        var flat = ClipBake.Bake(clip, bpm, sampleRate, channels);
        clip.AraPitchOffsetSemitones = 12;
        var shifted = ClipBake.Bake(clip, bpm, sampleRate, channels);

        Assert.Equal(flat.Samples.Length, shifted.Samples.Length);
        var diff = 0.0;
        for (var i = 0; i < flat.Samples.Length; i++)
            diff += Math.Abs(flat.Samples[i] - shifted.Samples[i]);
        Assert.True(diff / flat.Samples.Length > 0.01, "ARA pitch offset should alter baked audio");
    }

    [Fact]
    public void AudioClipPitch_NeedsShifters_WhenAraOffsetSet()
    {
        var clip = new Clip { AraPitchOffsetSemitones = 3 };
        Assert.True(AudioClipPitch.NeedsShifters(clip, warp: null));
    }
}
