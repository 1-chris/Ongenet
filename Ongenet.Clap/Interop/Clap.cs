using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ongenet.Clap.Interop;

/// <summary>
/// Hand-transcribed CLAP C ABI (clap-audio.org), plus the host-side callbacks Ongenet provides.
/// Structs mirror the C layouts exactly (sequential, natural alignment). Function-pointer fields
/// we call are typed <c>delegate* unmanaged[Cdecl]</c>; ones we don't call are <c>nint</c>
/// placeholders (still 8 bytes, so offsets stay correct). Plugins are loaded by path via
/// <see cref="System.Runtime.InteropServices.NativeLibrary"/> and the <c>clap_entry</c> export.
/// </summary>
public static unsafe class ClapApi
{
    // --- Identifiers / constants ---
    public const string EntrySymbol = "clap_entry";
    public const string FactoryId = "clap.plugin-factory";
    public const string ExtParams = "clap.params";
    public const string ExtGui = "clap.gui";
    public const string ExtAudioPorts = "clap.audio-ports";
    public const string ExtNotePorts = "clap.note-ports";
    public const string ExtTimerSupport = "clap.timer-support";
    public const string ExtPosixFdSupport = "clap.posix-fd-support";
    public const string ExtLog = "clap.log";
    public const string ExtThreadCheck = "clap.thread-check";
    public const string FeatureInstrument = "instrument";
    public const string FeatureAudioEffect = "audio-effect";

    // POSIX fd flags (clap_posix_fd_flags).
    public const uint FdRead = 1 << 0;
    public const uint FdWrite = 1 << 1;
    public const uint FdError = 1 << 2;

    public const ushort CoreEventSpaceId = 0;
    public const ushort EventNoteOn = 0;
    public const ushort EventNoteOff = 1;
    public const ushort EventParamValue = 5;

    public const string WindowApiWin32 = "win32";
    public const string WindowApiCocoa = "cocoa";
    public const string WindowApiX11 = "x11";

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapVersion
    {
        public uint Major, Minor, Revision;
        public static ClapVersion Current => new() { Major = 1, Minor = 2, Revision = 2 };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapPluginEntry
    {
        public ClapVersion ClapVersion;
        public delegate* unmanaged[Cdecl]<byte*, byte> Init;       // bool(const char* path)
        public delegate* unmanaged[Cdecl]<void> Deinit;
        public delegate* unmanaged[Cdecl]<byte*, void*> GetFactory; // const void*(const char* factory_id)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapPluginFactory
    {
        public delegate* unmanaged[Cdecl]<ClapPluginFactory*, uint> GetPluginCount;
        public delegate* unmanaged[Cdecl]<ClapPluginFactory*, uint, ClapPluginDescriptor*> GetPluginDescriptor;
        public delegate* unmanaged[Cdecl]<ClapPluginFactory*, ClapHost*, byte*, ClapPlugin*> CreatePlugin;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapPluginDescriptor
    {
        public ClapVersion ClapVersion;
        public byte* Id;
        public byte* Name;
        public byte* Vendor;
        public byte* Url;
        public byte* ManualUrl;
        public byte* SupportUrl;
        public byte* Version;
        public byte* Description;
        public byte** Features; // null-terminated array of const char*
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapHost
    {
        public ClapVersion ClapVersion;
        public void* HostData;
        public byte* Name;
        public byte* Vendor;
        public byte* Url;
        public byte* Version;
        public delegate* unmanaged[Cdecl]<ClapHost*, byte*, void*> GetExtension;
        public delegate* unmanaged[Cdecl]<ClapHost*, void> RequestRestart;
        public delegate* unmanaged[Cdecl]<ClapHost*, void> RequestProcess;
        public delegate* unmanaged[Cdecl]<ClapHost*, void> RequestCallback;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapPlugin
    {
        public ClapPluginDescriptor* Desc;
        public void* PluginData;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte> Init;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, void> Destroy;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, double, uint, uint, byte> Activate;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, void> Deactivate;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte> StartProcessing;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, void> StopProcessing;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, void> Reset;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, ClapProcess*, int> Process;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte*, void*> GetExtension;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, void> OnMainThread;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapAudioBuffer
    {
        public float** Data32;
        public double** Data64;
        public uint ChannelCount;
        public uint Latency;
        public ulong ConstantMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapProcess
    {
        public long SteadyTime;
        public uint FramesCount;
        public void* Transport;
        public ClapAudioBuffer* AudioInputs;
        public ClapAudioBuffer* AudioOutputs;
        public uint AudioInputsCount;
        public uint AudioOutputsCount;
        public ClapInputEvents* InEvents;
        public ClapOutputEvents* OutEvents;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapInputEvents
    {
        public void* Ctx;
        public delegate* unmanaged[Cdecl]<ClapInputEvents*, uint> Size;
        public delegate* unmanaged[Cdecl]<ClapInputEvents*, uint, ClapEventHeader*> Get;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapOutputEvents
    {
        public void* Ctx;
        public delegate* unmanaged[Cdecl]<ClapOutputEvents*, ClapEventHeader*, byte> TryPush;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapEventHeader
    {
        public uint Size;
        public uint Time;
        public ushort SpaceId;
        public ushort Type;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapEventNote
    {
        public ClapEventHeader Header;
        public int NoteId;
        public short PortIndex;
        public short Channel;
        public short Key;
        public double Velocity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapEventParamValue
    {
        public ClapEventHeader Header;
        public uint ParamId;
        public void* Cookie;
        public int NoteId;
        public short PortIndex;
        public short Channel;
        public short Key;
        public double Value;
    }

    public const int NameSize = 256;
    public const int PathSize = 1024;

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapParamInfo
    {
        public uint Id;
        public uint Flags;
        public void* Cookie;
        public fixed byte Name[NameSize];
        public fixed byte Module[PathSize];
        public double MinValue;
        public double MaxValue;
        public double DefaultValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapPluginParams
    {
        public delegate* unmanaged[Cdecl]<ClapPlugin*, uint> Count;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, uint, ClapParamInfo*, byte> GetInfo;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, uint, double*, byte> GetValue;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, uint, double, byte*, uint, byte> ValueToText;
        public nint TextToValue;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, ClapInputEvents*, ClapOutputEvents*, void> Flush;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapAudioPortInfo
    {
        public uint Id;
        public fixed byte Name[NameSize];
        public uint Flags;
        public uint ChannelCount;
        public byte* PortType;
        public uint InPlacePair;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapPluginAudioPorts
    {
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte, uint> Count;            // (plugin, is_input)
        public delegate* unmanaged[Cdecl]<ClapPlugin*, uint, byte, ClapAudioPortInfo*, byte> Get; // (plugin, index, is_input, info)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapWindow
    {
        public byte* Api;
        public void* Handle; // win32 HWND / cocoa NSView / x11 window id (pointer-sized)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapPluginGui
    {
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte*, byte, byte> IsApiSupported;   // (plugin, api, is_floating)
        public nint GetPreferredApi;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte*, byte, byte> Create;           // (plugin, api, is_floating)
        public delegate* unmanaged[Cdecl]<ClapPlugin*, void> Destroy;
        public nint SetScale;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, uint*, uint*, byte> GetSize;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte> CanResize;
        public nint GetResizeHints;
        public nint AdjustSize;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, uint, uint, byte> SetSize;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, ClapWindow*, byte> SetParent;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, ClapWindow*, byte> SetTransient;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte*, void> SuggestTitle;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte> Show;
        public delegate* unmanaged[Cdecl]<ClapPlugin*, byte> Hide;
    }

    // Plugin-side extensions the host calls into.
    [StructLayout(LayoutKind.Sequential)]
    public struct ClapPluginTimerSupport
    {
        public delegate* unmanaged[Cdecl]<ClapPlugin*, uint, void> OnTimer; // (plugin, timer_id)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapPluginPosixFdSupport
    {
        public delegate* unmanaged[Cdecl]<ClapPlugin*, int, uint, void> OnFd; // (plugin, fd, flags)
    }

    // Host-side extensions we expose to plugins.
    [StructLayout(LayoutKind.Sequential)]
    public struct ClapHostTimerSupport
    {
        public delegate* unmanaged[Cdecl]<ClapHost*, uint, uint*, byte> RegisterTimer;   // (host, period_ms, *timer_id)
        public delegate* unmanaged[Cdecl]<ClapHost*, uint, byte> UnregisterTimer;        // (host, timer_id)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapHostPosixFdSupport
    {
        public delegate* unmanaged[Cdecl]<ClapHost*, int, uint, byte> RegisterFd; // (host, fd, flags)
        public delegate* unmanaged[Cdecl]<ClapHost*, int, uint, byte> ModifyFd;
        public delegate* unmanaged[Cdecl]<ClapHost*, int, byte> UnregisterFd;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapHostLog
    {
        public delegate* unmanaged[Cdecl]<ClapHost*, int, byte*, void> Log; // (host, severity, msg)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapHostThreadCheck
    {
        public delegate* unmanaged[Cdecl]<ClapHost*, byte> IsMainThread;
        public delegate* unmanaged[Cdecl]<ClapHost*, byte> IsAudioThread;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClapHostGui
    {
        public delegate* unmanaged[Cdecl]<ClapHost*, void> ResizeHintsChanged;
        public delegate* unmanaged[Cdecl]<ClapHost*, uint, uint, byte> RequestResize;
        public delegate* unmanaged[Cdecl]<ClapHost*, byte> RequestShow;
        public delegate* unmanaged[Cdecl]<ClapHost*, byte> RequestHide;
        public delegate* unmanaged[Cdecl]<ClapHost*, byte, void> Closed; // (host, was_destroyed)
    }

    /// <summary>The native context backing a <see cref="ClapInputEvents"/> list (uniform-stride buffer).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct InputEventsCtx
    {
        public int Count;
        public int Stride;
        public byte* Buffer;
    }

    // --- Host callbacks (provided to plugins) ---

    private static int _mainThreadId;
    private static readonly object _extLock = new();
    private static ClapHostTimerSupport* _extTimer;
    private static ClapHostPosixFdSupport* _extFd;
    private static ClapHostLog* _extLog;
    private static ClapHostThreadCheck* _extThread;
    private static ClapHostGui* _extGui;

    /// <summary>Recovers the managed instrument backing a host pointer (via host_data GCHandle).</summary>
    private static ClapInstrument? HostInstance(ClapHost* host)
    {
        if (host == null || host->HostData == null) return null;
        try { return GCHandle.FromIntPtr((nint)host->HostData).Target as ClapInstrument; }
        catch { return null; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void* HostGetExtension(ClapHost* host, byte* extId)
    {
        var id = ReadUtf8(extId);
        return id switch
        {
            ExtTimerSupport => EnsureTimerExt(),
            ExtPosixFdSupport => OperatingSystem.IsWindows() ? null : EnsureFdExt(),
            ExtLog => EnsureLogExt(),
            ExtThreadCheck => EnsureThreadExt(),
            ExtGui => EnsureGuiExt(),
            _ => null
        };
    }

    private static void* EnsureTimerExt()
    {
        lock (_extLock)
        {
            if (_extTimer == null)
            {
                _extTimer = (ClapHostTimerSupport*)Marshal.AllocHGlobal(sizeof(ClapHostTimerSupport));
                _extTimer->RegisterTimer = &HostRegisterTimer;
                _extTimer->UnregisterTimer = &HostUnregisterTimer;
            }

            return _extTimer;
        }
    }

    private static void* EnsureFdExt()
    {
        lock (_extLock)
        {
            if (_extFd == null)
            {
                _extFd = (ClapHostPosixFdSupport*)Marshal.AllocHGlobal(sizeof(ClapHostPosixFdSupport));
                _extFd->RegisterFd = &HostRegisterFd;
                _extFd->ModifyFd = &HostModifyFd;
                _extFd->UnregisterFd = &HostUnregisterFd;
            }

            return _extFd;
        }
    }

    private static void* EnsureLogExt()
    {
        lock (_extLock)
        {
            if (_extLog == null)
            {
                _extLog = (ClapHostLog*)Marshal.AllocHGlobal(sizeof(ClapHostLog));
                _extLog->Log = &HostLog;
            }

            return _extLog;
        }
    }

    private static void* EnsureThreadExt()
    {
        lock (_extLock)
        {
            if (_extThread == null)
            {
                _extThread = (ClapHostThreadCheck*)Marshal.AllocHGlobal(sizeof(ClapHostThreadCheck));
                _extThread->IsMainThread = &HostIsMainThread;
                _extThread->IsAudioThread = &HostIsAudioThread;
            }

            return _extThread;
        }
    }

    private static void* EnsureGuiExt()
    {
        lock (_extLock)
        {
            if (_extGui == null)
            {
                _extGui = (ClapHostGui*)Marshal.AllocHGlobal(sizeof(ClapHostGui));
                _extGui->ResizeHintsChanged = &HostResizeHintsChanged;
                _extGui->RequestResize = &HostRequestResize;
                _extGui->RequestShow = &HostRequestShow;
                _extGui->RequestHide = &HostRequestHide;
                _extGui->Closed = &HostGuiClosed;
            }

            return _extGui;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void HostRequestRestart(ClapHost* host) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void HostRequestProcess(ClapHost* host) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void HostRequestCallback(ClapHost* host) => HostInstance(host)?.RequestCallback();

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostRegisterTimer(ClapHost* host, uint periodMs, uint* timerId)
    {
        var inst = HostInstance(host);
        if (inst == null || timerId == null) return 0;
        *timerId = inst.RegisterTimer(periodMs);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostUnregisterTimer(ClapHost* host, uint timerId)
    {
        HostInstance(host)?.UnregisterTimer(timerId);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostRegisterFd(ClapHost* host, int fd, uint flags)
    {
        HostInstance(host)?.RegisterFd(fd, flags);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostModifyFd(ClapHost* host, int fd, uint flags)
    {
        HostInstance(host)?.ModifyFd(fd, flags);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostUnregisterFd(ClapHost* host, int fd)
    {
        HostInstance(host)?.UnregisterFd(fd);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void HostLog(ClapHost* host, int severity, byte* msg)
        => ClapInstrument.Log?.Invoke($"[plugin] {ReadUtf8(msg)}");

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostIsMainThread(ClapHost* host)
        => Environment.CurrentManagedThreadId == _mainThreadId ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostIsAudioThread(ClapHost* host) => 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void HostResizeHintsChanged(ClapHost* host) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostRequestResize(ClapHost* host, uint width, uint height) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostRequestShow(ClapHost* host) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte HostRequestHide(ClapHost* host) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void HostGuiClosed(ClapHost* host, byte wasDestroyed)
        => HostInstance(host)?.OnGuiClosed(wasDestroyed != 0);

    // --- Input/output event list callbacks ---

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint InEventsSize(ClapInputEvents* list)
    {
        var ctx = (InputEventsCtx*)list->Ctx;
        return ctx == null ? 0u : (uint)ctx->Count;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ClapEventHeader* InEventsGet(ClapInputEvents* list, uint index)
    {
        var ctx = (InputEventsCtx*)list->Ctx;
        if (ctx == null || index >= (uint)ctx->Count) return null;
        return (ClapEventHeader*)(ctx->Buffer + (long)index * ctx->Stride);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte OutEventsTryPush(ClapOutputEvents* list, ClapEventHeader* ev) => 1; // accept + ignore

    /// <summary>Fills <paramref name="list"/> with our event-list callbacks and the given context.</summary>
    public static void InitInputEvents(ClapInputEvents* list, InputEventsCtx* ctx)
    {
        list->Ctx = ctx;
        list->Size = &InEventsSize;
        list->Get = &InEventsGet;
    }

    public static void InitOutputEvents(ClapOutputEvents* list)
    {
        list->Ctx = null;
        list->TryPush = &OutEventsTryPush;
    }

    /// <summary>Allocates and fills a clap_host with Ongenet's callbacks. Caller frees with <see cref="FreeHost"/>.</summary>
    public static ClapHost* AllocHost(void* hostData)
    {
        if (_mainThreadId == 0) _mainThreadId = Environment.CurrentManagedThreadId;
        var host = (ClapHost*)Marshal.AllocHGlobal(sizeof(ClapHost));
        *host = default;
        host->ClapVersion = ClapVersion.Current;
        host->HostData = hostData;
        host->Name = _hostName;
        host->Vendor = _hostVendor;
        host->Url = _hostUrl;
        host->Version = _hostVersion;
        host->GetExtension = &HostGetExtension;
        host->RequestRestart = &HostRequestRestart;
        host->RequestProcess = &HostRequestProcess;
        host->RequestCallback = &HostRequestCallback;
        return host;
    }

    public static void FreeHost(ClapHost* host)
    {
        if (host != null) Marshal.FreeHGlobal((nint)host);
    }

    private static readonly byte* _hostName = Utf8Static("Ongenet");
    private static readonly byte* _hostVendor = Utf8Static("Ongenet");
    private static readonly byte* _hostUrl = Utf8Static("https://ongenet.app");
    private static readonly byte* _hostVersion = Utf8Static("1.0.0");

    // --- String helpers ---

    private static byte* Utf8Static(string s) => (byte*)Marshal.StringToCoTaskMemUTF8(s);

    /// <summary>Allocates a NUL-terminated UTF-8 copy (free with <see cref="FreeUtf8"/>).</summary>
    public static byte* Utf8(string s) => (byte*)Marshal.StringToCoTaskMemUTF8(s);

    public static void FreeUtf8(byte* p)
    {
        if (p != null) Marshal.FreeCoTaskMem((nint)p);
    }

    public static string? ReadUtf8(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((nint)p);

    public static string ReadFixedUtf8(byte* p, int max)
    {
        if (p == null) return string.Empty;
        var len = 0;
        while (len < max && p[len] != 0) len++;
        return System.Text.Encoding.UTF8.GetString(p, len);
    }

    /// <summary>Whether a plugin/entry clap_version is ABI-compatible with this host (major must match, &gt;= 1).</summary>
    public static bool IsCompatible(ClapVersion v) => v.Major == 1;
}
