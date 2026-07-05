using System;
using System.Runtime.InteropServices;
using Ongenet.Au.Interop;

namespace Ongenet.Au;

/// <summary>
/// The Cocoa GUI half of <see cref="AuPluginBase"/>. Audio Units expose their editor as an
/// <c>NSView</c> via <c>kAudioUnitProperty_CocoaUI</c> (an <c>AUCocoaUIBase</c> factory in a bundle);
/// we load the bundle, build the factory, ask it for the view, and embed it into the Avalonia host
/// window's content view. Units without a Cocoa view report <see cref="HasEditor"/> = false, so the
/// app falls back to the generic parameter inspector. macOS only.
/// </summary>
public abstract unsafe partial class AuPluginBase
{
    private const int EmbedDefaultW = 600;
    private const int EmbedDefaultH = 400;

    private bool _hasCocoaView;
    private bool _editorOpen;
    private int _editorW;
    private int _editorH;
    private IntPtr _pluginView;
    private IntPtr _factory;

    public bool HasEditor { get { EnsureLoaded(); return _hasCocoaView; } }
    public bool IsEditorOpen => _editorOpen;
    public bool PrefersFloating => false; // AU Cocoa views embed into the host window
    public int EditorWidth => _editorW;
    public int EditorHeight => _editorH;

    // Detects whether the unit advertises a Cocoa view (called during load).
    private void DetectEditor()
    {
        _hasCocoaView = false;
        if (!OperatingSystem.IsMacOS() || _unit == IntPtr.Zero) return;

        try
        {
            if (AudioUnitApi.AudioUnitGetPropertyInfo(_unit, AudioUnitApi.kAudioUnitProperty_CocoaUI,
                    AudioUnitApi.kAudioUnitScope_Global, 0, out var size, out _) == 0
                && size >= (uint)sizeof(AudioUnitApi.AudioUnitCocoaViewInfo))
                _hasCocoaView = true;
        }
        catch { /* ignore */ }
    }

    public void OpenEditor(nint windowHandle, string apiType, bool floating)
    {
        if (_editorOpen) return;
        if (!EnsureLoaded() || !_hasCocoaView) { Log?.Invoke($"AU '{Name}': no Cocoa view."); return; }

        try
        {
            var parent = ResolveParentView(windowHandle);
            if (parent == IntPtr.Zero) { Log?.Invoke($"AU '{Name}': no parent NSView."); return; }

            if (_pluginView == IntPtr.Zero && !CreateView()) return;

            AudioUnitApi.MsgSend_Ptr(parent, AudioUnitApi.Sel("addSubview:"), _pluginView);
            _editorOpen = true;
            Log?.Invoke($"AU '{Name}': Cocoa view shown size={_editorW}x{_editorH}.");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"AU '{Name}': open editor failed: {ex.Message}");
        }
    }

    private bool CreateView()
    {
        if (AudioUnitApi.AudioUnitGetPropertyInfo(_unit, AudioUnitApi.kAudioUnitProperty_CocoaUI,
                AudioUnitApi.kAudioUnitScope_Global, 0, out var size, out _) != 0
            || size < (uint)sizeof(AudioUnitApi.AudioUnitCocoaViewInfo))
            return false;

        var infoPtr = (byte*)Marshal.AllocHGlobal((nint)size);
        try
        {
            var ioSize = size;
            if (AudioUnitApi.AudioUnitGetProperty(_unit, AudioUnitApi.kAudioUnitProperty_CocoaUI,
                    AudioUnitApi.kAudioUnitScope_Global, 0, infoPtr, ref ioSize) != 0)
                return false;

            var bundleUrl = *(IntPtr*)infoPtr;               // CFURLRef (toll-free NSURL)
            var classNameCf = *(IntPtr*)(infoPtr + sizeof(IntPtr)); // CFStringRef (toll-free NSString)
            if (bundleUrl == IntPtr.Zero || classNameCf == IntPtr.Zero) return false;

            try
            {
                var nsBundle = AudioUnitApi.GetClass("NSBundle");
                var bundle = AudioUnitApi.MsgSend_Ptr(nsBundle, AudioUnitApi.Sel("bundleWithURL:"), bundleUrl);
                if (bundle == IntPtr.Zero) return false;
                AudioUnitApi.MsgSend(bundle, AudioUnitApi.Sel("load"));

                var factoryClass = AudioUnitApi.MsgSend_Ptr(bundle, AudioUnitApi.Sel("classNamed:"), classNameCf);
                if (factoryClass == IntPtr.Zero) return false;

                _factory = AudioUnitApi.MsgSend(
                    AudioUnitApi.MsgSend(factoryClass, AudioUnitApi.Sel("alloc")), AudioUnitApi.Sel("init"));
                if (_factory == IntPtr.Zero) return false;

                var pref = new AudioUnitApi.CGSize { Width = 0, Height = 0 };
                _pluginView = AudioUnitApi.MsgSend_PtrSize(_factory,
                    AudioUnitApi.Sel("uiViewForAudioUnit:withSize:"), _unit, pref);
                if (_pluginView == IntPtr.Zero) return false;

                ReadViewSize();
                return true;
            }
            finally
            {
                AudioUnitApi.CFRelease(bundleUrl);
                AudioUnitApi.CFRelease(classNameCf);
            }
        }
        finally
        {
            Marshal.FreeHGlobal((nint)infoPtr);
        }
    }

    private void ReadViewSize()
    {
        _editorW = EmbedDefaultW;
        _editorH = EmbedDefaultH;
        try
        {
            var frame = AudioUnitApi.ViewFrame(_pluginView, AudioUnitApi.Sel("frame"));
            if (frame.W > 0) _editorW = (int)frame.W;
            if (frame.H > 0) _editorH = (int)frame.H;
        }
        catch { /* keep defaults */ }
    }

    // The Avalonia macOS top-level handle is the NSWindow; embed into its contentView. If we were
    // handed an NSView directly, use it as-is.
    private static IntPtr ResolveParentView(nint windowHandle)
    {
        var handle = (IntPtr)windowHandle;
        if (handle == IntPtr.Zero) return IntPtr.Zero;
        if (AudioUnitApi.MsgSend_Bool_Sel(handle, AudioUnitApi.Sel("respondsToSelector:"),
                AudioUnitApi.Sel("contentView")))
            return AudioUnitApi.MsgSend(handle, AudioUnitApi.Sel("contentView"));
        return handle;
    }

    public void SetEditorSize(int width, int height)
    {
        if (!_editorOpen || _pluginView == IntPtr.Zero || width <= 0 || height <= 0) return;
        try
        {
            var size = new AudioUnitApi.CGSize { Width = width, Height = height };
            AudioUnitApi.MsgSend_Size(_pluginView, AudioUnitApi.Sel("setFrameSize:"), size);
        }
        catch { /* ignore */ }
    }

    public void CloseEditor()
    {
        if (!_editorOpen) return;
        try
        {
            if (_pluginView != IntPtr.Zero)
                AudioUnitApi.MsgSend(_pluginView, AudioUnitApi.Sel("removeFromSuperview"));
        }
        catch { /* ignore */ }
        _editorOpen = false;
    }

    // AU Cocoa views run on the app's normal AppKit run loop; nothing to pump.
    public void PumpEditor() { }

    private void DestroyEditor()
    {
        try
        {
            if (_pluginView != IntPtr.Zero)
                AudioUnitApi.MsgSend(_pluginView, AudioUnitApi.Sel("removeFromSuperview"));
            if (_factory != IntPtr.Zero)
                AudioUnitApi.MsgSend(_factory, AudioUnitApi.Sel("release"));
        }
        catch { /* ignore */ }

        _pluginView = IntPtr.Zero;
        _factory = IntPtr.Zero;
        _editorOpen = false;
    }
}
