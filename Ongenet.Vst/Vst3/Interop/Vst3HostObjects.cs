using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ongenet.Vst.Vst3.Interop;

/// <summary>
/// The host-side COM objects Ongenet hands to a VST3 plugin: <c>IHostApplication</c> (the init context),
/// <c>IComponentHandler</c> (parameter edit notifications), <c>IPlugFrame</c> (view resize requests),
/// <c>IEventList</c> + <c>IParameterChanges</c>/<c>IParamValueQueue</c> (note + parameter input each
/// process block), and a memory-backed <c>IBStream</c> (used to copy component state into the
/// controller). Each object is a native block whose first field points at a shared static vtable of
/// <see cref="UnmanagedCallersOnlyAttribute"/> thunks; a managed <see cref="GCHandle"/> is stored beside
/// it so the thunks can recover their owner (the <see cref="Vst3PluginBase"/>, or a
/// <see cref="Vst3MemoryStream"/> for streams).
/// </summary>
public static unsafe class Vst3Host
{
    [StructLayout(LayoutKind.Sequential)]
    private struct HostObj { public void* Vtbl; public nint Gc; public int Index; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ConnObj { public void* Vtbl; public nint Gc; public nint Peer; }

    private static readonly object Lock = new();
    private static void* _hostAppVtbl, _handlerVtbl, _frameVtbl, _eventListVtbl, _paramChangesVtbl, _paramQueueVtbl, _streamVtbl, _runLoopVtbl, _connVtbl, _messageVtbl, _attrListVtbl;

    private static Vst3PluginBase? Plugin(void* self)
    {
        try { return GCHandle.FromIntPtr(((HostObj*)self)->Gc).Target as Vst3PluginBase; }
        catch { return null; }
    }

    private static int Index(void* self) => ((HostObj*)self)->Index;

    private static void* MakeVtbl(Span<nint> fns)
    {
        var p = (nint*)Marshal.AllocHGlobal(fns.Length * sizeof(nint));
        for (var i = 0; i < fns.Length; i++) p[i] = fns[i];
        return p;
    }

    private static void* MakeObj(void* vtbl, nint gc, int index = 0)
    {
        var o = (HostObj*)Marshal.AllocHGlobal(sizeof(HostObj));
        o->Vtbl = vtbl;
        o->Gc = gc;
        o->Index = index;
        return o;
    }

    public static void Free(void* obj) { if (obj != null) Marshal.FreeHGlobal((nint)obj); }

    // ---------------- IHostApplication ----------------

    public static void* BuildHostApplication(nint gc)
    {
        lock (Lock)
            if (_hostAppVtbl == null) _hostAppVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&HostAppQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&AddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&Release,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, int>)&HostAppGetName,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, byte*, void**, int>)&HostAppCreateInstance,
            });
        return MakeObj(_hostAppVtbl, gc);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HostAppQuery(void* self, byte* iid, void** obj)
    {
        if (Vst3Api.IidEquals(iid, Vst3Api.IidHostApplication) || Vst3Api.IidEquals(iid, Vst3Api.IidFUnknown))
        {
            *obj = self; return Vst3Api.ResultOk;
        }

        *obj = null; return Vst3Api.NoInterface;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HostAppGetName(void* self, byte* name)
    {
        Vst3Api.WriteUtf16(name, "Ongenet", Vst3Api.Str128Bytes);
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HostAppCreateInstance(void* self, byte* cid, byte* iid, void** obj)
    {
        // Plugins ask the host to create an IMessage (carrying an IAttributeList) so a component and its
        // separate controller can talk over IConnectionPoint::notify. Vending a real, refcounted message —
        // rather than failing with kNoInterface — is what lets us keep the component<->controller connection
        // wired without plugins like Philharmonik 2 dereferencing a null message, and is required for
        // separate-controller plugins such as Sylenth1 to finish initializing their editor.
        if (iid != null && Vst3Api.IidEquals(iid, Vst3Api.IidMessage))
        {
            *obj = NewMessage();
            return Vst3Api.ResultOk;
        }

        if (iid != null && Vst3Api.IidEquals(iid, Vst3Api.IidAttributeList))
        {
            *obj = NewAttributeList();
            return Vst3Api.ResultOk;
        }

        *obj = null; return Vst3Api.NoInterface;
    }

    // ---------------- IComponentHandler ----------------

    public static void* BuildComponentHandler(nint gc)
    {
        lock (Lock)
            if (_handlerVtbl == null) _handlerVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&HandlerQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&AddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&Release,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint, int>)&HandlerBeginEdit,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint, double, int>)&HandlerPerformEdit,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint, int>)&HandlerEndEdit,
                (nint)(delegate* unmanaged[Cdecl]<void*, int, int>)&HandlerRestart,
            });
        return MakeObj(_handlerVtbl, gc);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HandlerQuery(void* self, byte* iid, void** obj)
    {
        if (Vst3Api.IidEquals(iid, Vst3Api.IidComponentHandler) || Vst3Api.IidEquals(iid, Vst3Api.IidFUnknown))
        {
            *obj = self; return Vst3Api.ResultOk;
        }

        *obj = null; return Vst3Api.NoInterface;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HandlerBeginEdit(void* self, uint id) => Vst3Api.ResultOk;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HandlerPerformEdit(void* self, uint id, double value)
    {
        Plugin(self)?.OnControllerEdit(id, value);
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HandlerEndEdit(void* self, uint id) => Vst3Api.ResultOk;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HandlerRestart(void* self, int flags) => Vst3Api.ResultOk;

    // ---------------- IPlugFrame ----------------

    public static void* BuildPlugFrame(nint gc)
    {
        lock (Lock)
            if (_frameVtbl == null) _frameVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&FrameQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&AddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&Release,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, Vst3Api.ViewRect*, int>)&FrameResizeView,
            });
        return MakeObj(_frameVtbl, gc);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int FrameQuery(void* self, byte* iid, void** obj)
    {
        if (Vst3Api.IidEquals(iid, Vst3Api.IidPlugFrame) || Vst3Api.IidEquals(iid, Vst3Api.IidFUnknown))
        {
            *obj = self; return Vst3Api.ResultOk;
        }

        // X11 plugin GUIs query the frame for IRunLoop; hand back the plugin's run-loop object so it can
        // register its file descriptors + timers with us (serviced on the UI thread in PumpEditor).
        if (Vst3Api.IidEquals(iid, Vst3Api.IidRunLoop))
        {
            var rl = Plugin(self) is { RunLoopPtr: var p } && p != 0 ? (void*)p : null;
            if (rl != null) { *obj = rl; return Vst3Api.ResultOk; }
        }

        *obj = null; return Vst3Api.NoInterface;
    }

    // ---------------- IConnectionPoint proxy ----------------
    // Sits between the plugin's component and controller connection points. We connect each plugin side
    // to one of these proxies instead of directly to each other, so a plugin that answers a notify() with
    // another notify() (Philharmonik) can't recurse straight back across a direct peer link. The proxy
    // forwards notify() to the real peer, guarded so a re-entrant notify on the same thread is dropped.

    public static void* BuildConnectionProxy(nint gc, nint peer)
    {
        lock (Lock)
            if (_connVtbl == null) _connVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&ConnQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&AddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&Release,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, int>)&ConnConnect,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, int>)&ConnDisconnect,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, int>)&ConnNotify,
            });

        var o = (ConnObj*)Marshal.AllocHGlobal(sizeof(ConnObj));
        o->Vtbl = _connVtbl;
        o->Gc = gc;
        o->Peer = peer;
        return o;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ConnQuery(void* self, byte* iid, void** obj)
    {
        if (Vst3Api.IidEquals(iid, Vst3Api.IidConnectionPoint) || Vst3Api.IidEquals(iid, Vst3Api.IidFUnknown))
        {
            *obj = self; return Vst3Api.ResultOk;
        }

        *obj = null; return Vst3Api.NoInterface;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ConnConnect(void* self, void* other) => Vst3Api.ResultOk;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ConnDisconnect(void* self, void* other) => Vst3Api.ResultOk;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ConnNotify(void* self, void* message)
    {
        // Drop a notify that arrives while we're already delivering one on this thread — that nesting is
        // exactly the component<->controller ping-pong that would otherwise overflow the stack.
        if (!Vst3PluginBase.BeginNotify()) return Vst3Api.ResultOk;
        try
        {
            var peer = (void*)((ConnObj*)self)->Peer;
            if (peer == null) return Vst3Api.ResultOk;
            var v = *(Vst3Api.ConnectionPointVtbl**)peer;
            return v->Notify != null ? v->Notify(peer, message) : Vst3Api.ResultOk;
        }
        finally { Vst3PluginBase.EndNotify(); }
    }

    // ---------------- IMessage + IAttributeList ----------------
    // A message (and the attribute list it owns) is created on demand by a plugin, filled in, handed to the
    // peer connection point via notify(), then released. Unlike the host's long-lived singletons these are
    // genuinely owned by the plugin, so they carry a real refcount (RcObj.RefCount) and free themselves —
    // native block, GCHandle, and any binary buffers — when the last reference is released.

    [StructLayout(LayoutKind.Sequential)]
    private struct RcObj { public void* Vtbl; public nint Gc; public int RefCount; }

    private sealed class Message { public nint IdNative; public nint Attr; }

    private sealed class AttrList
    {
        // Boxed long / double / string, or Bin for binary. Binary is copied into a native buffer so the
        // pointer handed back from getBinary stays valid until the attribute is overwritten or freed.
        public readonly Dictionary<string, object> Map = new();
        public sealed class Bin { public nint Ptr; public uint Len; }
    }

    private static T? Rc<T>(void* self) where T : class
        => GCHandle.FromIntPtr(((RcObj*)self)->Gc).Target as T;

    private static void* NewRcObj(void* vtbl, object managed)
    {
        var o = (RcObj*)Marshal.AllocHGlobal(sizeof(RcObj));
        o->Vtbl = vtbl;
        o->Gc = GCHandle.ToIntPtr(GCHandle.Alloc(managed));
        o->RefCount = 1;
        return o;
    }

    // Managed cores (callable directly); the [UnmanagedCallersOnly] thunks below just forward to them.
    private static uint RcAddRefImpl(void* self) => (uint)Interlocked.Increment(ref ((RcObj*)self)->RefCount);

    private static uint RcReleaseImpl(void* self)
    {
        var c = Interlocked.Decrement(ref ((RcObj*)self)->RefCount);
        if (c > 0) return (uint)c;
        FreeRcObj(self);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint RcAddRef(void* self) => RcAddRefImpl(self);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint RcRelease(void* self) => RcReleaseImpl(self);

    private static void FreeRcObj(void* self)
    {
        var handle = GCHandle.FromIntPtr(((RcObj*)self)->Gc);
        switch (handle.Target)
        {
            case Message m:
                if (m.IdNative != 0) Marshal.FreeHGlobal(m.IdNative);
                if (m.Attr != 0) RcReleaseImpl((void*)m.Attr); // drop the message's ref on its attribute list
                break;
            case AttrList a:
                foreach (var v in a.Map.Values)
                    if (v is AttrList.Bin b && b.Ptr != 0) Marshal.FreeHGlobal(b.Ptr);
                break;
        }

        handle.Free();
        Marshal.FreeHGlobal((nint)self);
    }

    private static void* MessageVtbl()
    {
        lock (Lock)
            if (_messageVtbl == null) _messageVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&MsgQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&RcAddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&RcRelease,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*>)&MsgGetId,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void>)&MsgSetId,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*>)&MsgGetAttributes,
            });
        return _messageVtbl;
    }

    private static void* AttrListVtbl()
    {
        lock (Lock)
            if (_attrListVtbl == null) _attrListVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&AttrQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&RcAddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&RcRelease,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, long, int>)&AttrSetInt,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, long*, int>)&AttrGetInt,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, double, int>)&AttrSetFloat,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, double*, int>)&AttrGetFloat,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, byte*, int>)&AttrSetString,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, byte*, uint, int>)&AttrGetString,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void*, uint, int>)&AttrSetBinary,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, uint*, int>)&AttrGetBinary,
            });
        return _attrListVtbl;
    }

    private static void* NewAttributeList() => NewRcObj(AttrListVtbl(), new AttrList());

    private static void* NewMessage()
    {
        var attr = NewAttributeList();
        // Start with a valid (empty) ID buffer so getMessageID never returns null — a plugin or yabridge
        // that strlen()s the ID before setMessageID is called would otherwise crash.
        return NewRcObj(MessageVtbl(), new Message { Attr = (nint)attr, IdNative = Marshal.StringToHGlobalAnsi("") });
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MsgQuery(void* self, byte* iid, void** obj)
    {
        if (Vst3Api.IidEquals(iid, Vst3Api.IidMessage) || Vst3Api.IidEquals(iid, Vst3Api.IidFUnknown))
        {
            RcAddRefImpl(self); *obj = self; return Vst3Api.ResultOk;
        }

        *obj = null; return Vst3Api.NoInterface;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte* MsgGetId(void* self) => (byte*)(Rc<Message>(self)?.IdNative ?? 0);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void MsgSetId(void* self, byte* id)
    {
        if (Rc<Message>(self) is not { } m) return;
        if (m.IdNative != 0) { Marshal.FreeHGlobal(m.IdNative); m.IdNative = 0; }
        if (id != null) m.IdNative = Marshal.StringToHGlobalAnsi(Vst3Api.ReadAscii(id, 1024));
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void* MsgGetAttributes(void* self) => (void*)(Rc<Message>(self)?.Attr ?? 0);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AttrQuery(void* self, byte* iid, void** obj)
    {
        if (Vst3Api.IidEquals(iid, Vst3Api.IidAttributeList) || Vst3Api.IidEquals(iid, Vst3Api.IidFUnknown))
        {
            RcAddRefImpl(self); *obj = self; return Vst3Api.ResultOk;
        }

        *obj = null; return Vst3Api.NoInterface;
    }

    // 'id' is an ASCII AttrID (const char*). On overwrite, release any binary buffer the key previously held.
    private static void AttrPut(void* self, byte* id, object value)
    {
        if (Rc<AttrList>(self) is not { } a) return;
        var key = Vst3Api.ReadAscii(id, 256);
        if (a.Map.TryGetValue(key, out var old) && old is AttrList.Bin b && b.Ptr != 0) Marshal.FreeHGlobal(b.Ptr);
        a.Map[key] = value;
    }

    private static bool AttrTryGet(void* self, byte* id, out object? value)
    {
        value = null;
        return Rc<AttrList>(self) is { } a && a.Map.TryGetValue(Vst3Api.ReadAscii(id, 256), out value);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AttrSetInt(void* self, byte* id, long value) { AttrPut(self, id, value); return Vst3Api.ResultOk; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AttrGetInt(void* self, byte* id, long* value)
    {
        if (AttrTryGet(self, id, out var v) && v is long l) { *value = l; return Vst3Api.ResultOk; }
        *value = 0; return Vst3Api.ResultFalse;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AttrSetFloat(void* self, byte* id, double value) { AttrPut(self, id, value); return Vst3Api.ResultOk; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AttrGetFloat(void* self, byte* id, double* value)
    {
        if (AttrTryGet(self, id, out var v) && v is double d) { *value = d; return Vst3Api.ResultOk; }
        *value = 0; return Vst3Api.ResultFalse;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AttrSetString(void* self, byte* id, byte* str)
    {
        AttrPut(self, id, Vst3Api.ReadUtf16(str, 1 << 16));
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AttrGetString(void* self, byte* id, byte* str, uint sizeInBytes)
    {
        if (AttrTryGet(self, id, out var v) && v is string s)
        {
            Vst3Api.WriteUtf16(str, s, (int)sizeInBytes);
            return Vst3Api.ResultOk;
        }

        if (str != null && sizeInBytes >= 2) { str[0] = 0; str[1] = 0; }
        return Vst3Api.ResultFalse;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AttrSetBinary(void* self, byte* id, void* data, uint sizeInBytes)
    {
        var bin = new AttrList.Bin { Len = sizeInBytes, Ptr = Marshal.AllocHGlobal((int)Math.Max(1, sizeInBytes)) };
        if (data != null && sizeInBytes > 0) Buffer.MemoryCopy(data, (void*)bin.Ptr, sizeInBytes, sizeInBytes);
        AttrPut(self, id, bin);
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AttrGetBinary(void* self, byte* id, void** data, uint* sizeInBytes)
    {
        if (AttrTryGet(self, id, out var v) && v is AttrList.Bin b)
        {
            *data = (void*)b.Ptr; *sizeInBytes = b.Len; return Vst3Api.ResultOk;
        }

        *data = null; *sizeInBytes = 0; return Vst3Api.ResultFalse;
    }

    // ---------------- IRunLoop (Steinberg::Linux) ----------------

    public static void* BuildRunLoop(nint gc)
    {
        lock (Lock)
            if (_runLoopVtbl == null) _runLoopVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&RunLoopQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&AddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&Release,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, int, int>)&RunLoopRegisterEventHandler,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, int>)&RunLoopUnregisterEventHandler,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, ulong, int>)&RunLoopRegisterTimer,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, int>)&RunLoopUnregisterTimer,
            });
        return MakeObj(_runLoopVtbl, gc);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RunLoopQuery(void* self, byte* iid, void** obj) => QuerySelfIf(self, iid, obj, Vst3Api.IidRunLoop);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RunLoopRegisterEventHandler(void* self, void* handler, int fd)
    {
        Plugin(self)?.RegisterFd((nint)handler, fd);
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RunLoopUnregisterEventHandler(void* self, void* handler)
    {
        Plugin(self)?.UnregisterFd((nint)handler);
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RunLoopRegisterTimer(void* self, void* handler, ulong milliseconds)
    {
        Plugin(self)?.RegisterTimer((nint)handler, milliseconds);
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RunLoopUnregisterTimer(void* self, void* handler)
    {
        Plugin(self)?.UnregisterTimer((nint)handler);
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int FrameResizeView(void* self, void* view, Vst3Api.ViewRect* rect)
    {
        if (rect != null) Plugin(self)?.OnViewResize(rect->Right - rect->Left, rect->Bottom - rect->Top, view);
        return Vst3Api.ResultOk;
    }

    // ---------------- IEventList ----------------

    public static void* BuildEventList(nint gc)
    {
        lock (Lock)
            if (_eventListVtbl == null) _eventListVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&EventListQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&AddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&Release,
                (nint)(delegate* unmanaged[Cdecl]<void*, int>)&EventListCount,
                (nint)(delegate* unmanaged[Cdecl]<void*, int, Vst3Api.Event*, int>)&EventListGet,
                (nint)(delegate* unmanaged[Cdecl]<void*, Vst3Api.Event*, int>)&EventListAdd,
            });
        return MakeObj(_eventListVtbl, gc);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int EventListCount(void* self) => Plugin(self)?.InEventCount ?? 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int EventListGet(void* self, int index, Vst3Api.Event* e)
    {
        var p = Plugin(self);
        if (p == null || e == null || index < 0 || index >= p.InEventCount) return Vst3Api.ResultFalse;
        *e = p.InEvents[index];
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int EventListAdd(void* self, Vst3Api.Event* e) => Vst3Api.ResultOk; // output events ignored

    // ---------------- IParameterChanges + IParamValueQueue ----------------

    public static void* BuildParamChanges(nint gc)
    {
        lock (Lock)
            if (_paramChangesVtbl == null) _paramChangesVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&ParamChangesQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&AddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&Release,
                (nint)(delegate* unmanaged[Cdecl]<void*, int>)&ParamChangesCount,
                (nint)(delegate* unmanaged[Cdecl]<void*, int, void*>)&ParamChangesGetData,
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, int*, void*>)&ParamChangesAddData,
            });
        return MakeObj(_paramChangesVtbl, gc);
    }

    public static void* BuildParamQueue(nint gc, int index)
    {
        lock (Lock)
            if (_paramQueueVtbl == null) _paramQueueVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&ParamQueueQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&AddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&Release,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&QueueGetParamId,
                (nint)(delegate* unmanaged[Cdecl]<void*, int>)&QueueGetPointCount,
                (nint)(delegate* unmanaged[Cdecl]<void*, int, int*, double*, int>)&QueueGetPoint,
                (nint)(delegate* unmanaged[Cdecl]<void*, int, double, int*, int>)&QueueAddPoint,
            });
        return MakeObj(_paramQueueVtbl, gc, index);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ParamChangesCount(void* self) => Plugin(self)?.ParamChangeCount ?? 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void* ParamChangesGetData(void* self, int index)
    {
        var p = Plugin(self);
        if (p == null || index < 0 || index >= p.ParamChangeCount) return null;
        return (void*)p.QueueObjAt(index);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void* ParamChangesAddData(void* self, byte* id, int* index) => null; // host doesn't read output param changes

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint QueueGetParamId(void* self)
    {
        var p = Plugin(self);
        return p != null ? p.ParamChangeIdAt(Index(self)) : 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int QueueGetPointCount(void* self) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int QueueGetPoint(void* self, int index, int* sampleOffset, double* value)
    {
        var p = Plugin(self);
        if (p == null || index != 0) return Vst3Api.ResultFalse;
        if (sampleOffset != null) *sampleOffset = 0;
        if (value != null) *value = p.ParamChangeValueAt(Index(self));
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int QueueAddPoint(void* self, int sampleOffset, double value, int* index) => Vst3Api.ResultOk;

    // ---------------- IBStream (memory) ----------------

    public static void* BuildStream(nint gc)
    {
        lock (Lock)
            if (_streamVtbl == null) _streamVtbl = MakeVtbl(stackalloc nint[]
            {
                (nint)(delegate* unmanaged[Cdecl]<void*, byte*, void**, int>)&StreamQuery,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&AddRef,
                (nint)(delegate* unmanaged[Cdecl]<void*, uint>)&Release,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, int, int*, int>)&StreamRead,
                (nint)(delegate* unmanaged[Cdecl]<void*, void*, int, int*, int>)&StreamWrite,
                (nint)(delegate* unmanaged[Cdecl]<void*, long, int, long*, int>)&StreamSeek,
                (nint)(delegate* unmanaged[Cdecl]<void*, long*, int>)&StreamTell,
            });
        return MakeObj(_streamVtbl, gc);
    }

    private static Vst3MemoryStream? Stream(void* self)
    {
        try { return GCHandle.FromIntPtr(((HostObj*)self)->Gc).Target as Vst3MemoryStream; }
        catch { return null; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StreamRead(void* self, void* buffer, int numBytes, int* numRead)
    {
        var s = Stream(self);
        if (s == null || buffer == null || numBytes < 0) { if (numRead != null) *numRead = 0; return Vst3Api.ResultFalse; }
        var n = s.Read(new Span<byte>(buffer, numBytes));
        if (numRead != null) *numRead = n;
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StreamWrite(void* self, void* buffer, int numBytes, int* numWritten)
    {
        var s = Stream(self);
        if (s == null || buffer == null || numBytes < 0) { if (numWritten != null) *numWritten = 0; return Vst3Api.ResultFalse; }
        s.Write(new ReadOnlySpan<byte>(buffer, numBytes));
        if (numWritten != null) *numWritten = numBytes;
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StreamSeek(void* self, long pos, int mode, long* result)
    {
        var s = Stream(self);
        if (s == null) return Vst3Api.ResultFalse;
        var origin = mode switch { 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => SeekOrigin.Begin };
        var p = s.Seek(pos, origin);
        if (result != null) *result = p;
        return Vst3Api.ResultOk;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StreamTell(void* self, long* pos)
    {
        var s = Stream(self);
        if (s == null) return Vst3Api.ResultFalse;
        if (pos != null) *pos = s.Position;
        return Vst3Api.ResultOk;
    }

    // ---------------- per-interface queryInterface thunks ----------------
    // Each must answer ONLY for its own IID (+ FUnknown). Returning self for an unrecognised IID hands the
    // plugin our object as the wrong interface, so it then calls a vtable slot that maps to one of our
    // methods with mismatched argument types — corrupting pointers and crashing (AccessViolation).

    private static int QuerySelfIf(void* self, byte* iid, void** obj, byte[] ownIid)
    {
        if (Vst3Api.IidEquals(iid, ownIid) || Vst3Api.IidEquals(iid, Vst3Api.IidFUnknown))
        {
            *obj = self; return Vst3Api.ResultOk;
        }

        *obj = null; return Vst3Api.NoInterface;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StreamQuery(void* self, byte* iid, void** obj) => QuerySelfIf(self, iid, obj, Vst3Api.IidBStream);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int EventListQuery(void* self, byte* iid, void** obj) => QuerySelfIf(self, iid, obj, Vst3Api.IidEventList);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ParamChangesQuery(void* self, byte* iid, void** obj) => QuerySelfIf(self, iid, obj, Vst3Api.IidParameterChanges);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ParamQueueQuery(void* self, byte* iid, void** obj) => QuerySelfIf(self, iid, obj, Vst3Api.IidParamValueQueue);

    // ---------------- shared FUnknown thunks ----------------

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint AddRef(void* self) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint Release(void* self) => 1;
}

/// <summary>A growable in-memory <c>IBStream</c> backing used to copy component state into the controller.</summary>
public sealed class Vst3MemoryStream
{
    private readonly MemoryStream _ms = new();

    public long Position => _ms.Position;
    public int Read(Span<byte> dst) => _ms.Read(dst);
    public void Write(ReadOnlySpan<byte> src) => _ms.Write(src);
    public long Seek(long offset, SeekOrigin origin) => _ms.Seek(offset, origin);
}
