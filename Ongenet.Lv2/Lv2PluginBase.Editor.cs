using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ongenet.Core.Platform;
using Ongenet.Lv2.Interop;

namespace Ongenet.Lv2;

/// <summary>
/// Native LV2 UI hosting (the <see cref="IPluginEditor"/> half of <see cref="Lv2PluginBase"/>).
/// v1 hosts <c>ui:X11UI</c> on Linux: it loads the UI binary's <c>lv2ui_descriptor</c>, instantiates
/// the UI embedded into the host window via the <c>ui:parent</c> feature, and pumps the UI's
/// <c>idleInterface</c> on the editor timer. The UI gets <c>instance-access</c> + <c>data-access</c>
/// so tightly-coupled UIs (e.g. Cardinal/VCV Rack) can talk to the running DSP directly. The UI's
/// <c>write_function</c> writes flow back into the managed control values so host params stay in sync.
/// </summary>
public abstract unsafe partial class Lv2PluginBase
{
    // UI features this host can provide; a UI requiring anything else is reported as not hostable.
    private static readonly HashSet<string> UiSupported = new(StringComparer.Ordinal)
    {
        Lv2Api.FeatureUridMap, Lv2Api.FeatureUridUnmap, Lv2Api.FeatureOptions,
        Lv2Api.FeatureUiParent, Lv2Api.UiIdleInterface, Lv2Api.FeatureUiResize,
        Lv2Api.FeatureInstanceAccess, Lv2Api.FeatureDataAccess,
    };

    private nint _uiLib;
    private Lv2Api.LV2UI_Descriptor* _uiDesc;
    private void* _uiHandle;
    private Lv2Api.LV2UI_Idle_Interface* _uiIdle;
    private Lv2Api.LV2UI_Show_Interface* _uiShow;
    private readonly List<nint> _uiAllocs = new();
    private bool _editorOpen;
    private int _editorW;
    private int _editorH;

    // Intermediate GL-compatible X11 child we embed the plugin UI into (see X11Embed).
    private nint _x11Display;
    private nint _embedWindow;
    private const int DefaultUiWidth = 1000;
    private const int DefaultUiHeight = 700;

    public bool HasEditor =>
        OperatingSystem.IsLinux()
        && Descriptor.Ui is { IsX11: true } ui
        && SupportsUi(ui.RequiredFeatures);

    public bool IsEditorOpen => _editorOpen;
    public bool PrefersFloating => false; // we always embed via ui:parent
    public int EditorWidth => _editorW;
    public int EditorHeight => _editorH;

    private static bool SupportsUi(IEnumerable<string> required)
    {
        foreach (var f in required) if (!UiSupported.Contains(f)) return false;
        return true;
    }

    public void OpenEditor(nint windowHandle, string apiType, bool floating)
    {
        if (_editorOpen) return;
        if (!HasEditor) { Log?.Invoke($"LV2 '{Name}': no hostable UI."); return; }
        if (!string.Equals(apiType, "x11", StringComparison.OrdinalIgnoreCase))
        {
            Log?.Invoke($"LV2 '{Name}': UI needs an X11 window (got '{apiType}').");
            return;
        }

        if (!EnsureLoaded()) return;
        var ui = Descriptor.Ui!;

        try
        {
            LoadUiLibrary(ui);

            _editorW = DefaultUiWidth;
            _editorH = DefaultUiHeight;

            // Embed into an intermediate default-visual X11 child; a GL UI can't realize directly in
            // Avalonia's 32-bit ARGB window. Fall back to the raw window if the child can't be made.
            var parent = windowHandle;
            if (X11Embed.Create(windowHandle, DefaultUiWidth, DefaultUiHeight, out _x11Display, out _embedWindow))
                parent = _embedWindow;
            else
                Log?.Invoke($"LV2 '{Name}': X11 embed window failed; using host window directly.");

            var pluginUri = Lv2Api.Utf8(Descriptor.Uri);
            var bundlePath = Lv2Api.Utf8(EnsureTrailingSep(ui.BundlePath));
            var features = BuildUiFeatures(parent);
            void* widget = null;
            try
            {
                _uiHandle = _uiDesc->Instantiate(_uiDesc, pluginUri, bundlePath, &UiWriteCb,
                    (void*)GCHandle.ToIntPtr(_selfHandle), &widget, features);
            }
            finally
            {
                Lv2Api.FreeUtf8(pluginUri);
                Lv2Api.FreeUtf8(bundlePath);
            }

            if (_uiHandle == null) throw new InvalidOperationException("UI instantiate() returned null.");

            FetchUiInterfaces();
            if (_uiShow != null && _uiShow->Show != null) _uiShow->Show(_uiHandle);

            _editorOpen = true;
            Log?.Invoke($"LV2 '{Name}': UI opened ({_editorW}x{_editorH}).");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"LV2 '{Name}': UI open failed: {ex.Message}");
            CloseEditorInternal();
        }
    }

    public void SetEditorSize(int width, int height)
    {
        // The Avalonia host window resized — resize our embedded X11 child to fill it.
        if (_embedWindow != 0 && width > 0 && height > 0)
            X11Embed.Resize(_x11Display, _embedWindow, width, height);
    }

    // The plugin UI asked the host to become a given size; grow the embed child + report it.
    private void OnUiResize(int width, int height)
    {
        _editorW = width;
        _editorH = height;
        X11Embed.Resize(_x11Display, _embedWindow, width, height);
    }

    public void CloseEditor() => CloseEditorInternal();

    public void PumpEditor()
    {
        if (!_editorOpen || _uiIdle == null || _uiIdle->Idle == null) return;
        if (_uiIdle->Idle(_uiHandle) != 0) CloseEditorInternal(); // UI asked to close
    }

    private void LoadUiLibrary(Lv2UiInfo ui)
    {
        _uiLib = NativeLibrary.Load(ui.BinaryPath);
        if (!NativeLibrary.TryGetExport(_uiLib, Lv2Api.UiEntrySymbol, out var entryPtr) || entryPtr == 0)
            throw new InvalidOperationException("UI binary has no lv2ui_descriptor.");

        var entry = (delegate* unmanaged[Cdecl]<uint, Lv2Api.LV2UI_Descriptor*>)entryPtr;
        for (var i = 0u; ; i++)
        {
            var d = entry(i);
            if (d == null) break;
            if (Lv2Api.ReadUtf8(d->Uri) == ui.Uri) { _uiDesc = d; break; }
        }

        if (_uiDesc == null || _uiDesc->Instantiate == null)
            throw new InvalidOperationException("lv2ui_descriptor has no matching UI URI.");
    }

    private void FetchUiInterfaces()
    {
        if (_uiDesc->ExtensionData == null) return;

        var idle = Lv2Api.Utf8(Lv2Api.UiIdleInterface);
        var show = Lv2Api.Utf8(Lv2Api.UiShowInterface);
        try
        {
            _uiIdle = (Lv2Api.LV2UI_Idle_Interface*)_uiDesc->ExtensionData(idle);
            _uiShow = (Lv2Api.LV2UI_Show_Interface*)_uiDesc->ExtensionData(show);
        }
        finally { Lv2Api.FreeUtf8(idle); Lv2Api.FreeUtf8(show); }
    }

    // Builds the NULL-terminated UI feature array. All memory is tracked in _uiAllocs and freed on close.
    private Lv2Api.LV2_Feature** BuildUiFeatures(nint parentWindow)
    {
        // Options block (min/max/nominal block length + sample rate), as for the DSP instance.
        var rate = _format.SampleRate <= 0 ? 44100 : _format.SampleRate;
        var minCell = (int*)UiAlloc(sizeof(int)); *minCell = 1;
        var maxCell = (int*)UiAlloc(sizeof(int)); *maxCell = MaxBlock;
        var nomCell = (int*)UiAlloc(sizeof(int)); *nomCell = MaxBlock;
        var srCell = (float*)UiAlloc(sizeof(float)); *srCell = rate;
        var atomInt = Lv2Api.MapUrid(Lv2Api.AtomInt);
        var atomFloat = Lv2Api.MapUrid(Lv2Api.AtomFloat);

        var options = (Lv2Api.LV2_Options_Option*)UiAlloc(sizeof(Lv2Api.LV2_Options_Option) * 5);
        options[0] = Opt(Lv2Api.OptMinBlockLength, atomInt, sizeof(int), minCell);
        options[1] = Opt(Lv2Api.OptMaxBlockLength, atomInt, sizeof(int), maxCell);
        options[2] = Opt(Lv2Api.OptNominalBlockLength, atomInt, sizeof(int), nomCell);
        options[3] = Opt(Lv2Api.OptSampleRate, atomFloat, sizeof(float), srCell);
        options[4] = default;

        // data-access: exposes the plugin's extension_data to the UI.
        var ext = (Lv2Api.LV2_Extension_Data_Feature*)UiAlloc(sizeof(Lv2Api.LV2_Extension_Data_Feature));
        ext->DataAccess = _desc->ExtensionData;

        // ui:resize: the UI can ask the host to resize the container.
        var resize = (Lv2Api.LV2UI_Resize*)UiAlloc(sizeof(Lv2Api.LV2UI_Resize));
        resize->Handle = (void*)GCHandle.ToIntPtr(_selfHandle);
        resize->UiResize = &UiResizeCb;

        var entries = new (string Uri, nint Data)[]
        {
            (Lv2Api.FeatureUridMap, (nint)Lv2Api.UridMapData()),
            (Lv2Api.FeatureUridUnmap, (nint)Lv2Api.UridUnmapData()),
            (Lv2Api.FeatureOptions, (nint)options),
            (Lv2Api.FeatureUiParent, parentWindow),                       // X11 window id
            (Lv2Api.UiIdleInterface, 0),                                  // signals idle support
            (Lv2Api.FeatureInstanceAccess, (nint)_handle),               // the live plugin instance
            (Lv2Api.FeatureDataAccess, (nint)ext),
            (Lv2Api.FeatureUiResize, (nint)resize),
        };

        var arr = (Lv2Api.LV2_Feature**)UiAlloc((entries.Length + 1) * sizeof(void*));
        for (var i = 0; i < entries.Length; i++)
        {
            var f = (Lv2Api.LV2_Feature*)UiAlloc(sizeof(Lv2Api.LV2_Feature));
            f->Uri = UiAllocUtf8(entries[i].Uri);
            f->Data = (void*)entries[i].Data;
            arr[i] = f;
        }

        arr[entries.Length] = null;
        return arr;
    }

    private Lv2Api.LV2_Options_Option Opt(string keyUri, uint type, uint size, void* value) => new()
    {
        Context = Lv2Api.OptionsContextInstance,
        Subject = 0,
        Key = Lv2Api.MapUrid(keyUri),
        Size = size,
        Type = type,
        Value = value,
    };

    private void CloseEditorInternal()
    {
        try
        {
            if (_uiHandle != null && _uiDesc != null)
            {
                if (_editorOpen && _uiShow != null && _uiShow->Hide != null) _uiShow->Hide(_uiHandle);
                if (_uiDesc->Cleanup != null) _uiDesc->Cleanup(_uiHandle);
            }
        }
        catch { /* ignore */ }

        _uiHandle = null;
        _uiDesc = null;
        _uiIdle = null;
        _uiShow = null;
        _editorOpen = false;

        foreach (var p in _uiAllocs) Marshal.FreeHGlobal(p);
        _uiAllocs.Clear();

        if (_uiLib != 0) { NativeLibrary.Free(_uiLib); _uiLib = 0; }

        // Destroy the embed child only after the UI (which lives inside it) is gone.
        X11Embed.Destroy(ref _x11Display, ref _embedWindow);
    }

    // Called by the UI (its write_function) when a control changes; keep the managed value in sync.
    private void OnUiControlWrite(int portIndex, float value)
    {
        if (_controlIndexByPort.TryGetValue(portIndex, out var ci) && _controlValues != null)
            _controlValues[ci] = value;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void UiWriteCb(void* controller, uint portIndex, uint bufferSize, uint protocol, void* buffer)
    {
        // protocol 0 == plain float control port value.
        if (protocol != 0 || buffer == null || bufferSize < sizeof(float)) return;
        Recover(controller)?.OnUiControlWrite((int)portIndex, *(float*)buffer);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UiResizeCb(void* handle, int width, int height)
    {
        var inst = Recover(handle);
        if (inst == null) return 1;
        inst.OnUiResize(width, height);
        return 0;
    }

    private void* UiAlloc(int bytes)
    {
        var p = Marshal.AllocHGlobal(bytes);
        _uiAllocs.Add(p);
        return (void*)p;
    }

    private byte* UiAllocUtf8(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        var p = (byte*)UiAlloc(bytes.Length + 1);
        for (var i = 0; i < bytes.Length; i++) p[i] = bytes[i];
        p[bytes.Length] = 0;
        return p;
    }
}
