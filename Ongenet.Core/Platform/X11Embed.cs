using System;
using System.Runtime.InteropServices;

namespace Ongenet.Core.Platform;

/// <summary>
/// Creates a plain intermediate X11 child window (with the screen's default, GL-compatible 24-bit
/// visual) inside a host top-level window, to embed a foreign plugin UI into. Handing a GL-based
/// plugin UI the host window directly fails when that window uses a 32-bit ARGB visual (as Avalonia's
/// do): the UI can't create a matching GLX context and either renders black or aborts. Giving it a
/// default-visual child instead is the approach suil/Carla use. Shared by every native plugin host
/// (CLAP, LV2, and future VST). Linux/X11 only; callers guard with <see cref="OperatingSystem.IsLinux"/>.
/// </summary>
public static class X11Embed
{
    private const int InputOutput = 1;
    private const int AllocNone = 0;
    private const ulong CWBackPixel = 1UL << 1;
    private const ulong CWBorderPixel = 1UL << 3;
    private const ulong CWColormap = 1UL << 13;

    [StructLayout(LayoutKind.Sequential)]
    private struct XSetWindowAttributes
    {
        public nint background_pixmap;
        public nuint background_pixel;
        public nint border_pixmap;
        public nuint border_pixel;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public nuint backing_planes;
        public nuint backing_pixel;
        public int save_under;
        public nint event_mask;
        public nint do_not_propagate_mask;
        public int override_redirect;
        public nint colormap;
        public nint cursor;
    }

    private const string Lib = "libX11.so.6";
    [DllImport(Lib)] private static extern nint XOpenDisplay(nint name);
    [DllImport(Lib)] private static extern int XCloseDisplay(nint display);
    [DllImport(Lib)] private static extern int XDefaultScreen(nint display);
    [DllImport(Lib)] private static extern nint XDefaultVisual(nint display, int screen);
    [DllImport(Lib)] private static extern int XDefaultDepth(nint display, int screen);
    [DllImport(Lib)] private static extern nint XCreateColormap(nint display, nint w, nint visual, int alloc);
    [DllImport(Lib)] private static extern nint XCreateWindow(nint display, nint parent, int x, int y,
        uint width, uint height, uint borderWidth, int depth, uint @class, nint visual, nuint valueMask,
        ref XSetWindowAttributes attributes);
    [DllImport(Lib)] private static extern int XMapWindow(nint display, nint w);
    [DllImport(Lib)] private static extern int XResizeWindow(nint display, nint w, uint width, uint height);
    [DllImport(Lib)] private static extern int XDestroyWindow(nint display, nint w);
    [DllImport(Lib)] private static extern int XFlush(nint display);
    [DllImport(Lib)] private static extern int XSync(nint display, bool discard);

    /// <summary>Creates + maps a default-visual child of <paramref name="parent"/>. Returns false on failure.</summary>
    public static bool Create(nint parent, int width, int height, out nint display, out nint window)
    {
        display = 0;
        window = 0;
        if (parent == 0) return false;

        var d = XOpenDisplay(0);
        if (d == 0) return false;

        try
        {
            var screen = XDefaultScreen(d);
            var visual = XDefaultVisual(d, screen);
            var depth = XDefaultDepth(d, screen);
            var colormap = XCreateColormap(d, parent, visual, AllocNone);

            var attrs = new XSetWindowAttributes { background_pixel = 0, border_pixel = 0, colormap = colormap };
            var w = XCreateWindow(d, parent, 0, 0, (uint)Math.Max(1, width), (uint)Math.Max(1, height), 0,
                depth, InputOutput, visual, (nuint)(CWBackPixel | CWBorderPixel | CWColormap), ref attrs);
            if (w == 0) { XCloseDisplay(d); return false; }

            XMapWindow(d, w);
            XSync(d, false);
            display = d;
            window = w;
            return true;
        }
        catch
        {
            XCloseDisplay(d);
            return false;
        }
    }

    public static void Resize(nint display, nint window, int width, int height)
    {
        if (display == 0 || window == 0) return;
        XResizeWindow(display, window, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
        XFlush(display);
    }

    public static void Destroy(ref nint display, ref nint window)
    {
        if (display != 0)
        {
            if (window != 0) XDestroyWindow(display, window);
            XCloseDisplay(display);
        }

        display = 0;
        window = 0;
    }
}
