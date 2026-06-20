using System;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Selects the MIDI-input backend for the current operating system. Returns null on platforms whose
/// backend is not yet implemented, so the MIDI service degrades gracefully to "no devices".
/// </summary>
public static class MidiInputBackendFactory
{
    public static IMidiInputBackend? Create()
    {
        if (OperatingSystem.IsLinux())
            // Prefer the ALSA sequencer (sees hardware, PipeWire/JACK-bridged, software and BLE MIDI);
            // fall back to rawmidi only if the sequencer is unavailable.
            return AlsaSeqMidiInput.TryCreate() ?? (IMidiInputBackend)new AlsaMidiInput();
        if (OperatingSystem.IsWindows()) return new WinMmMidiInput();
        if (OperatingSystem.IsMacOS()) return new CoreMidiInput();
        return null;
    }
}
