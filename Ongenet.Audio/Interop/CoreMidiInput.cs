using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>
/// macOS MIDI input via CoreMIDI with multi-source support on one input port.
/// </summary>
public sealed class CoreMidiInput : IMidiInputBackend
{
    private const double RunLoopSliceSeconds = 0.25;

    private readonly object _lock = new();
    private readonly Dictionary<int, string> _sourceOpenIds = new();
    private readonly Dictionary<int, MidiRunningStatusParser> _parsers = new();
    private readonly List<MidiDeviceInfo> _connectedList = new();

    private CoreMidiNative.MIDIReadProc? _readProc;
    private Action<MidiMessage>? _onMessage;
    private Thread? _thread;
    private volatile bool _running;

    public bool IsCapturing { get; private set; }

    public IReadOnlyList<MidiDeviceInfo> ConnectedDevices => _connectedList;

    public IReadOnlyList<MidiDeviceInfo> EnumerateDevices()
    {
        var list = new List<MidiDeviceInfo>();
        var count = CoreMidiNative.MIDIGetNumberOfSources();
        for (nuint i = 0; i < count; i++)
        {
            var src = CoreMidiNative.MIDIGetSource(i);
            var name = SourceName(src, i);
            list.Add(new MidiDeviceInfo(name, i.ToString(CultureInfo.InvariantCulture)));
        }

        return list;
    }

    public void Connect(MidiDeviceInfo device, Action<MidiMessage> onMessage)
    {
        if (!int.TryParse(device.OpenId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            throw new InvalidOperationException($"Invalid MIDI device id '{device.OpenId}'.");

        lock (_lock)
        {
            _onMessage = onMessage;
            if (_sourceOpenIds.ContainsKey(index)) return;

            _sourceOpenIds[index] = device.OpenId;
            _parsers[index] = new MidiRunningStatusParser();
            _connectedList.Add(device);
            RestartThreadLocked();
        }
    }

    public void Disconnect(MidiDeviceInfo device)
    {
        if (!int.TryParse(device.OpenId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            return;

        lock (_lock)
        {
            if (!_sourceOpenIds.Remove(index)) return;
            _parsers.Remove(index);
            _connectedList.RemoveAll(d => d.OpenId == device.OpenId);
            RestartThreadLocked();
        }
    }

    public void DisconnectAll()
    {
        lock (_lock)
        {
            _sourceOpenIds.Clear();
            _parsers.Clear();
            _connectedList.Clear();
            StopThreadLocked();
        }
    }

    private void RestartThreadLocked()
    {
        StopThreadLocked();
        if (_sourceOpenIds.Count == 0 || _onMessage is null) return;

        _running = true;
        _thread = new Thread(RunLoopThread) { IsBackground = true, Name = "CoreMIDI In" };
        _thread.Start();
        IsCapturing = true;
    }

    private void StopThreadLocked()
    {
        var thread = _thread;
        _running = false;
        _thread = null;
        thread?.Join(2000);
        _readProc = null;
        IsCapturing = false;
    }

    private void RunLoopThread()
    {
        var clientName = CoreMidiNative.CFStringCreateWithCString(IntPtr.Zero, Utf8("Ongenet"),
            CoreMidiNative.kCFStringEncodingUTF8);
        var portName = CoreMidiNative.CFStringCreateWithCString(IntPtr.Zero, Utf8("Ongenet Input"),
            CoreMidiNative.kCFStringEncodingUTF8);

        uint client = 0, port = 0;
        var connected = new List<(uint Source, int Index)>();
        try
        {
            _readProc = OnRead;
            if (CoreMidiNative.MIDIClientCreate(clientName, IntPtr.Zero, IntPtr.Zero, out client) != 0) return;
            if (CoreMidiNative.MIDIInputPortCreate(client, portName, _readProc, IntPtr.Zero, out port) != 0) return;

            lock (_lock)
            {
                foreach (var (index, _) in _sourceOpenIds)
                {
                    var source = CoreMidiNative.MIDIGetSource((uint)index);
                    if (CoreMidiNative.MIDIPortConnectSource(port, source, (IntPtr)index) == 0)
                        connected.Add((source, index));
                }
            }

            var mode = CoreMidiNative.DefaultRunLoopMode();
            while (_running)
                CoreMidiNative.CFRunLoopRunInMode(mode, RunLoopSliceSeconds, false);
        }
        finally
        {
            foreach (var (source, _) in connected)
                CoreMidiNative.MIDIPortDisconnectSource(port, source);
            if (port != 0) CoreMidiNative.MIDIPortDispose(port);
            if (client != 0) CoreMidiNative.MIDIClientDispose(client);
            if (clientName != IntPtr.Zero) CoreMidiNative.CFRelease(clientName);
            if (portName != IntPtr.Zero) CoreMidiNative.CFRelease(portName);
        }
    }

    private unsafe void OnRead(IntPtr pktlist, IntPtr readProcRefCon, IntPtr srcConnRefCon)
    {
        var cb = _onMessage;
        if (cb is null || pktlist == IntPtr.Zero) return;

        var index = srcConnRefCon.ToInt32();
        string? openId;
        MidiRunningStatusParser? parser;
        lock (_lock)
        {
            if (!_sourceOpenIds.TryGetValue(index, out openId) || !_parsers.TryGetValue(index, out parser))
                return;
        }

        var p = (byte*)pktlist;
        var numPackets = *(uint*)p;
        var pkt = p + 4;

        for (uint i = 0; i < numPackets; i++)
        {
            var length = *(ushort*)(pkt + 8);
            var data = pkt + 10;
            if (length > 0)
            {
                var sourceId = openId;
                parser!.Push(new ReadOnlySpan<byte>(data, length), m => cb(m.WithSource(sourceId)));
            }

            var advance = 10 + length;
            advance = (advance + 3) & ~3;
            pkt += advance;
        }
    }

    private static string SourceName(uint src, nuint index)
    {
        var prop = CoreMidiNative.DisplayNameProperty();
        if (prop != IntPtr.Zero &&
            CoreMidiNative.MIDIObjectGetStringProperty(src, prop, out var cf) == 0 && cf != IntPtr.Zero)
        {
            try
            {
                var name = CFStringToManaged(cf);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            finally
            {
                CoreMidiNative.CFRelease(cf);
            }
        }

        return $"MIDI input {index}";
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

    private static string CFStringToManaged(IntPtr cf)
    {
        var len = CoreMidiNative.CFStringGetLength(cf);
        var cap = (int)len * 4 + 1;
        if (cap < 16) cap = 16;
        var buffer = new byte[cap];
        if (!CoreMidiNative.CFStringGetCString(cf, buffer, buffer.Length, CoreMidiNative.kCFStringEncodingUTF8))
            return "";
        var n = Array.IndexOf(buffer, (byte)0);
        if (n < 0) n = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, n);
    }

    public void Dispose() => DisconnectAll();
}
