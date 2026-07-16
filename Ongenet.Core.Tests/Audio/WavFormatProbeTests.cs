using System;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Tests.Audio;

public sealed class WavFormatProbeTests
{
    [Fact]
    public void Detects_fl_studio_ogg_in_wav_format_tag()
    {
        var path = FindFactoryLaurieWebb();
        if (path is null) return;

        Assert.True(WavFormatProbe.TryGetFormatTag(path, out var tag));
        Assert.Equal(WavFormatProbe.FormatOggVorbis, tag);
        Assert.False(WavFormatProbe.IsNativePcmOrFloat(tag));
        Assert.False(new WavFileDecoder().CanDecode(path));
        Assert.True(new FfmpegAudioDecoder().CanDecode(path));
    }

    [Fact]
    public void Ffmpeg_decodes_fl_studio_ogg_in_wav_to_real_pcm()
    {
        var path = FindFactoryLaurieWebb();
        if (path is null) return;

        var buf = new FfmpegAudioDecoder().Decode(path);
        Assert.True(buf.FrameCount > 1000);
        Assert.Equal(1, buf.Channels);
        Assert.Equal(44100, buf.SampleRate);

        // Static-from-PCM-misread is near full-scale noise; real speech has moderate RMS.
        var sumSq = 0.0;
        var peak = 0f;
        foreach (var s in buf.Samples)
        {
            sumSq += s * s;
            var a = Math.Abs(s);
            if (a > peak) peak = a;
        }
        var rms = Math.Sqrt(sumSq / buf.Samples.Length);
        Assert.InRange(rms, 0.01, 0.45);
        Assert.InRange(peak, 0.1f, 1.0f);
    }

    private static string? FindFactoryLaurieWebb()
    {
        if (!Directory.Exists("/Applications")) return null;
        foreach (var app in Directory.GetDirectories("/Applications", "FL Studio*.app")
                     .OrderByDescending(a => a, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(
                app, "Contents", "Resources", "FL", "Data", "Patches", "Packs", "Vocals",
                "Laurie Webb Come On A.wav");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
