namespace Ongenet.Core.Audio.Midi;

/// <summary>
/// An enumerated MIDI input port. <see cref="DisplayName"/> is the friendly name shown to the user;
/// <see cref="OpenId"/> is the backend-specific handle used to open the port (an ALSA "hw:c,d" string,
/// a winmm device index as text, or a CoreMIDI source index as text).
/// </summary>
public sealed record MidiDeviceInfo(string DisplayName, string OpenId);
