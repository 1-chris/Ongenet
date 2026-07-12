using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class ExportServiceTests
{
    [Fact]
    public void ExportOptions_DefaultsToMasterStereo()
    {
        var options = new ExportOptions();
        Assert.Equal(ExportKind.Master, options.Kind);
        Assert.Equal(SurroundFormat.Stereo, options.Surround);
        Assert.Equal(16, options.BitDepth);
    }

    [Fact]
    public void StemSeparationService_Heuristic_ReturnsFourStems()
    {
        var svc = new StemSeparationService();
        var tone = new float[4410];
        for (var i = 0; i < tone.Length; i++)
            tone[i] = (float)System.Math.Sin(2 * System.Math.PI * 220 * i / 44100.0);
        var buffer = new AudioSampleBuffer(tone, 1, 44100);
        var stems = svc.Separate(buffer);
        Assert.Equal(4, stems.Count);
        Assert.True(stems.ContainsKey(StemSeparationService.StemVocals));
        Assert.True(stems.ContainsKey(StemSeparationService.StemDrums));
    }
}
