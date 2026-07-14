using Ongenet.Core.Audio.Containers;
using Xunit;

namespace Ongenet.Core.Tests.Containers;

public sealed class XyMorphTests
{
    [Fact]
    public void CornerWeights_AtCenter_AreEqual()
    {
        Span<float> w = stackalloc float[4];
        XyMorphMath.CornerWeights(0.5, 0.5, w);
        Assert.Equal(0.25f, w[0], 3);
        Assert.Equal(0.25f, w[1], 3);
        Assert.Equal(0.25f, w[2], 3);
        Assert.Equal(0.25f, w[3], 3);
    }

    [Fact]
    public void CornerWeights_SumToOne()
    {
        Span<float> w = stackalloc float[4];
        XyMorphMath.CornerWeights(0.37, 0.82, w);
        Assert.Equal(1f, w[0] + w[1] + w[2] + w[3], 3);
    }

    [Fact]
    public void XyInstrument_HasFourChildren()
    {
        var xy = new XyInstrument();
        Assert.Equal(4, xy.Children.Count);
        Assert.Equal(4, xy.MaxChildren);
        Assert.Equal(4, xy.MinChildren);
    }
}
