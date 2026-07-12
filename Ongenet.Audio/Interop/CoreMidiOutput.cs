using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

public sealed class CoreMidiOutput : IMidiOutputBackend
{
    private uint _client;
    private uint _port;
    private uint _destination;

    public bool IsAvailable => _client != 0;

    public static CoreMidiOutput? TryCreate()
    {
        var o = new CoreMidiOutput();
        var name = CFStringFromUtf8("Ongenet");
        if (CoreMidiNative.MIDIClientCreate(name, IntPtr.Zero, IntPtr.Zero, out o._client) != 0)
            return null;
        if (CoreMidiNative.MIDIOutputPortCreate(o._client, name, out o._port) != 0)
        {
            CoreMidiNative.MIDIClientDispose(o._client);
            return null;
        }

        return o;
    }

    public IReadOnlyList<MidiDeviceInfo> EnumerateDevices()
    {
        var list = new List<MidiDeviceInfo>();
        var count = CoreMidiNative.MIDIGetNumberOfDestinations();
        for (nuint i = 0; i < count; i++)
        {
            var dest = CoreMidiNative.MIDIGetDestination(i);
            list.Add(new MidiDeviceInfo(DestName(dest, i), i.ToString(CultureInfo.InvariantCulture)));
        }

        return list;
    }

    private static string DestName(uint dest, nuint index)
    {
        var prop = CoreMidiNative.DisplayNameProperty();
        if (prop != IntPtr.Zero &&
            CoreMidiNative.MIDIObjectGetStringProperty(dest, prop, out var cf) == 0 && cf != IntPtr.Zero)
        {
            try
            {
                var len = CoreMidiNative.CFStringGetLength(cf);
                var buf = new byte[len * 4 + 8];
                if (CoreMidiNative.CFStringGetCString(cf, buf, buf.Length, CoreMidiNative.kCFStringEncodingUTF8))
                    return Encoding.UTF8.GetString(buf).TrimEnd('\0');
            }
            finally
            {
                CoreMidiNative.CFRelease(cf);
            }
        }

        return $"MIDI destination {index}";
    }

    public void Open(MidiDeviceInfo device)
    {
        if (!uint.TryParse(device.OpenId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
            throw new InvalidOperationException($"Invalid CoreMIDI destination id '{device.OpenId}'.");
        _destination = CoreMidiNative.MIDIGetDestination(idx);
    }

    public void Close() => _destination = 0;

    public unsafe void SendShortMessage(int status, int data1, int data2)
    {
        if (_port == 0 || _destination == 0) return;
        var listSize = sizeof(MidiPacketListHeader) + sizeof(MidiPacket);
        var buffer = stackalloc byte[listSize];
        var list = (MidiPacketListHeader*)buffer;
        list->numPackets = 1;
        list->packet.timeStamp = 0;
        list->packet.length = (ushort)(status >= 0xF8 ? 1 : 3);
        list->packet.data0 = (byte)status;
        list->packet.data1 = (byte)data1;
        list->packet.data2 = (byte)data2;
        CoreMidiNative.MIDISend(_port, _destination, (IntPtr)buffer);
    }

    public void Dispose()
    {
        if (_port != 0) CoreMidiNative.MIDIPortDispose(_port);
        if (_client != 0) CoreMidiNative.MIDIClientDispose(_client);
        _port = _client = 0;
    }

    private static IntPtr CFStringFromUtf8(string s)
        => CoreMidiNative.CFStringCreateWithCString(IntPtr.Zero, Encoding.UTF8.GetBytes(s + "\0"),
            CoreMidiNative.kCFStringEncodingUTF8);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct MidiPacketListHeader
    {
        public uint numPackets;
        public MidiPacket packet;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct MidiPacket
    {
        public ulong timeStamp;
        public ushort length;
        public byte data0;
        public byte data1;
        public byte data2;
    }
}
