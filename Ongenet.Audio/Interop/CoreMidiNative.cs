using System;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// P/Invoke surface over Apple's CoreMIDI framework plus the small slice of CoreFoundation needed to
/// marshal CFStrings and pump a run loop. Only touched on macOS (the factory guards by OS). Uses the
/// classic <c>MIDIReadProc</c> (a plain C function pointer) rather than the block-based
/// <c>MIDIReceiveBlock</c>, which cannot be synthesized from P/Invoke.
/// </summary>
internal static class CoreMidiNative
{
    public const string CoreMidi = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";
    public const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const uint kCFStringEncodingUTF8 = 0x08000100;

    // void MIDIReadProc(const MIDIPacketList *pktlist, void *readProcRefCon, void *srcConnRefCon)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MIDIReadProc(IntPtr pktlist, IntPtr readProcRefCon, IntPtr srcConnRefCon);

    // --- CoreMIDI ------------------------------------------------------------------------
    // MIDIClientRef/MIDIPortRef/MIDIEndpointRef/MIDIObjectRef are all UInt32. OSStatus is Int32.

    [DllImport(CoreMidi)]
    public static extern int MIDIClientCreate(IntPtr name, IntPtr notifyProc, IntPtr notifyRefCon, out uint outClient);

    [DllImport(CoreMidi)]
    public static extern int MIDIClientDispose(uint client);

    [DllImport(CoreMidi)]
    public static extern int MIDIInputPortCreate(uint client, IntPtr portName, MIDIReadProc readProc,
        IntPtr refCon, out uint outPort);

    [DllImport(CoreMidi)]
    public static extern int MIDIPortDispose(uint port);

    [DllImport(CoreMidi)]
    public static extern nuint MIDIGetNumberOfSources();

    [DllImport(CoreMidi)]
    public static extern uint MIDIGetSource(nuint sourceIndex0);

    [DllImport(CoreMidi)]
    public static extern int MIDIPortConnectSource(uint port, uint source, IntPtr connRefCon);

    [DllImport(CoreMidi)]
    public static extern int MIDIPortDisconnectSource(uint port, uint source);

    [DllImport(CoreMidi)]
    public static extern int MIDIObjectGetStringProperty(uint obj, IntPtr propertyID, out IntPtr str);

    // --- CoreFoundation ------------------------------------------------------------------

    [DllImport(CoreFoundation)]
    public static extern IntPtr CFStringCreateWithCString(IntPtr alloc, byte[] cStr, uint encoding);

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, nint bufferSize, uint encoding);

    [DllImport(CoreFoundation)]
    public static extern nint CFStringGetLength(IntPtr theString);

    // SInt32 CFRunLoopRunInMode(CFStringRef mode, CFTimeInterval seconds, Boolean returnAfterSourceHandled)
    [DllImport(CoreFoundation)]
    public static extern int CFRunLoopRunInMode(IntPtr mode, double seconds,
        [MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);

    // --- Exported data symbols (CFStringRef constants) -----------------------------------
    // kMIDIPropertyDisplayName and kCFRunLoopDefaultMode are variables, not functions, so they can't be
    // [DllImport]ed; read the pointer value at the exported address instead.

    private static IntPtr _displayNameProp;
    private static IntPtr _defaultRunLoopMode;

    public static IntPtr DisplayNameProperty()
        => _displayNameProp != IntPtr.Zero
            ? _displayNameProp
            : _displayNameProp = ReadExportedPtr(CoreMidi, "kMIDIPropertyDisplayName");

    public static IntPtr DefaultRunLoopMode()
        => _defaultRunLoopMode != IntPtr.Zero
            ? _defaultRunLoopMode
            : _defaultRunLoopMode = ReadExportedPtr(CoreFoundation, "kCFRunLoopDefaultMode");

    private static IntPtr ReadExportedPtr(string libraryPath, string symbol)
    {
        if (!NativeLibrary.TryLoad(libraryPath, out var handle)) return IntPtr.Zero;
        return NativeLibrary.TryGetExport(handle, symbol, out var addr) ? Marshal.ReadIntPtr(addr) : IntPtr.Zero;
    }
}
