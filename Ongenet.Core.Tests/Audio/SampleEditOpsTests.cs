using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Tests.Audio;

public class SampleEditOpsTests
{
    private static AudioSampleBuffer Mono(params float[] frames)
        => new(frames, 1, 44100);

    [Fact]
    public void CopyRange_CopiesRequestedFrames()
    {
        var buffer = Mono(0f, 1f, 2f, 3f, 4f);
        var segment = SampleEditOps.CopyRange(buffer, 1, 3);

        Assert.Equal(3, segment.FrameCount);
        Assert.Equal(new[] { 1f, 2f, 3f }, segment.Samples);
    }

    [Fact]
    public void Trim_KeepsInclusiveWindow()
    {
        var buffer = Mono(0f, 1f, 2f, 3f, 4f);
        var trimmed = SampleEditOps.Trim(buffer, 1, 4);

        Assert.Equal(3, trimmed.FrameCount);
        Assert.Equal(new[] { 1f, 2f, 3f }, trimmed.Samples);
    }

    [Fact]
    public void DeleteRange_RemovesMiddle()
    {
        var buffer = Mono(0f, 1f, 2f, 3f, 4f);
        var result = SampleEditOps.DeleteRange(buffer, 2, 2);

        Assert.Equal(new[] { 0f, 1f, 4f }, result.Samples);
    }

    [Fact]
    public void InsertRange_InsertsAtPosition()
    {
        var buffer = Mono(0f, 3f);
        var insert = new AudioSegment(new[] { 1f, 2f }, 1, 44100);
        var result = SampleEditOps.InsertRange(buffer, 1, insert);

        Assert.Equal(new[] { 0f, 1f, 2f, 3f }, result.Samples);
    }

    [Fact]
    public void MoveRange_RelocatesSegment()
    {
        var buffer = Mono(0f, 1f, 2f, 3f);
        var result = SampleEditOps.MoveRange(buffer, 1, 1, 3);

        Assert.Equal(new[] { 0f, 2f, 1f, 3f }, result.Samples);
    }

    [Fact]
    public void MakeUnique_ExtractsClipWindow()
    {
        var shared = Mono(0f, 1f, 2f, 3f, 4f);
        var clip = new Clip
        {
            IsAudio = true,
            Samples = shared,
            Waveform = AudioWaveform.Build(shared),
            SourceOffsetSeconds = 2.0 / 44100.0,
            SourceLengthSeconds = 2.0 / 44100.0
        };

        Assert.True(ClipSampleOps.MakeUnique(clip));
        Assert.NotSame(shared, clip.Samples);
        Assert.Equal(new[] { 2f, 3f }, clip.Samples!.Samples);
        Assert.Equal(0.0, clip.SourceOffsetSeconds);
        Assert.NotNull(clip.SourceLengthSeconds);
    }

    [Fact]
    public void ReplaceSharedBuffer_UpdatesAllSharingClips()
    {
        var old = Mono(0f, 1f, 2f);
        var replacement = Mono(9f, 8f);
        var a = new Clip { IsAudio = true, Samples = old, Waveform = AudioWaveform.Build(old) };
        var b = new Clip
        {
            IsAudio = true,
            Samples = old,
            Waveform = AudioWaveform.Build(old),
            SourceOffsetSeconds = 1.0 / 44100.0,
            SourceLengthSeconds = 1.0 / 44100.0
        };

        SampleEditOps.ReplaceSharedBuffer(new[] { a, b }, old, replacement);

        Assert.Same(replacement, a.Samples);
        Assert.Same(replacement, b.Samples);
        Assert.Equal(1.0 / 44100.0, b.SourceOffsetSeconds);
        Assert.Equal(1.0 / 44100.0, b.SourceLengthSeconds);
    }

    private static AudioSampleBuffer Stereo(params float[] interleaved)
        => new(interleaved, 2, 44100);

    [Fact]
    public void ApplyGainRange_DoublesAmplitude()
    {
        var buffer = Mono(0.5f, 1f);
        var result = SampleEditOps.ApplyGainRange(buffer, 0, 2, 2.0);
        Assert.Equal(new[] { 1f, 2f }, result.Samples);
    }

    [Fact]
    public void ApplyPanRange_HardLeftMutesRight()
    {
        var buffer = Stereo(1f, 0f, 1f, 1f);
        var result = SampleEditOps.ApplyPanRange(buffer, 0, 2, -1.0);
        Assert.Equal(0f, result.Samples[1], 3);
        Assert.Equal(0f, result.Samples[3], 3);
        Assert.True(result.Samples[0] > 0.5f);
    }

    [Fact]
    public void SwapChannelsRange_ExchangesChannels()
    {
        var buffer = Stereo(1f, 2f, 3f, 4f);
        var result = SampleEditOps.SwapChannelsRange(buffer, 0, 2);
        Assert.Equal(new[] { 2f, 1f, 4f, 3f }, result.Samples);
    }

    [Fact]
    public void ReverseRange_ReversesSelectedFrames()
    {
        var buffer = Mono(0f, 1f, 2f, 3f, 4f);
        var result = SampleEditOps.ReverseRange(buffer, 1, 3);

        Assert.Equal(new[] { 0f, 3f, 2f, 1f, 4f }, result.Samples);
    }

    [Fact]
    public void ReverseRange_KeepsChannelsPaired()
    {
        var buffer = Stereo(1f, 10f, 2f, 20f, 3f, 30f);
        var result = SampleEditOps.ReverseRange(buffer, 0, 3);

        Assert.Equal(new[] { 3f, 30f, 2f, 20f, 1f, 10f }, result.Samples);
    }
}
