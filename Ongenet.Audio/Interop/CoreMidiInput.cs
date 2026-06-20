using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>
/// macOS MIDI input via CoreMIDI. The client, input port and source connection must be created on a
/// thread that then pumps a CFRunLoop — the classic <c>MIDIReadProc</c> only fires on such a thread —
/// so all of that lives on a dedicated background thread that loops <c>CFRunLoopRunInMode</c> until
/// stopped (no <c>CFRunLoopStop</c> timing race). Incoming packets are walked manually (the
/// <c>MIDIPacket</c> layout is packed and not safely marshalled as a managed struct) and fed through
/// the shared running-status parser.
/// </summary>
public sealed class CoreMidiInput : IMidiInputBackend
{
    private const double RunLoopSliceSeconds = 0.25;

    private readonly object _lock = new();
    private readonly MidiRunningStatusParser _parser = new();

    private CoreMidiNative.MIDIReadProc? _readProc; // rooted for the port's lifetime
    private Action<MidiMessage>? _onMessage;
    private Thread? _thread;
    private volatile bool _running;
    private uint _sourceIndex;

    public bool IsCapturing { get; private set; }

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

    public void Start(MidiDeviceInfo device, Action<MidiMessage> onMessage)
    {
        Stop();

        if (!uint.TryParse(device.OpenId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            throw new InvalidOperationException($"Invalid MIDI device id '{device.OpenId}'.");

        lock (_lock)
        {
            _onMessage = onMessage;
            _sourceIndex = index;
            _parser.Reset();
            _running = true;
            _thread = new Thread(RunLoopThread) { IsBackground = true, Name = "CoreMIDI In" };
            _thread.Start();
            IsCapturing = true;
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lock)
        {
            if (!_running && _thread is null) return;
            _running = false;
            thread = _thread;
            _thread = null;
        }

        thread?.Join(2000);

        lock (_lock)
        {
            _onMessage = null;
            _readProc = null;
            IsCapturing = false;
        }
    }

    private void RunLoopThread()
    {
        var clientName = CoreMidiNative.CFStringCreateWithCString(IntPtr.Zero, Utf8("Ongenet"),
            CoreMidiNative.kCFStringEncodingUTF8);
        var portName = CoreMidiNative.CFStringCreateWithCString(IntPtr.Zero, Utf8("Ongenet Input"),
            CoreMidiNative.kCFStringEncodingUTF8);

        uint client = 0, port = 0, source = 0;
        var connected = false;
        try
        {
            _readProc = OnRead; // rooted while the loop runs
            if (CoreMidiNative.MIDIClientCreate(clientName, IntPtr.Zero, IntPtr.Zero, out client) != 0) return;
            if (CoreMidiNative.MIDIInputPortCreate(client, portName, _readProc, IntPtr.Zero, out port) != 0) return;

            source = CoreMidiNative.MIDIGetSource(_sourceIndex);
            if (CoreMidiNative.MIDIPortConnectSource(port, source, IntPtr.Zero) != 0) return;
            connected = true;

            var mode = CoreMidiNative.DefaultRunLoopMode();
            // Pump the run loop in slices so the loop notices Stop() promptly and exits cleanly.
            while (_running)
                CoreMidiNative.CFRunLoopRunInMode(mode, RunLoopSliceSeconds, false);
        }
        finally
        {
            if (connected) CoreMidiNative.MIDIPortDisconnectSource(port, source);
            if (port != 0) CoreMidiNative.MIDIPortDispose(port);
            if (client != 0) CoreMidiNative.MIDIClientDispose(client);
            if (clientName != IntPtr.Zero) CoreMidiNative.CFRelease(clientName);
            if (portName != IntPtr.Zero) CoreMidiNative.CFRelease(portName);
        }
    }

    // CoreMIDI read callback (run-loop thread). Walks the packet list manually: MIDIPacket is
    // { UInt64 timeStamp; UInt16 length; Byte data[]; } packed so data begins at offset 10, and each
    // packet's advance is rounded up to a 4-byte boundary.
    private unsafe void OnRead(IntPtr pktlist, IntPtr readProcRefCon, IntPtr srcConnRefCon)
    {
        var cb = _onMessage;
        if (cb is null || pktlist == IntPtr.Zero) return;

        var p = (byte*)pktlist;
        var numPackets = *(uint*)p;
        var pkt = p + 4; // packets follow the UInt32 count (no padding under pack(4))

        for (uint i = 0; i < numPackets; i++)
        {
            var length = *(ushort*)(pkt + 8);
            var data = pkt + 10;
            if (length > 0)
                _parser.Push(new ReadOnlySpan<byte>(data, length), cb);

            var advance = 10 + length;
            advance = (advance + 3) & ~3; // round up to 4-byte alignment
            pkt += advance;
        }
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

    private static string CFStringToManaged(IntPtr cf)
    {
        var len = CoreMidiNative.CFStringGetLength(cf);
        var cap = (int)len * 4 + 1; // UTF-8 worst case, plus terminator
        if (cap < 16) cap = 16;
        var buffer = new byte[cap];
        if (!CoreMidiNative.CFStringGetCString(cf, buffer, buffer.Length, CoreMidiNative.kCFStringEncodingUTF8))
            return "";
        var n = Array.IndexOf(buffer, (byte)0);
        if (n < 0) n = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, n);
    }

    public void Dispose() => Stop();
}
