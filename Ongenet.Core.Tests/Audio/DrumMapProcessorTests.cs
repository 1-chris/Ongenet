using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class DrumMapProcessorTests
{
    [Fact]
    public void Apply_ScalesVelocityFromMap()
    {
        var project = new Project();
        var map = new DrumMap { Name = "Kit" };
        map.Entries.Add(new DrumMapEntry { Note = 36, VelocityScale = 0.5f });
        project.DrumMaps.Add(map);
        var track = new Track { DrumMapId = map.Id };

        var (_, vel) = DrumMapProcessor.Apply(project, track, 36, 1f);
        Assert.Equal(0.5f, vel, 3);
    }
}
