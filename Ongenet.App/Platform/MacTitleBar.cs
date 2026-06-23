using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace Ongenet.App.Platform;

/// <summary>
/// macOS-only AppKit interop that keeps the native window controls (the red/yellow/green "traffic
/// lights") visible while the window is in native fullscreen.
///
/// In native fullscreen macOS parks the title bar — and the buttons with it — in an auto-hiding
/// overlay that only drops down when the pointer touches the top edge. The buttons are never hidden
/// (they stay in the <c>NSTitlebarContainerView</c>), it's that container which slides away. Neither
/// a title-bar accessory (<c>fullScreenMinHeight</c>) nor the fullscreen presentation options keep it
/// pinned under Avalonia's window setup, and a toolbar pins it but pushes the client area down,
/// hiding the app's own title strip.
///
/// So instead we move the three standard window buttons out of the auto-hiding title bar and into the
/// window's persistent content view, pinned to the top-left — exactly how JetBrains' IDEs keep their
/// traffic lights "in the window" in fullscreen. On leaving fullscreen we hand them back to the title
/// bar, where AppKit manages and repositions them again.
///
/// We only send well-known selectors to standard AppKit objects, and never on a non-macOS platform.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacTitleBar
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend_Long_Ret(IntPtr receiver, IntPtr selector, long arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_Double(IntPtr receiver, IntPtr selector, double arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_ULong(IntPtr receiver, IntPtr selector, ulong arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool MsgSend_Bool(IntPtr receiver, IntPtr selector);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, W, H; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X, Y; }

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern CGRect MsgSend_CGRect(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_Point(IntPtr receiver, IntPtr selector, CGPoint origin);

    // Standard traffic-light geometry inside the window's top-left (matches macOS defaults).
    private const double ButtonLeft = 13.0; // x of the first (close) button
    private const double ButtonGap = 20.0;  // x spacing between buttons
    private const double ButtonTop = 6.0;   // distance from the window's top edge (matches the native
                                            // windowed offset, so the lights line up with the app's title strip)

    // NSView autoresizing-mask bits — keep the pinned buttons glued to the top-left on any resize.
    private const ulong NSViewMaxXMargin = 1UL << 2; // flexible right margin  -> pinned left
    private const ulong NSViewMinYMargin = 1UL << 3; // flexible bottom margin -> pinned top (unflipped view)
    private const ulong NSViewMaxYMargin = 1UL << 5; // flexible top margin    -> pinned top (flipped view)

    private const long NSWindowStyleMaskFullScreen = 1L << 14;

    // The title-bar superview the buttons came from, per window, so we can hand them back on exit.
    private static readonly Dictionary<IntPtr, IntPtr> _originalButtonSuper = new();

    /// <summary>
    /// Moves the traffic lights into the content view (pinned top-left) when entering fullscreen, and
    /// back to the title bar when leaving. Safe on any platform; a no-op off macOS or before the
    /// native handle exists.
    /// </summary>
    public static void SetFullScreen(Window window, bool fullScreen)
    {
        var nsWindow = NsWindow(window);
        if (nsWindow == IntPtr.Zero) return;
        if (fullScreen) PinButtonsToContent(nsWindow);
        else RestoreButtonsToTitleBar(nsWindow);
    }

    /// <summary>
    /// Re-asserts the pinned buttons once the fullscreen-enter animation settles — AppKit fades them
    /// to alpha 0 and re-homes them into the (auto-hiding) title bar across the transition.
    /// </summary>
    public static void RefreshButtons(Window window)
    {
        var nsWindow = NsWindow(window);
        if (nsWindow == IntPtr.Zero) return;
        if (((long)MsgSend(nsWindow, Sel("styleMask")) & NSWindowStyleMaskFullScreen) != 0)
            PinButtonsToContent(nsWindow);
    }

    private static IntPtr NsWindow(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return IntPtr.Zero;
        // On Avalonia's macOS backend the top-level platform handle IS the NSWindow (its `AvnWindow`
        // subclass), not the content view — so we use it directly.
        return window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    }

    private static void PinButtonsToContent(IntPtr nsWindow)
    {
        var content = MsgSend(nsWindow, Sel("contentView"));
        if (content == IntPtr.Zero) return;

        var flipped = MsgSend_Bool(content, Sel("isFlipped"));
        var contentH = MsgSend_CGRect(content, Sel("frame")).H;
        var mask = NSViewMaxXMargin | (flipped ? NSViewMaxYMargin : NSViewMinYMargin);

        for (long i = 0; i <= 2; i++)
        {
            var btn = MsgSend_Long_Ret(nsWindow, Sel("standardWindowButton:"), i);
            if (btn == IntPtr.Zero) continue;

            var super = MsgSend(btn, Sel("superview"));
            if (super != content) // remember the title-bar parent once, then reparent into the content view
            {
                if (!_originalButtonSuper.ContainsKey(nsWindow) && super != IntPtr.Zero)
                    _originalButtonSuper[nsWindow] = super;
                MsgSend_IntPtr(content, Sel("addSubview:"), btn);
            }

            var h = MsgSend_CGRect(btn, Sel("frame")).H;
            var y = flipped ? ButtonTop : contentH - ButtonTop - h;
            MsgSend_Point(btn, Sel("setFrameOrigin:"), new CGPoint { X = ButtonLeft + i * ButtonGap, Y = y });
            MsgSend_ULong(btn, Sel("setAutoresizingMask:"), mask);
            MsgSend_Double(btn, Sel("setAlphaValue:"), 1.0);
        }
    }

    private static void RestoreButtonsToTitleBar(IntPtr nsWindow)
    {
        if (!_originalButtonSuper.TryGetValue(nsWindow, out var titleBar) || titleBar == IntPtr.Zero)
            return;

        for (long i = 0; i <= 2; i++)
        {
            var btn = MsgSend_Long_Ret(nsWindow, Sel("standardWindowButton:"), i);
            if (btn == IntPtr.Zero) continue;
            MsgSend_ULong(btn, Sel("setAutoresizingMask:"), 0); // let AppKit position it in the title bar again
            MsgSend_IntPtr(titleBar, Sel("addSubview:"), btn);
        }
    }
}
