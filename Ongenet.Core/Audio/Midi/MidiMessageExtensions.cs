namespace Ongenet.Core.Audio.Midi;

public static class MidiMessageExtensions
{
    public static MidiMessage WithSource(this MidiMessage message, string? sourceDeviceId)
        => message.SourceDeviceId == sourceDeviceId
            ? message
            : message with { SourceDeviceId = sourceDeviceId };
}
