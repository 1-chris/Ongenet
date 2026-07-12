using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Optional ASIO host bindings. When <c>ENABLE_ASIO</c> is undefined or no ASIO driver is present,
/// all entry points fail gracefully so the app falls back to WASAPI.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AsioNative
{
    private const string LibraryName = "asio.dll";

    public static bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
#if ENABLE_ASIO
            try { return NativeLibrary.TryLoad(LibraryName, out _); }
            catch { return false; }
#else
            return false;
#endif
        }
    }

#if ENABLE_ASIO
    [DllImport(LibraryName, EntryPoint = "ASIOInit", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AsioInit(IntPtr driverHandle);

    public static bool TryInit(IntPtr driverHandle)
    {
        try { return AsioInit(driverHandle) == 0; }
        catch { return false; }
    }
#endif
}
