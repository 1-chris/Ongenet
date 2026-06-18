namespace Ongenet.Core.Audio.Files;

/// <summary>Shared helper for flattening an interleaved <see cref="AudioSampleBuffer"/> to mono.</summary>
public static class SampleMixdown
{
    /// <summary>
    /// Averages all channels into a mono float array (one value per frame). When <paramref name="guard"/>
    /// is true (default), the array has one extra trailing 0 so a reader can safely interpolate to frame+1
    /// at the very end. Compute once (e.g. on sample load); read on the audio thread without bounds maths.
    /// </summary>
    public static float[] ToMono(AudioSampleBuffer sample, bool guard = true)
    {
        var frames = (int)sample.FrameCount;
        var channels = sample.Channels;
        var src = sample.Samples;
        var mono = new float[frames + (guard ? 1 : 0)];
        for (var f = 0; f < frames; f++)
        {
            var baseIndex = (long)f * channels;
            float sum = 0f;
            for (var c = 0; c < channels; c++) sum += src[baseIndex + c];
            mono[f] = sum / channels;
        }

        return mono;
    }
}
