using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Ongenet.Core.Audio;
using PW = Ongenet.Audio.Interop.PipeWireNative;

namespace Ongenet.Audio.Native;

/// <summary>
/// Captures a single application's audio in isolation (just that app, not the whole sink mix). Because
/// WirePlumber's auto-connect policy can ignore a target hint, this does the linking itself: it opens a
/// capture stream with NO auto-connect, then creates explicit PipeWire links from the target app node's
/// output ports to the capture stream's input ports (explicit links are left alone by the session
/// manager). Owns a persistent loop+context+core+registry for the link lifetime.
/// </summary>
internal sealed unsafe class PipeWireAppCapture : INativeStream
{
    private const int Rate = 48000;

    private readonly string _appKey;
    private readonly int _channels;
    private readonly int _stride;
    private readonly AudioCaptureCallback _capture;

    private readonly object _lock = new();
    private readonly List<(uint id, string key)> _apps = new();
    private readonly List<(uint portId, uint nodeId, bool output)> _ports = new();

    private IntPtr _loop, _context, _core, _registry, _stream, _streamEvents, _regEvents, _regHook;
    private readonly List<IntPtr> _links = new();
    private GCHandle _self;

    public AudioFormat Format { get; }

    private PipeWireAppCapture(string appKey, int channels, AudioCaptureCallback capture)
    {
        _appKey = appKey;
        _channels = channels;
        _stride = channels * sizeof(float);
        _capture = capture;
        Format = new AudioFormat(Rate, channels);

        PW.EnsureInit();
        _loop = PW.pw_thread_loop_new("ongenet-appcap", IntPtr.Zero);
        if (_loop == IntPtr.Zero) throw new InvalidOperationException("pw_thread_loop_new failed.");
        _self = GCHandle.Alloc(this);

        if (PW.pw_thread_loop_start(_loop) != 0) { Cleanup(); throw new InvalidOperationException("pw_thread_loop_start failed."); }

        SetupUnderLock();
        Thread.Sleep(400);  // let the stream's node/ports and the graph register
        LinkUnderLock();
    }

    public static PipeWireAppCapture Open(string appKey, int channels, AudioCaptureCallback capture)
        => new(appKey, Math.Clamp(channels, 1, 8), capture);

    private void SetupUnderLock()
    {
        PW.pw_thread_loop_lock(_loop);
        try
        {
            // Persistent connection used both to watch the graph (registry) and to create the links (core).
            _context = PW.pw_context_new(PW.pw_thread_loop_get_loop(_loop), IntPtr.Zero, 0);
            _core = PW.pw_context_connect(_context, IntPtr.Zero, 0);
            _registry = PW.CoreGetRegistry(_core);

            _regEvents = Marshal.AllocHGlobal(PW.REGEVENTS_SIZE);
            for (var i = 0; i < PW.REGEVENTS_SIZE; i++) Marshal.WriteByte(_regEvents, i, 0);
            Marshal.WriteInt32(_regEvents, 0, PW.PW_VERSION_REGISTRY_EVENTS);
            Marshal.WriteIntPtr(_regEvents, PW.REGEVENTS_OFF_GLOBAL,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, uint, uint, IntPtr, uint, IntPtr, void>)&GlobalThunk);
            _regHook = Marshal.AllocHGlobal(PW.SPA_HOOK_SIZE);
            for (var i = 0; i < PW.SPA_HOOK_SIZE; i++) Marshal.WriteByte(_regHook, i, 0);
            PW.pw_proxy_add_object_listener(_registry, _regHook, _regEvents, GCHandle.ToIntPtr(_self));

            // Capture stream, NO auto-connect (we link it manually).
            var props = PW.pw_properties_new(IntPtr.Zero);
            PW.pw_properties_set(props, "media.type", "Audio");
            PW.pw_properties_set(props, "media.category", "Capture");
            PW.pw_properties_set(props, "media.role", "Production");
            PW.pw_properties_set(props, "application.name", "Ongenet");
            PW.pw_properties_set(props, "node.name", "Ongenet App Capture");

            _streamEvents = Marshal.AllocHGlobal(PW.EVENTS_SIZE);
            for (var i = 0; i < PW.EVENTS_SIZE; i++) Marshal.WriteByte(_streamEvents, i, 0);
            Marshal.WriteInt32(_streamEvents, PW.EVENTS_OFF_VERSION, PW.PW_VERSION_STREAM_EVENTS);
            Marshal.WriteIntPtr(_streamEvents, PW.EVENTS_OFF_PROCESS,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&ProcessThunk);

            _stream = PW.pw_stream_new_simple(PW.pw_thread_loop_get_loop(_loop), "ongenet-appcap", props, _streamEvents, GCHandle.ToIntPtr(_self));

            var pod = PipeWireStream.BuildAudioFormatPod(Rate, _channels);
            var podPin = GCHandle.Alloc(pod, GCHandleType.Pinned);
            try
            {
                var arr = stackalloc IntPtr[1];
                arr[0] = podPin.AddrOfPinnedObject();
                var flags = PW.PW_STREAM_FLAG_MAP_BUFFERS | PW.PW_STREAM_FLAG_RT_PROCESS; // NOT autoconnect
                PW.pw_stream_connect(_stream, PW.PW_DIRECTION_INPUT, PW.PW_ID_ANY, flags, (IntPtr)arr, 1);
            }
            finally { podPin.Free(); }
        }
        finally
        {
            PW.pw_thread_loop_unlock(_loop);
        }
    }

    private void LinkUnderLock()
    {
        PW.pw_thread_loop_lock(_loop);
        try
        {
            var myNode = PW.pw_stream_get_node_id(_stream);
            uint appNode = 0;
            lock (_lock)
            {
                foreach (var (id, key) in _apps)
                    if (key == _appKey) { appNode = id; break; }
            }

            if (appNode == 0 || myNode == 0) return; // app gone or stream not ready → no links (silent, but alive)

            List<uint> appOut = new(), myIn = new();
            lock (_lock)
            {
                foreach (var (portId, nodeId, output) in _ports)
                {
                    if (nodeId == appNode && output) appOut.Add(portId);
                    else if (nodeId == myNode && !output) myIn.Add(portId);
                }
            }

            var n = Math.Min(appOut.Count, myIn.Count);
            for (var i = 0; i < n; i++) CreateLink(appNode, appOut[i], myNode, myIn[i]);
        }
        finally
        {
            PW.pw_thread_loop_unlock(_loop);
        }
    }

    private void CreateLink(uint outNode, uint outPort, uint inNode, uint inPort)
    {
        var props = PW.pw_properties_new(IntPtr.Zero);
        PW.pw_properties_set(props, "link.output.node", outNode.ToString());
        PW.pw_properties_set(props, "link.output.port", outPort.ToString());
        PW.pw_properties_set(props, "link.input.node", inNode.ToString());
        PW.pw_properties_set(props, "link.input.port", inPort.ToString());
        var link = PW.CoreCreateLink(_core, props); // props' first member is the spa_dict create_object wants
        if (link != IntPtr.Zero) _links.Add(link);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void GlobalThunk(IntPtr data, uint id, uint permissions, IntPtr type, uint version, IntPtr props)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is not PipeWireAppCapture self) return;
            var t = Marshal.PtrToStringAnsi(type);
            if (t == "PipeWire:Interface:Node")
            {
                if (PW.DictLookup(props, "media.class") != "Stream/Output/Audio") return;
                var nodeName = PW.DictLookup(props, "node.name");
                var app = PW.DictLookup(props, "application.name");
                var key = !string.IsNullOrEmpty(nodeName) ? nodeName : app;
                if (!string.IsNullOrEmpty(key)) lock (self._lock) self._apps.Add((id, key!));
            }
            else if (t == "PipeWire:Interface:Port")
            {
                var nid = PW.DictLookup(props, "node.id");
                var dir = PW.DictLookup(props, "port.direction");
                if (uint.TryParse(nid, out var nodeId) && dir is not null)
                    lock (self._lock) self._ports.Add((id, nodeId, dir == "out"));
            }
        }
        catch
        {
            // Never throw back into libpipewire.
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ProcessThunk(IntPtr data)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is PipeWireAppCapture s) s.OnProcess();
        }
        catch
        {
            // Never let a managed exception escape onto the RT thread.
        }
    }

    private void OnProcess()
    {
        var pb = PW.pw_stream_dequeue_buffer(_stream);
        if (pb == IntPtr.Zero) return;
        var spaBuf = *(IntPtr*)((byte*)pb + PW.PWBUF_OFF_BUFFER);
        if (spaBuf == IntPtr.Zero) { PW.pw_stream_queue_buffer(_stream, pb); return; }
        var datas = *(IntPtr*)((byte*)spaBuf + PW.SPABUF_OFF_DATAS);
        if (datas == IntPtr.Zero) { PW.pw_stream_queue_buffer(_stream, pb); return; }

        var d = (byte*)datas;
        var dataPtr = *(IntPtr*)(d + PW.SPADATA_OFF_DATA);
        var chunk = (byte*)*(IntPtr*)(d + PW.SPADATA_OFF_CHUNK);
        if (dataPtr != IntPtr.Zero && chunk != null)
        {
            var size = *(int*)(chunk + PW.SPACHUNK_OFF_SIZE);
            var frames = size / _stride;
            if (frames > 0) _capture(new ReadOnlySpan<float>((void*)dataPtr, frames * _channels), _channels);
        }

        PW.pw_stream_queue_buffer(_stream, pb);
    }

    public void Dispose()
    {
        if (_loop != IntPtr.Zero) PW.pw_thread_loop_lock(_loop);
        try
        {
            foreach (var link in _links) PW.pw_proxy_destroy(link);
            _links.Clear();
            if (_stream != IntPtr.Zero) PW.pw_stream_destroy(_stream);
            if (_registry != IntPtr.Zero) PW.pw_proxy_destroy(_registry);
            if (_core != IntPtr.Zero) PW.pw_core_disconnect(_core);
            if (_context != IntPtr.Zero) PW.pw_context_destroy(_context);
        }
        finally
        {
            if (_loop != IntPtr.Zero) PW.pw_thread_loop_unlock(_loop);
        }

        Cleanup();
    }

    private void Cleanup()
    {
        if (_loop != IntPtr.Zero) { PW.pw_thread_loop_stop(_loop); PW.pw_thread_loop_destroy(_loop); _loop = IntPtr.Zero; }
        if (_streamEvents != IntPtr.Zero) { Marshal.FreeHGlobal(_streamEvents); _streamEvents = IntPtr.Zero; }
        if (_regEvents != IntPtr.Zero) { Marshal.FreeHGlobal(_regEvents); _regEvents = IntPtr.Zero; }
        if (_regHook != IntPtr.Zero) { Marshal.FreeHGlobal(_regHook); _regHook = IntPtr.Zero; }
        if (_self.IsAllocated) _self.Free();
    }
}
