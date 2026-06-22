using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// A running JACK stream (playback or capture). JACK is callback-driven and non-interleaved: the server
/// calls <see cref="Process"/> on its RT thread with one mono float buffer per port, so we
/// de-interleave the engine's block to the output ports (or interleave the input ports for capture).
/// Sample rate and block size come from the server. Our ports auto-connect to the system's physical
/// playback/capture ports.
/// </summary>
internal sealed unsafe class JackStream : INativeStream
{
    private readonly bool _playback;
    private readonly AudioRenderCallback? _render;
    private readonly AudioCaptureCallback? _capture;
    private readonly IntPtr _client;
    private readonly IntPtr[] _ports;
    private readonly int _channels;
    private GCHandle _self;
    private float[] _scratch = Array.Empty<float>();

    public AudioFormat Format { get; }

    private JackStream(bool playback, IntPtr client, IntPtr[] ports, int rate,
        AudioRenderCallback? render, AudioCaptureCallback? capture)
    {
        _playback = playback;
        _client = client;
        _ports = ports;
        _channels = ports.Length;
        _render = render;
        _capture = capture;
        Format = new AudioFormat(rate, _channels);

        _self = GCHandle.Alloc(this);
        JackNative.jack_set_process_callback(client,
            (IntPtr)(delegate* unmanaged[Cdecl]<uint, IntPtr, int>)&ProcessThunk, GCHandle.ToIntPtr(_self));

        if (JackNative.jack_activate(client) != 0)
            throw new InvalidOperationException("jack_activate failed.");

        AutoConnect();
    }

    public static JackStream Open(string clientName, bool playback, int channels,
        AudioRenderCallback? render, AudioCaptureCallback? capture)
    {
        var client = JackNative.jack_client_open(clientName, JackNative.JackNullOption, out _);
        if (client == IntPtr.Zero) throw new InvalidOperationException("jack_client_open failed (no JACK server?).");

        try
        {
            var rate = (int)JackNative.jack_get_sample_rate(client);
            var n = Math.Max(1, channels);
            var ports = new IntPtr[n];
            var flag = playback ? JackNative.JackPortIsOutput : JackNative.JackPortIsInput;
            for (var i = 0; i < n; i++)
            {
                var pname = (playback ? "out_" : "in_") + (i + 1);
                ports[i] = JackNative.jack_port_register(client, pname, JackNative.JACK_DEFAULT_AUDIO_TYPE, flag, 0);
                if (ports[i] == IntPtr.Zero) throw new InvalidOperationException($"jack_port_register({pname}) failed.");
            }

            return new JackStream(playback, client, ports, rate, render, capture);
        }
        catch
        {
            JackNative.jack_client_close(client);
            throw;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ProcessThunk(uint nframes, IntPtr arg)
    {
        try
        {
            if (GCHandle.FromIntPtr(arg).Target is JackStream s) s.Process(nframes);
        }
        catch
        {
            // Never let a managed exception escape onto the RT thread.
        }

        return 0;
    }

    private void Process(uint nframesU)
    {
        var nframes = (int)nframesU;
        var need = nframes * _channels;
        if (_scratch.Length < need) _scratch = new float[need]; // grows only if the server changes block size

        if (_playback)
        {
            var span = _scratch.AsSpan(0, need);
            var render = _render;
            if (render is not null) render(span);
            else span.Clear();

            // De-interleave the block into each mono output port.
            for (var c = 0; c < _channels; c++)
            {
                var dst = (float*)JackNative.jack_port_get_buffer(_ports[c], nframesU);
                if (dst is null) continue;
                for (int f = 0, i = c; f < nframes; f++, i += _channels) dst[f] = _scratch[i];
            }
        }
        else
        {
            // Interleave each mono input port into the block, then hand it to the capture callback.
            for (var c = 0; c < _channels; c++)
            {
                var src = (float*)JackNative.jack_port_get_buffer(_ports[c], nframesU);
                if (src is null) continue;
                for (int f = 0, i = c; f < nframes; f++, i += _channels) _scratch[i] = src[f];
            }

            var capture = _capture;
            capture?.Invoke(_scratch.AsSpan(0, need), _channels);
        }
    }

    // Wires our ports to the system's physical capture/playback ports so audio actually flows.
    private void AutoConnect()
    {
        // For playback we connect our outputs → physical inputs (system playback); for capture, physical
        // outputs (system capture) → our inputs.
        var wantFlags = JackNative.JackPortIsPhysical | (_playback ? JackNative.JackPortIsInput : JackNative.JackPortIsOutput);
        var listPtr = JackNative.jack_get_ports(_client, null, JackNative.JACK_DEFAULT_AUDIO_TYPE, wantFlags);
        if (listPtr == IntPtr.Zero) return;

        try
        {
            var physical = new List<string>();
            for (var p = listPtr; ; p += IntPtr.Size)
            {
                var s = Marshal.ReadIntPtr(p);
                if (s == IntPtr.Zero) break;
                var name = Marshal.PtrToStringAnsi(s);
                if (!string.IsNullOrEmpty(name)) physical.Add(name);
            }

            for (var c = 0; c < _channels && c < physical.Count; c++)
            {
                var mine = Marshal.PtrToStringAnsi(JackNative.jack_port_name(_ports[c]));
                if (string.IsNullOrEmpty(mine)) continue;
                if (_playback) JackNative.jack_connect(_client, mine, physical[c]);
                else JackNative.jack_connect(_client, physical[c], mine);
            }
        }
        finally
        {
            JackNative.jack_free(listPtr);
        }
    }

    public void Dispose()
    {
        JackNative.jack_deactivate(_client);
        JackNative.jack_client_close(_client);
        if (_self.IsAllocated) _self.Free();
    }
}
