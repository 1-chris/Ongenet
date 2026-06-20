using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Linux MIDI input via the ALSA <b>sequencer</b> (snd_seq). This is the universal Linux path: the
/// kernel exposes hardware MIDI as sequencer clients, and PipeWire/JACK bridge their MIDI into the
/// sequencer too — so unlike rawmidi this sees USB devices on PipeWire systems, software ports and
/// Bluetooth (BLE) MIDI. We create one input port and subscribe the chosen source to it.
///
/// Events are structured (no running-status parsing needed); we decode the packed
/// <c>snd_seq_event_t</c> by field offset. Shutdown mirrors the rawmidi backend: non-blocking input +
/// a poll timeout so the read thread exits promptly without an unsafe cross-thread close.
/// </summary>
public sealed class AlsaSeqMidiInput : IMidiInputBackend
{
    private const int PollTimeoutMs = 100;

    // snd_seq_event_t field offsets (LP64): type@0; data union@16. note_t: channel@16, note@17, vel@18.
    // ctrl_t: channel@16, param(uint)@20, value(int)@24.
    private const int OffType = 0;
    private const int OffChannel = 16;
    private const int OffNote = 17;
    private const int OffVelocity = 18;
    private const int OffParam = 20;
    private const int OffValue = 24;

    private readonly object _lock = new();
    private readonly List<MidiMessage> _batch = new(32);

    private IntPtr _handle;
    private int _port = -1;
    private int _clientId = -1;
    private Thread? _thread;
    private Action<MidiMessage>? _onMessage;
    private volatile bool _running;
    private (int Client, int Port)? _connected;

    private AlsaSeqMidiInput(IntPtr handle, int clientId, int port)
    {
        _handle = handle;
        _clientId = clientId;
        _port = port;
    }

    public bool IsCapturing { get; private set; }

    /// <summary>Opens a sequencer client + input port, or returns null if the sequencer is unavailable.</summary>
    public static AlsaSeqMidiInput? TryCreate()
    {
        if (AlsaMidiNative.snd_seq_open(out var handle, "default", AlsaMidiNative.SND_SEQ_OPEN_DUPLEX, 0) < 0)
            return null;

        AlsaMidiNative.snd_seq_set_client_name(handle, "Ongenet");
        AlsaMidiNative.snd_seq_nonblock(handle, 1);
        var clientId = AlsaMidiNative.snd_seq_client_id(handle);

        var caps = AlsaMidiNative.SND_SEQ_PORT_CAP_WRITE | AlsaMidiNative.SND_SEQ_PORT_CAP_SUBS_WRITE;
        var type = AlsaMidiNative.SND_SEQ_PORT_TYPE_MIDI_GENERIC | AlsaMidiNative.SND_SEQ_PORT_TYPE_APPLICATION;
        var port = AlsaMidiNative.snd_seq_create_simple_port(handle, "Ongenet input", caps, type);
        if (port < 0)
        {
            AlsaMidiNative.snd_seq_close(handle);
            return null;
        }

        return new AlsaSeqMidiInput(handle, clientId, port);
    }

    public IReadOnlyList<MidiDeviceInfo> EnumerateDevices()
    {
        var list = new List<MidiDeviceInfo>();
        const uint readable = AlsaMidiNative.SND_SEQ_PORT_CAP_READ | AlsaMidiNative.SND_SEQ_PORT_CAP_SUBS_READ;

        lock (_lock)
        {
            if (_handle == IntPtr.Zero) return list;
            if (AlsaMidiNative.snd_seq_client_info_malloc(out var cinfo) != 0) return list;
            if (AlsaMidiNative.snd_seq_port_info_malloc(out var pinfo) != 0)
            {
                AlsaMidiNative.snd_seq_client_info_free(cinfo);
                return list;
            }

            try
            {
                AlsaMidiNative.snd_seq_client_info_set_client(cinfo, -1);
                while (AlsaMidiNative.snd_seq_query_next_client(_handle, cinfo) >= 0)
                {
                    var client = AlsaMidiNative.snd_seq_client_info_get_client(cinfo);
                    if (client == _clientId || client == 0) continue; // skip ourselves and the System client
                    var clientName = PtrToString(AlsaMidiNative.snd_seq_client_info_get_name(cinfo), $"Client {client}");

                    AlsaMidiNative.snd_seq_port_info_set_client(pinfo, client);
                    AlsaMidiNative.snd_seq_port_info_set_port(pinfo, -1);
                    while (AlsaMidiNative.snd_seq_query_next_port(_handle, pinfo) >= 0)
                    {
                        var caps = AlsaMidiNative.snd_seq_port_info_get_capability(pinfo);
                        if ((caps & readable) != readable) continue; // must be a subscribable source
                        var port = AlsaMidiNative.snd_seq_port_info_get_port(pinfo);
                        var portName = PtrToString(AlsaMidiNative.snd_seq_port_info_get_name(pinfo), $"Port {port}");
                        var display = portName.Contains(clientName) ? portName : $"{clientName} — {portName}";
                        list.Add(new MidiDeviceInfo(display, $"{client}:{port}"));
                    }
                }
            }
            finally
            {
                AlsaMidiNative.snd_seq_port_info_free(pinfo);
                AlsaMidiNative.snd_seq_client_info_free(cinfo);
            }
        }

        return list;
    }

    public void Start(MidiDeviceInfo device, Action<MidiMessage> onMessage)
    {
        Stop();

        var (client, port) = ParseAddr(device.OpenId);
        if (client < 0) throw new InvalidOperationException($"Invalid MIDI port id '{device.OpenId}'.");

        lock (_lock)
        {
            var rc = AlsaMidiNative.snd_seq_connect_from(_handle, _port, client, port);
            if (rc < 0)
                throw new InvalidOperationException(
                    $"snd_seq_connect_from({client}:{port}) failed: {AlsaMidiNative.ErrorText(rc)}");

            _connected = (client, port);
            _onMessage = onMessage;
            _running = true;
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "ALSA seq MIDI In" };
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

        thread?.Join(1000);

        lock (_lock)
        {
            if (_connected is { } c && _handle != IntPtr.Zero)
                AlsaMidiNative.snd_seq_disconnect_from(_handle, _port, c.Client, c.Port);
            _connected = null;
            _onMessage = null;
            IsCapturing = false;
        }
    }

    private unsafe void ReadLoop()
    {
        int count;
        IntPtr pfds;
        lock (_lock)
        {
            if (_handle == IntPtr.Zero) return;
            count = AlsaMidiNative.snd_seq_poll_descriptors_count(_handle, AlsaMidiNative.POLLIN);
            if (count < 1) count = 1;
            pfds = Marshal.AllocHGlobal(count * Marshal.SizeOf<AlsaMidiNative.PollFd>());
            AlsaMidiNative.snd_seq_poll_descriptors(_handle, pfds, (uint)count, AlsaMidiNative.POLLIN);
        }

        try
        {
            while (_running)
            {
                var pr = AlsaMidiNative.poll(pfds, (nuint)count, PollTimeoutMs);
                if (pr < 0)
                {
                    if (Marshal.GetLastPInvokeError() == AlsaMidiNative.EINTR) continue;
                    break;
                }

                if (pr == 0) continue; // timeout — re-check _running

                // Drain all pending events under the lock (decode only), then dispatch outside it.
                _batch.Clear();
                var cb = _onMessage;
                lock (_lock)
                {
                    while (true)
                    {
                        var rc = AlsaMidiNative.snd_seq_event_input(_handle, out var ev);
                        if (rc < 0 || ev == IntPtr.Zero) break; // -EAGAIN when empty
                        if (TryDecode((byte*)ev, out var msg)) _batch.Add(msg);
                    }
                }

                if (cb is not null)
                    foreach (var m in _batch)
                        cb(m);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pfds);
        }
    }

    private static unsafe bool TryDecode(byte* p, out MidiMessage msg)
    {
        var channel = (byte)(p[OffChannel] & 0x0F);
        switch (p[OffType])
        {
            case AlsaMidiNative.SND_SEQ_EVENT_NOTEON:
            {
                var vel = p[OffVelocity];
                msg = vel == 0
                    ? new MidiMessage(MidiMessageKind.NoteOff, channel, p[OffNote], 0)
                    : new MidiMessage(MidiMessageKind.NoteOn, channel, p[OffNote], vel);
                return true;
            }
            case AlsaMidiNative.SND_SEQ_EVENT_NOTEOFF:
                msg = new MidiMessage(MidiMessageKind.NoteOff, channel, p[OffNote], p[OffVelocity]);
                return true;
            case AlsaMidiNative.SND_SEQ_EVENT_KEYPRESS:
                msg = new MidiMessage(MidiMessageKind.PolyAftertouch, channel, p[OffNote], p[OffVelocity]);
                return true;
            case AlsaMidiNative.SND_SEQ_EVENT_CONTROLLER:
            {
                var param = *(int*)(p + OffParam);
                var value = *(int*)(p + OffValue);
                msg = new MidiMessage(MidiMessageKind.ControlChange, channel, (byte)(param & 0x7F), (byte)(value & 0x7F));
                return true;
            }
            case AlsaMidiNative.SND_SEQ_EVENT_PGMCHANGE:
                msg = new MidiMessage(MidiMessageKind.ProgramChange, channel, (byte)(*(int*)(p + OffValue) & 0x7F), 0);
                return true;
            case AlsaMidiNative.SND_SEQ_EVENT_CHANPRESS:
                msg = new MidiMessage(MidiMessageKind.ChannelAftertouch, channel, (byte)(*(int*)(p + OffValue) & 0x7F), 0);
                return true;
            case AlsaMidiNative.SND_SEQ_EVENT_PITCHBEND:
            {
                // seq pitch bend is signed -8192..8191; convert to 14-bit 0..16383 split across data bytes.
                var v14 = *(int*)(p + OffValue) + 8192;
                if (v14 < 0) v14 = 0;
                else if (v14 > 16383) v14 = 16383;
                msg = new MidiMessage(MidiMessageKind.PitchBend, channel, (byte)(v14 & 0x7F), (byte)((v14 >> 7) & 0x7F));
                return true;
            }
            default:
                msg = default;
                return false;
        }
    }

    private static (int Client, int Port) ParseAddr(string id)
    {
        var parts = id.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var c) && int.TryParse(parts[1], out var p))
            return (c, p);
        return (-1, -1);
    }

    private static string PtrToString(IntPtr ptr, string fallback)
        => ptr == IntPtr.Zero ? fallback : Marshal.PtrToStringAnsi(ptr) ?? fallback;

    public void Dispose()
    {
        Stop();
        lock (_lock)
        {
            if (_handle != IntPtr.Zero)
            {
                AlsaMidiNative.snd_seq_close(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
