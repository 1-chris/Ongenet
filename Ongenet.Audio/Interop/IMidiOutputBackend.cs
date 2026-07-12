using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>Platform MIDI output port for external instruments.</summary>
public interface IMidiOutputBackend : IDisposable
{
    bool IsAvailable { get; }
    IReadOnlyList<MidiDeviceInfo> EnumerateDevices();
    void Open(MidiDeviceInfo device);
    void Close();
    void SendShortMessage(int status, int data1, int data2);
}

public static class MidiOutputBackendFactory
{
    public static IMidiOutputBackend? Create()
    {
        if (OperatingSystem.IsLinux()) return new AlsaRawMidiOutput();
        if (OperatingSystem.IsWindows()) return new WinMmMidiOutput();
        if (OperatingSystem.IsMacOS()) return CoreMidiOutput.TryCreate();
        return null;
    }
}
