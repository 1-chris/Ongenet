using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Ongenet.Vst.Vst2.Interop;

namespace Ongenet.Vst.Vst2;

/// <summary>
/// A loaded VST2 module (a plugin shared library): owns the native handle and resolves the entry point.
/// <see cref="Open"/> calls the entry with Ongenet's audioMaster callback and returns the plugin's
/// <c>AEffect*</c>. Used both for scanning (load → read → close) and for live instances. Dispose unloads
/// the library.
/// </summary>
public sealed unsafe class Vst2Module : IDisposable
{
    /// <summary>The file extensions that identify a VST2 module on this OS.</summary>
    public static IReadOnlyList<string> Extensions =>
        OperatingSystem.IsWindows() ? new[] { ".dll" }
        : OperatingSystem.IsMacOS() ? new[] { ".vst" }
        : new[] { ".so" };

    private readonly nint _handle;
    private readonly delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<Vst2Api.AEffect*, int, int, nint, void*, float, nint>, Vst2Api.AEffect*> _entry;
    private bool _disposed;

    public string Path { get; }

    public Vst2Module(string path)
    {
        var binary = ResolveBinary(path) ?? throw new FileNotFoundException($"No VST2 binary for '{path}'.");
        Path = path;
        _handle = NativeLibrary.Load(binary);
        try
        {
            nint entryPtr = 0;
            foreach (var sym in Vst2Api.EntrySymbols)
                if (NativeLibrary.TryGetExport(_handle, sym, out entryPtr) && entryPtr != 0) break;
            if (entryPtr == 0) throw new InvalidOperationException("no VSTPluginMain/main export.");
            _entry = (delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<Vst2Api.AEffect*, int, int, nint, void*, float, nint>, Vst2Api.AEffect*>)entryPtr;
        }
        catch
        {
            NativeLibrary.Free(_handle);
            throw;
        }
    }

    /// <summary>Calls the entry point and returns the plugin's AEffect (validated magic), or throws.</summary>
    public Vst2Api.AEffect* Open()
    {
        var effect = _entry(Vst2Api.Callback);
        if (effect == null) throw new InvalidOperationException("entry point returned null.");
        if (effect->Magic != Vst2Api.Magic) throw new InvalidOperationException("bad AEffect magic.");
        return effect;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeLibrary.Free(_handle);
    }

    /// <summary>Loads a module just long enough to read its descriptor (one plugin per VST2 module).</summary>
    public static IReadOnlyList<VstPluginDescriptor> ReadDescriptors(string path)
    {
        if (ResolveBinary(path) is null) return Array.Empty<VstPluginDescriptor>();

        using var module = new Vst2Module(path);
        var effect = module.Open();
        try
        {
            effect->User = null; // no managed instance during scanning
            if (effect->Dispatcher != null) effect->Dispatcher(effect, Vst2Api.EffOpen, 0, 0, null, 0);

            var name = DispatchString(effect, Vst2Api.EffGetEffectName) ;
            if (string.IsNullOrWhiteSpace(name)) name = DispatchString(effect, Vst2Api.EffGetProductString);
            if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileNameWithoutExtension(path);
            var vendor = DispatchString(effect, Vst2Api.EffGetVendorString);

            var isSynth = (effect->Flags & Vst2Api.FlagsIsSynth) != 0;
            if (!isSynth && effect->Dispatcher != null)
                isSynth = effect->Dispatcher(effect, Vst2Api.EffGetPlugCategory, 0, 0, null, 0) == Vst2Api.PlugCategSynth;

            var uid = ((uint)effect->UniqueId).ToString("x8", CultureInfo.InvariantCulture);
            return new[] { new VstPluginDescriptor(VstFormat.Vst2, path, uid, name!, vendor, isSynth, !isSynth) };
        }
        finally
        {
            if (effect->Dispatcher != null) effect->Dispatcher(effect, Vst2Api.EffClose, 0, 0, null, 0);
        }
    }

    private static string DispatchString(Vst2Api.AEffect* effect, int opcode)
    {
        if (effect->Dispatcher == null) return string.Empty;
        const int cap = 256;
        var buf = stackalloc byte[cap];
        effect->Dispatcher(effect, opcode, 0, 0, buf, 0);
        return Vst2Api.ReadFixed(buf, cap);
    }

    // Resolves the loadable binary: a plain file on Win/Linux, the bundle executable on macOS.
    private static string? ResolveBinary(string path)
    {
        if (File.Exists(path) && !OperatingSystem.IsMacOS()) return path;
        if (File.Exists(path)) return path;
        if (!Directory.Exists(path)) return null;

        // macOS .vst bundle: Contents/MacOS/<name>
        var macOsDir = System.IO.Path.Combine(path, "Contents", "MacOS");
        if (Directory.Exists(macOsDir)) return Directory.EnumerateFiles(macOsDir).FirstOrDefault();
        return null;
    }
}
