using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class LogicalMidiEditTests
{
    [Fact]
    public void TransposeClip_ShiftsAllNotes()
    {
        var clip = new Clip { Name = "Melody", IsAudio = false };
        clip.Notes.Add(new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 1, Velocity = 0.8f });
        clip.Notes.Add(new MidiNote { Note = 64, StartBeat = 1, LengthBeats = 1, Velocity = 0.7f });

        LogicalMidiEdit.TransposeClip(clip, 5);

        Assert.Equal(65, clip.Notes[0].Note);
        Assert.Equal(69, clip.Notes[1].Note);
    }
}
