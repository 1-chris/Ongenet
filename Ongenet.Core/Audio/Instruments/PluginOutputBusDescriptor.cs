namespace Ongenet.Core.Audio.Instruments;

/// <summary>Describes one auxiliary output bus exposed by a multi-out instrument plugin.</summary>
public sealed class PluginOutputBusDescriptor
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public int ChannelCount { get; init; } = 2;
}
