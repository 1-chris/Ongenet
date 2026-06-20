using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Tests.Midi;

public class MidiQuantizeTests
{
    [Theory]
    [InlineData(1.31, 0.25, 1.25)]  // 1/16 grid, rounds down
    [InlineData(1.40, 0.25, 1.50)]  // 1/16 grid, rounds up
    [InlineData(0.10, 0.5, 0.0)]    // 1/8 grid, snaps to 0
    [InlineData(2.0, 1.0, 2.0)]     // already on the grid
    public void Snap_rounds_to_nearest_grid_multiple(double beat, double grid, double expected)
        => Assert.Equal(expected, MidiQuantize.Snap(beat, grid), 9);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Snap_with_nonpositive_grid_is_identity(double grid)
        => Assert.Equal(3.137, MidiQuantize.Snap(3.137, grid), 9);
}
