using System.Collections.Generic;

namespace Ongenet.Desktop.Services;

/// <summary>
/// Serializable app-wide preferences persisted to the per-user config file. Audio/MIDI devices are
/// stored by display name (stable across reconnects, unlike a backend index); the theme by name + variant.
/// </summary>
public sealed class AppSettings
{
    public string? AudioOutputDevice { get; set; }
    public string? AudioInputDevice { get; set; }
    public string InputChannelMode { get; set; } = "Stereo";
    public string? MidiInputDevice { get; set; }
    public string? ThemeName { get; set; }
    public bool ThemeIsLight { get; set; }
    public double InputQuantizeBeats { get; set; }
    public List<TransportMappingDto> TransportMappings { get; set; } = new();
}

/// <summary>Serializable form of a <see cref="Core.Audio.Midi.TransportMapping"/>.</summary>
public sealed class TransportMappingDto
{
    public string Action { get; set; } = "";
    public bool IsNote { get; set; }
    public int Channel { get; set; } = -1;
    public int Number { get; set; }
}
