using Ongenet.Core.Audio.Instruments.Sfz;

namespace Ongenet.Core.Tests.Sfz;

public class SfzNoteTests
{
    [Theory]
    [InlineData("c4", 60)]
    [InlineData("C4", 60)]
    [InlineData("c-1", 0)]
    [InlineData("C-1", 0)]
    [InlineData("c#4", 61)]
    [InlineData("db4", 61)]
    [InlineData("g9", 127)]
    [InlineData("36", 36)]
    [InlineData("0", 0)]
    public void ParsesKeys(string input, int expected)
        => Assert.Equal(expected, SfzNote.Parse(input));

    [Theory]
    [InlineData("xyz")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("h3")]
    public void RejectsInvalid(string input)
        => Assert.Null(SfzNote.Parse(input));

    [Fact]
    public void FallbackUsedWhenInvalid()
        => Assert.Equal(42, SfzNote.Parse("nope", 42));
}
