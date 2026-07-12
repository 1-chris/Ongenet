using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class GrooveMathTests
{
    [Fact]
    public void Apply_WithNoGroove_ReturnsOriginalBeat()
    {
        Assert.Equal(4.0, GrooveMath.Apply(4.0, null));
    }

    [Fact]
    public void Apply_WithSwingDelaysOffbeatSteps()
    {
        var groove = new GrooveTemplate { Division = 16, SwingAmount = 0.65 };
        var straight = GrooveMath.Apply(0.25, groove);
        Assert.True(straight > 0.25);
    }
}
