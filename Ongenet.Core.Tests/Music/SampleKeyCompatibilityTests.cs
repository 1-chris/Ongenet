using Ongenet.Core.Music;

namespace Ongenet.Core.Tests.Music;

public class SampleKeyCompatibilityTests
{
    [Theory]
    [InlineData(0, false, 0, false, SampleKeyCompatibility.Fit.Same)]
    [InlineData(0, false, 0, true, SampleKeyCompatibility.Fit.Parallel)]
    [InlineData(8, true, 8, false, SampleKeyCompatibility.Fit.Parallel)]
    [InlineData(8, true, 11, false, SampleKeyCompatibility.Fit.Relative)]   // G# min -> B maj
    [InlineData(0, false, 9, true, SampleKeyCompatibility.Fit.Relative)]    // C maj -> A min
    [InlineData(8, true, 1, true, SampleKeyCompatibility.Fit.Subdominant)]  // G# min -> C# min
    [InlineData(8, true, 3, true, SampleKeyCompatibility.Fit.Dominant)]    // G# min -> D# min
    [InlineData(8, true, 0, false, SampleKeyCompatibility.Fit.Other)]        // G# min -> C maj (+4)
    public void Classify_relationships(int fromRoot, bool fromMinor, int toRoot, bool toMinor, SampleKeyCompatibility.Fit expected)
        => Assert.Equal(expected, SampleKeyCompatibility.Classify(fromRoot, fromMinor, toRoot, toMinor));

    [Fact]
    public void IsRecommended_marks_related_and_close_shifts()
    {
        Assert.True(SampleKeyCompatibility.IsRecommended(SampleKeyCompatibility.Fit.Relative));
        Assert.True(SampleKeyCompatibility.IsRecommended(SampleKeyCompatibility.Fit.CloseShift));
        Assert.False(SampleKeyCompatibility.IsRecommended(SampleKeyCompatibility.Fit.Other));
    }
}
