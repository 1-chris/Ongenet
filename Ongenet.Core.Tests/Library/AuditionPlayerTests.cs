using System;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Xunit;

namespace Ongenet.Core.Tests.Library;

public class AuditionPlayerTests
{
    private static readonly AudioFormat Format = new(44100, 1);

    [Fact]
    public void Mix_PlaysThenStops()
    {
        var player = new AuditionPlayer();
        player.Play(new AudioSampleBuffer(Enumerable.Repeat(0.5f, 100).ToArray(), 1, 44100));
        Assert.True(player.IsPlaying);

        var first = new float[64];
        player.Mix(first, Format);
        Assert.Contains(first, s => Math.Abs(s) > 0.1f); // audible

        // Drain the rest of the 100-frame buffer.
        var guard = 0;
        while (player.IsPlaying && guard++ < 100)
        {
            var block = new float[64];
            player.Mix(block, Format);
        }

        Assert.False(player.IsPlaying);

        // After it ends, mixing adds nothing.
        var after = new float[64];
        player.Mix(after, Format);
        Assert.All(after, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Stop_SilencesImmediately()
    {
        var player = new AuditionPlayer();
        player.Play(new AudioSampleBuffer(Enumerable.Repeat(0.5f, 44100).ToArray(), 1, 44100));
        player.Stop();
        Assert.False(player.IsPlaying);

        var block = new float[64];
        player.Mix(block, Format);
        Assert.All(block, s => Assert.Equal(0f, s));
    }
}
