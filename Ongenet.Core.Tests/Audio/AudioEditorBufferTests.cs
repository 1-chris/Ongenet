using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;

namespace Ongenet.Core.Tests.Audio;

public sealed class AudioEditorBufferTests
{
    [Fact]
    public void TrimAndNormalize_round_trips_shared_buffer()
    {
        var samples = new float[44100];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)Math.Sin(i * 0.01);
        var buffer = new AudioSampleBuffer(samples, 1, 44100);

        var clip = new Clip
        {
            Name = "Test",
            IsAudio = true,
            Samples = buffer,
            LengthBeats = 4
        };

        var trimmed = SampleEditOps.Trim(buffer, 4410, 44100 - 4410);
        SampleEditOps.ReplaceSharedBufferSamples(new[] { clip }, buffer, trimmed);
        SampleEditorService.Normalize(trimmed);

        Assert.NotSame(buffer, clip.Samples);
        Assert.True(clip.Samples!.FrameCount < buffer.FrameCount);
        Assert.All(clip.Samples.Samples, s => Assert.InRange(Math.Abs(s), 0f, 1.01f));
    }
}
