using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;
using PW = Ongenet.Audio.Interop.PipeWireNative;

namespace Ongenet.Audio.Native;

/// <summary>
/// A running PipeWire stream (playback or capture) on a <c>pw_thread_loop</c>. Negotiates float32
/// interleaved at 48 kHz (the engine's native layout) via a hand-built SPA audio/raw format POD whose
/// byte layout was verified against <c>spa_format_audio_raw_build</c>. The server calls
/// <see cref="OnProcess"/> on the loop thread; we fill (or drain) the mapped buffer there. Auto-connects
/// to the default sink/source and mixes with other apps — the lowest-latency, fully-native Linux path.
/// </summary>
internal sealed unsafe class PipeWireStream : INativeStream
{
    private const int Rate = 48000;

    // Stream state-change diagnostics to stderr when ONGENET_PW_DEBUG=1 (handy for shaking out routing).
    private static readonly bool Debug = Environment.GetEnvironmentVariable("ONGENET_PW_DEBUG") == "1";

    private readonly bool _playback;
    private readonly AudioRenderCallback? _render;
    private readonly AudioCaptureCallback? _capture;
    private readonly int _channels;
    private readonly int _stride;

    private readonly IntPtr _loop;
    private readonly IntPtr _stream;
    private readonly IntPtr _events; // unmanaged pw_stream_events (kept alive for the stream's lifetime)
    private GCHandle _self;

    public AudioFormat Format { get; }

    private readonly uint _targetId;

    private PipeWireStream(bool playback, int channels, string? targetObject, bool captureSink, uint targetId,
        AudioRenderCallback? render, AudioCaptureCallback? capture)
    {
        _targetId = targetId;
        _playback = playback;
        _channels = channels;
        _stride = channels * sizeof(float);
        _render = render;
        _capture = capture;
        Format = new AudioFormat(Rate, channels);

        PW.EnsureInit();
        _loop = PW.pw_thread_loop_new("ongenet", IntPtr.Zero);
        if (_loop == IntPtr.Zero) throw new InvalidOperationException("pw_thread_loop_new failed.");

        // Stream metadata so PipeWire routes/labels it sensibly.
        var props = PW.pw_properties_new(IntPtr.Zero);
        PW.pw_properties_set(props, "media.type", "Audio");
        PW.pw_properties_set(props, "media.category", playback ? "Playback" : "Capture");
        PW.pw_properties_set(props, "media.role", "Production");
        PW.pw_properties_set(props, "application.name", "Ongenet");
        PW.pw_properties_set(props, "node.name", playback ? "Ongenet" : "Ongenet Capture");
        // Capture-from-sink: record whatever plays on a sink (its monitor) — used to record other apps.
        if (!playback && captureSink) PW.pw_properties_set(props, "stream.capture.sink", "true");

        // Pin to a specific sink/source node when the user picked one; otherwise follow the default.
        // Set both the modern (target.object) and legacy (node.target) keys for WirePlumber-version spread.
        if (!string.IsNullOrEmpty(targetObject))
        {
            PW.pw_properties_set(props, "target.object", targetObject);
            PW.pw_properties_set(props, "node.target", targetObject);
        }

        _events = BuildEvents();
        _self = GCHandle.Alloc(this);

        _stream = PW.pw_stream_new_simple(PW.pw_thread_loop_get_loop(_loop),
            playback ? "ongenet-out" : "ongenet-in", props, _events, GCHandle.ToIntPtr(_self));
        if (_stream == IntPtr.Zero) { Cleanup(); throw new InvalidOperationException("pw_stream_new_simple failed."); }

        Connect();

        if (PW.pw_thread_loop_start(_loop) != 0) { Cleanup(); throw new InvalidOperationException("pw_thread_loop_start failed."); }
    }

    public static PipeWireStream Open(bool playback, int channels, string? targetObject, bool captureSink,
        AudioRenderCallback? render, AudioCaptureCallback? capture, uint targetId = PW.PW_ID_ANY)
        => new(playback, Math.Clamp(channels, 1, 8), targetObject, captureSink, targetId, render, capture);

    // Builds the pw_stream_events struct in unmanaged memory: version + process (+ state_changed for logs).
    private IntPtr BuildEvents()
    {
        var ev = Marshal.AllocHGlobal(PW.EVENTS_SIZE);
        for (var i = 0; i < PW.EVENTS_SIZE; i++) Marshal.WriteByte(ev, i, 0);
        Marshal.WriteInt32(ev, PW.EVENTS_OFF_VERSION, PW.PW_VERSION_STREAM_EVENTS);
        Marshal.WriteIntPtr(ev, PW.EVENTS_OFF_PROCESS,
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&ProcessThunk);
        Marshal.WriteIntPtr(ev, PW.EVENTS_OFF_STATE_CHANGED,
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, void>)&StateThunk);
        return ev;
    }

    private void Connect()
    {
        var pod = BuildAudioFormatPod(Rate, _channels);
        var podPin = GCHandle.Alloc(pod, GCHandleType.Pinned);
        try
        {
            // pw_stream_connect wants `const spa_pod **params`: a pointer to an array of one POD pointer.
            var podPtr = podPin.AddrOfPinnedObject();
            var arr = stackalloc IntPtr[1];
            arr[0] = podPtr;
            var dir = _playback ? PW.PW_DIRECTION_OUTPUT : PW.PW_DIRECTION_INPUT;
            var flags = PW.PW_STREAM_FLAG_AUTOCONNECT | PW.PW_STREAM_FLAG_MAP_BUFFERS | PW.PW_STREAM_FLAG_RT_PROCESS;

            PW.pw_thread_loop_lock(_loop);
            try
            {
                // Pin to a specific node id when given (per-app capture / explicit target); else PW_ID_ANY.
                var rc = PW.pw_stream_connect(_stream, dir, _targetId, flags, (IntPtr)arr, 1);
                if (rc != 0) throw new InvalidOperationException($"pw_stream_connect failed: {rc}");
            }
            finally
            {
                PW.pw_thread_loop_unlock(_loop);
            }
        }
        finally
        {
            podPin.Free(); // the server copies the POD during connect
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ProcessThunk(IntPtr data)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is PipeWireStream s) s.OnProcess();
        }
        catch
        {
            // Never let a managed exception escape onto the RT thread.
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void StateThunk(IntPtr data, int old, int state, IntPtr error)
    {
        if (!Debug) return;
        try
        {
            var msg = error != IntPtr.Zero ? Marshal.PtrToStringAnsi(error) : null;
            Console.Error.WriteLine($"[pipewire] state {old}->{state}{(msg is null ? "" : $" error={msg}")}");
        }
        catch { /* ignore */ }
    }

    private void OnProcess()
    {
        var pb = PW.pw_stream_dequeue_buffer(_stream);
        if (pb == IntPtr.Zero) return;

        // pw_buffer.buffer → spa_buffer.datas → spa_data[0] { maxsize, data, chunk }.
        var spaBuf = *(IntPtr*)((byte*)pb + PW.PWBUF_OFF_BUFFER);
        if (spaBuf == IntPtr.Zero) { PW.pw_stream_queue_buffer(_stream, pb); return; }
        var datas = *(IntPtr*)((byte*)spaBuf + PW.SPABUF_OFF_DATAS);
        if (datas == IntPtr.Zero) { PW.pw_stream_queue_buffer(_stream, pb); return; }

        var d = (byte*)datas; // first spa_data
        var dataPtr = *(IntPtr*)(d + PW.SPADATA_OFF_DATA);
        var chunk = (byte*)*(IntPtr*)(d + PW.SPADATA_OFF_CHUNK);
        if (dataPtr == IntPtr.Zero || chunk == null) { PW.pw_stream_queue_buffer(_stream, pb); return; }

        if (_playback)
        {
            var maxsize = *(int*)(d + PW.SPADATA_OFF_MAXSIZE);
            var frames = maxsize / _stride;
            var requested = *(long*)((byte*)pb + PW.PWBUF_OFF_REQUESTED);
            if (requested > 0 && requested < frames) frames = (int)requested;

            var span = new Span<float>((void*)dataPtr, frames * _channels);
            var render = _render;
            if (render is not null) render(span);
            else span.Clear();

            *(int*)(chunk + PW.SPACHUNK_OFF_OFFSET) = 0;
            *(int*)(chunk + PW.SPACHUNK_OFF_SIZE) = frames * _stride;
            *(int*)(chunk + PW.SPACHUNK_OFF_STRIDE) = _stride;
        }
        else
        {
            var size = *(int*)(chunk + PW.SPACHUNK_OFF_SIZE);
            var frames = size / _stride;
            if (frames > 0)
            {
                var capture = _capture;
                if (capture is not null)
                {
                    var span = new ReadOnlySpan<float>((void*)dataPtr, frames * _channels);
                    capture(span, _channels);
                }
            }
        }

        PW.pw_stream_queue_buffer(_stream, pb);
    }

    public void Dispose()
    {
        if (_loop != IntPtr.Zero) PW.pw_thread_loop_stop(_loop);
        Cleanup();
    }

    private void Cleanup()
    {
        if (_stream != IntPtr.Zero) PW.pw_stream_destroy(_stream);
        if (_loop != IntPtr.Zero) PW.pw_thread_loop_destroy(_loop);
        if (_events != IntPtr.Zero) Marshal.FreeHGlobal(_events);
        if (_self.IsAllocated) _self.Free();
    }

    // --- SPA audio/raw format POD ----------------------------------------------------------------
    // Reproduces spa_format_audio_raw_build(SPA_PARAM_EnumFormat, {F32, rate, channels}) byte-for-byte
    // (verified against the header builder's output).
    internal static byte[] BuildAudioFormatPod(int rate, int channels)
    {
        const int SPA_TYPE_Id = 3, SPA_TYPE_Int = 4, SPA_TYPE_Array = 13, SPA_TYPE_Object = 15;
        const uint SPA_TYPE_OBJECT_Format = 0x40003;
        const uint SPA_PARAM_EnumFormat = 3;
        const uint KEY_mediaType = 0x1, KEY_mediaSubtype = 0x2;
        const uint KEY_format = 0x10001, KEY_rate = 0x10003, KEY_channels = 0x10004, KEY_position = 0x10005;
        const uint MEDIA_TYPE_audio = 1, MEDIA_SUBTYPE_raw = 1, AUDIO_FORMAT_F32 = 283;

        using var body = new MemoryStream();
        var w = new BinaryWriter(body);

        void Pad() { while ((body.Length & 7) != 0) w.Write((byte)0); }
        void Prop(uint key, Action writeValue) { w.Write(key); w.Write(0u); writeValue(); }
        void IdVal(uint v) { w.Write(4u); w.Write(SPA_TYPE_Id); w.Write(v); Pad(); }
        void IntVal(int v) { w.Write(4u); w.Write(SPA_TYPE_Int); w.Write(v); Pad(); }

        w.Write(SPA_TYPE_OBJECT_Format); // object body: type
        w.Write(SPA_PARAM_EnumFormat);   //              id
        Prop(KEY_mediaType, () => IdVal(MEDIA_TYPE_audio));
        Prop(KEY_mediaSubtype, () => IdVal(MEDIA_SUBTYPE_raw));
        Prop(KEY_format, () => IdVal(AUDIO_FORMAT_F32));
        Prop(KEY_rate, () => IntVal(rate));
        Prop(KEY_channels, () => IntVal(channels));
        Prop(KEY_position, () =>
        {
            // value = Array POD of `channels` Id elements (all 0 = UNKNOWN; PipeWire assigns defaults).
            w.Write((uint)(8 + 4 * channels)); // body size: child header (8) + elements
            w.Write(SPA_TYPE_Array);
            w.Write(4u);            // child element size
            w.Write(SPA_TYPE_Id);   // child element type
            for (var i = 0; i < channels; i++) w.Write(0u);
            Pad();
        });
        w.Flush();

        var bodyBytes = body.ToArray();
        using var pod = new MemoryStream();
        var pw = new BinaryWriter(pod);
        pw.Write((uint)bodyBytes.Length); // POD header: size (body only)
        pw.Write(SPA_TYPE_Object);        //             type
        pw.Write(bodyBytes);
        pw.Flush();
        return pod.ToArray();
    }
}
