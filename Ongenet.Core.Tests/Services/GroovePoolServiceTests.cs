using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class GroovePoolServiceTests
{
    [Fact]
    public void TemplateRoundTrip_PreservesOffsets()
    {
        var file = new GrooveFile { Name = "Shuffle", Swing = 0.62 };
        file.Offsets.Add(new GrooveTimingOffset { StepIndex = 1, OffsetBeats = 0.02 });
        file.Offsets.Add(new GrooveTimingOffset { StepIndex = 5, OffsetBeats = -0.015 });

        var template = GroovePoolService.ToTemplate(file);
        var roundTrip = GroovePoolService.FromTemplate(template);

        Assert.Equal(file.Name, roundTrip.Name);
        Assert.Equal(file.Swing, roundTrip.Swing, 3);
        Assert.Equal(2, roundTrip.Offsets.Count);
        Assert.Contains(roundTrip.Offsets, o => o.StepIndex == 1 && System.Math.Abs(o.OffsetBeats - 0.02) < 1e-9);
        Assert.Contains(roundTrip.Offsets, o => o.StepIndex == 5 && System.Math.Abs(o.OffsetBeats + 0.015) < 1e-9);
    }
}
