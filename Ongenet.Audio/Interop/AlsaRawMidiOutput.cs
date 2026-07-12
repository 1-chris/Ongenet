using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>Linux MIDI output via ALSA rawmidi.</summary>
public sealed class AlsaRawMidiOutput : IMidiOutputBackend
{
    private IntPtr _handle;

    public bool IsAvailable => true;

    public IReadOnlyList<MidiDeviceInfo> EnumerateDevices()
    {
        var list = new List<MidiDeviceInfo>();
        var card = -1;
        while (AlsaMidiNative.snd_card_next(ref card) >= 0 && card >= 0)
        {
            var ctlName = $"hw:{card}";
            if (AlsaMidiNative.snd_ctl_open(out var ctl, ctlName, 0) < 0) continue;
            try
            {
                var dev = -1;
                while (AlsaMidiNative.snd_ctl_rawmidi_next_device(ctl, ref dev) >= 0 && dev >= 0)
                {
                    var name = RawMidiName(ctl, dev) ?? $"MIDI {card}:{dev}";
                    list.Add(new MidiDeviceInfo(name, $"hw:{card},{dev},0"));
                }
            }
            finally
            {
                AlsaMidiNative.snd_ctl_close(ctl);
            }
        }

        return list;
    }

    private static string? RawMidiName(IntPtr ctl, int dev)
    {
        if (AlsaMidiNative.snd_rawmidi_info_malloc(out var info) != 0) return null;
        try
        {
            AlsaMidiNative.snd_rawmidi_info_set_device(info, (uint)dev);
            AlsaMidiNative.snd_rawmidi_info_set_subdevice(info, 0);
            AlsaMidiNative.snd_rawmidi_info_set_stream(info, AlsaMidiNative.SND_RAWMIDI_STREAM_OUTPUT);
            if (AlsaMidiNative.snd_ctl_rawmidi_info(ctl, info) != 0) return null;
            var p = AlsaMidiNative.snd_rawmidi_info_get_name(info);
            return p == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p);
        }
        finally
        {
            AlsaMidiNative.snd_rawmidi_info_free(info);
        }
    }

    public void Open(MidiDeviceInfo device)
    {
        Close();
        var rc = AlsaMidiNative.snd_rawmidi_open(out _, out _handle, device.OpenId, 0);
        if (rc < 0)
            throw new InvalidOperationException($"snd_rawmidi_open output failed: {AlsaMidiNative.ErrorText((int)rc)}");
    }

    public void Close()
    {
        if (_handle == IntPtr.Zero) return;
        AlsaMidiNative.snd_rawmidi_close(_handle);
        _handle = IntPtr.Zero;
    }

    public unsafe void SendShortMessage(int status, int data1, int data2)
    {
        if (_handle == IntPtr.Zero) return;
        Span<byte> bytes = stackalloc byte[] { (byte)status, (byte)data1, (byte)data2 };
        var length = status >= 0xF8 ? 1 : 3;
        fixed (byte* p = bytes)
            AlsaMidiNative.snd_rawmidi_write(_handle, (IntPtr)p, (nuint)length);
    }

    public void Dispose() => Close();
}
