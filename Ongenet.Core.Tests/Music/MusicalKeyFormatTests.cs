using Ongenet.Core.Music;

namespace Ongenet.Core.Tests.Music;

public class MusicalKeyFormatTests
{
    [Theory]
    [InlineData("C maj", 0, false)]
    [InlineData("A min", 9, true)]
    [InlineData("F# maj", 6, false)]
    public void TryParse_ParsesDetectedKeys(string text, int root, bool minor)
    {
        Assert.True(MusicalKeyFormat.TryParse(text, out var parsedRoot, out var parsedMinor));
        Assert.Equal(root, parsedRoot);
        Assert.Equal(minor, parsedMinor);
    }

    [Fact]
    public void ShortestSemitoneDelta_PicksNearestDirection()
    {
        Assert.Equal(1, MusicalKeyFormat.ShortestSemitoneDelta(0, 1));
        Assert.Equal(-1, MusicalKeyFormat.ShortestSemitoneDelta(1, 0));
        Assert.Equal(-3, MusicalKeyFormat.ShortestSemitoneDelta(0, 9));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var text = MusicalKeyFormat.Format(9, true);
        Assert.Equal("A min", text);
    }
}
