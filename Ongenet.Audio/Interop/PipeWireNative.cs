using System;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// P/Invoke surface over libpipewire-0.3 for raw audio via <c>pw_stream</c>. Constants and struct
/// offsets here were extracted from the real headers (pipewire-devel 1.4) with a probe program, not
/// guessed. No custom resolver (versioned soname, Linux-only). The stream runs on a
/// <c>pw_thread_loop</c>; its process callback fires on that loop's thread.
/// </summary>
internal static class PipeWireNative
{
    private const string Lib = "libpipewire-0.3.so.0";

    // pw_direction / flags / id (from probe).
    public const int PW_DIRECTION_OUTPUT = 1;
    public const int PW_DIRECTION_INPUT = 0;
    public const uint PW_ID_ANY = 0xffffffff;
    public const int PW_STREAM_FLAG_AUTOCONNECT = 0x1;
    public const int PW_STREAM_FLAG_MAP_BUFFERS = 0x4;
    public const int PW_STREAM_FLAG_RT_PROCESS = 0x10;
    public const int PW_VERSION_STREAM_EVENTS = 2;

    // pw_stream_state.
    public const int PW_STREAM_STATE_ERROR = -1;
    public const int PW_STREAM_STATE_STREAMING = 3;

    // struct pw_stream_events layout (sizeof 96): version@0, state_changed@16, param_changed@40, process@64.
    public const int EVENTS_SIZE = 96;
    public const int EVENTS_OFF_VERSION = 0;
    public const int EVENTS_OFF_STATE_CHANGED = 16;
    public const int EVENTS_OFF_PROCESS = 64;

    // struct pw_buffer: buffer@0, requested@24.   spa_buffer: datas@16.
    // struct spa_data: maxsize@20, data@24, chunk@32.   spa_chunk: offset@0, size@4, stride@8.
    public const int PWBUF_OFF_BUFFER = 0;
    public const int PWBUF_OFF_REQUESTED = 24;
    public const int SPABUF_OFF_DATAS = 16;
    public const int SPADATA_OFF_MAXSIZE = 20;
    public const int SPADATA_OFF_DATA = 24;
    public const int SPADATA_OFF_CHUNK = 32;
    public const int SPADATA_SIZE = 40;
    public const int SPACHUNK_OFF_OFFSET = 0;
    public const int SPACHUNK_OFF_SIZE = 4;
    public const int SPACHUNK_OFF_STRIDE = 8;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pw_init(IntPtr argc, IntPtr argv);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr pw_thread_loop_new(string name, IntPtr props);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pw_thread_loop_destroy(IntPtr loop);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pw_thread_loop_start(IntPtr loop);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pw_thread_loop_stop(IntPtr loop);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pw_thread_loop_get_loop(IntPtr loop);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pw_thread_loop_lock(IntPtr loop);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pw_thread_loop_unlock(IntPtr loop);

    // pw_properties_new is variadic-NULL-terminated; passing a single NULL yields an empty set.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pw_properties_new(IntPtr nullSentinel);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int pw_properties_set(IntPtr props, string key, string value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr pw_stream_new_simple(IntPtr loop, string name, IntPtr props, IntPtr events, IntPtr data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pw_stream_destroy(IntPtr stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pw_stream_connect(IntPtr stream, int direction, uint targetId, int flags, IntPtr paramsArray, uint nParams);

    // --- registry enumeration (context → core → registry + object listener) ---------------------
    // Versions/offsets from the probe. The core's get_registry is a vtable method (no exported wrapper):
    // an object pointer's first member is a spa_interface { ... cb { funcs@16, data@24 } }, and
    // pw_core_methods.get_registry sits at offset 48.
    public const uint PW_VERSION_REGISTRY = 3;
    public const int PW_VERSION_REGISTRY_EVENTS = 0;
    public const int IFACE_OFF_FUNCS = 16;
    public const int IFACE_OFF_DATA = 24;
    public const int COREMETHODS_OFF_GET_REGISTRY = 48;
    public const int SPA_HOOK_SIZE = 48;
    public const int REGEVENTS_SIZE = 24;
    public const int REGEVENTS_OFF_GLOBAL = 8;
    public const int DICT_OFF_NITEMS = 4;
    public const int DICT_OFF_ITEMS = 8;
    public const int DICT_ITEM_SIZE = 16; // { const char* key@0; const char* value@8; }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pw_context_new(IntPtr loop, IntPtr props, nuint userDataSize);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pw_context_connect(IntPtr context, IntPtr props, nuint userDataSize);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pw_context_destroy(IntPtr context);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pw_core_disconnect(IntPtr core);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pw_proxy_add_object_listener(IntPtr proxy, IntPtr hook, IntPtr funcs, IntPtr data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pw_proxy_destroy(IntPtr proxy);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint pw_stream_get_node_id(IntPtr stream);

    // For manual port linking (per-app capture): pw_core_methods.create_object is a vtable method @56.
    public const uint PW_VERSION_LINK = 3;
    public const int COREMETHODS_OFF_CREATE_OBJECT = 56;

    /// <summary>
    /// Calls the core's <c>create_object</c> vtable method to spawn a link via the "link-factory".
    /// <paramref name="propsDict"/> is a <c>pw_properties*</c> (its first member is the spa_dict the
    /// method expects). Returns the link proxy (keep it alive to keep the link).
    /// </summary>
    public static unsafe IntPtr CoreCreateLink(IntPtr core, IntPtr propsDict)
    {
        var funcs = Marshal.ReadIntPtr(core, IFACE_OFF_FUNCS);
        var data = Marshal.ReadIntPtr(core, IFACE_OFF_DATA);
        if (funcs == IntPtr.Zero) return IntPtr.Zero;
        var create = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, uint, IntPtr, nuint, IntPtr>)
            Marshal.ReadIntPtr(funcs, COREMETHODS_OFF_CREATE_OBJECT);
        var factory = Marshal.StringToHGlobalAnsi("link-factory");
        var type = Marshal.StringToHGlobalAnsi("PipeWire:Interface:Link");
        try { return create(data, factory, type, PW_VERSION_LINK, propsDict, 0); }
        finally { Marshal.FreeHGlobal(factory); Marshal.FreeHGlobal(type); }
    }

    /// <summary>Calls the core's <c>get_registry</c> vtable method (no exported wrapper exists).</summary>
    public static unsafe IntPtr CoreGetRegistry(IntPtr core)
    {
        var funcs = Marshal.ReadIntPtr(core, IFACE_OFF_FUNCS);
        var data = Marshal.ReadIntPtr(core, IFACE_OFF_DATA);
        if (funcs == IntPtr.Zero) return IntPtr.Zero;
        var getReg = (delegate* unmanaged[Cdecl]<IntPtr, uint, nuint, IntPtr>)Marshal.ReadIntPtr(funcs, COREMETHODS_OFF_GET_REGISTRY);
        return getReg(data, PW_VERSION_REGISTRY, 0);
    }

    /// <summary>Looks up a key in a <c>spa_dict*</c> by walking its items (no exported helper needed).</summary>
    public static string? DictLookup(IntPtr dict, string key)
    {
        if (dict == IntPtr.Zero) return null;
        var n = Marshal.ReadInt32(dict, DICT_OFF_NITEMS);
        var items = Marshal.ReadIntPtr(dict, DICT_OFF_ITEMS);
        if (items == IntPtr.Zero) return null;
        for (var i = 0; i < n; i++)
        {
            var k = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(items, i * DICT_ITEM_SIZE));
            if (k == key) return Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(items, i * DICT_ITEM_SIZE + 8));
        }

        return null;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pw_stream_dequeue_buffer(IntPtr stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pw_stream_queue_buffer(IntPtr stream, IntPtr buffer);

    private static bool _inited;
    private static readonly object InitLock = new();

    /// <summary>Initialises libpipewire once per process (refcounted by the library).</summary>
    public static void EnsureInit()
    {
        lock (InitLock)
        {
            if (_inited) return;
            pw_init(IntPtr.Zero, IntPtr.Zero);
            _inited = true;
        }
    }

    public static bool TryProbe()
    {
        try { return NativeLibrary.TryLoad(Lib, out _); }
        catch { return false; }
    }
}
