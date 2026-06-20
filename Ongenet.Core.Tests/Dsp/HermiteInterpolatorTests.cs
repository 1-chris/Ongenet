using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Tests.Dsp;

public class HermiteInterpolatorTests
{
    [Fact]
    public void ReturnsEndpointsAtParameterBounds()
    {
        Assert.Equal(1f, HermiteInterpolator.Sample(0f, 1f, 2f, 3f, 0f));       // t=0 -> y0
        Assert.Equal(2f, HermiteInterpolator.Sample(0f, 1f, 2f, 3f, 1f), 1e-5f); // t=1 -> y1
    }

    [Fact]
    public void IsExactForCollinearPoints()
    {
        // Collinear samples (slope 1) -> Hermite reduces to the straight line.
        Assert.Equal(1.5f, HermiteInterpolator.Sample(0f, 1f, 2f, 3f, 0.5f), 1e-5f);
        Assert.Equal(1.25f, HermiteInterpolator.Sample(0f, 1f, 2f, 3f, 0.25f), 1e-5f);
    }
}
