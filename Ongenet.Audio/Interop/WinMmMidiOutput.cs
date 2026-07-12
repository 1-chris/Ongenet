using System;
using System.Collections.Generic;
using System.Globalization;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

public sealed class WinMmMidiOutput : IMidiOutputBackend
{
    private IntPtr _handle;

    public bool IsAvailable => true;

    public IReadOnlyList<MidiDeviceInfo> EnumerateDevices()
    {
        var list = new List<MidiDeviceInfo>();
        var count = WinMmMidiNative.midiOutGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            var caps = default(WinMmMidiNative.MIDIOUTCAPS);
            if (WinMmMidiNative.midiOutGetDevCaps((UIntPtr)i, ref caps,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<WinMmMidiNative.MIDIOUTCAPS>()) != 0)
                continue;
            var name = string.IsNullOrEmpty(caps.szPname) ? $"MIDI output {i}" : caps.szPname;
            list.Add(new MidiDeviceInfo(name, i.ToString(CultureInfo.InvariantCulture)));
        }

        return list;
    }

    public void Open(MidiDeviceInfo device)
    {
        Close();
        if (!uint.TryParse(device.OpenId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            throw new InvalidOperationException($"Invalid MIDI output id '{device.OpenId}'.");
        var rc = WinMmMidiNative.midiOutOpen(out _handle, id, IntPtr.Zero, IntPtr.Zero, 0);
        if (rc != 0) throw new InvalidOperationException($"midiOutOpen failed with code {rc}.");
    }

    public void Close()
    {
        if (_handle != IntPtr.Zero)
        {
            WinMmMidiNative.midiOutClose(_handle);
            _handle = IntPtr.Zero;
        }
    }

    public void SendShortMessage(int status, int data1, int data2)
    {
        if (_handle == IntPtr.Zero) return;
        var msg = (uint)(status | (data1 << 8) | (data2 << 16));
        WinMmMidiNative.midiOutShortMsg(_handle, msg);
    }

    public void Dispose() => Close();
}
