using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Ongenet.Vst.Vst3.Interop;

namespace Ongenet.Vst.Vst3;

/// <summary>
/// A loaded VST3 module (a <c>.vst3</c> bundle's shared library): resolves the per-OS binary inside the
/// bundle, runs the module init entry, and owns the <c>IPluginFactory</c>. Used both to read class
/// descriptors (scanning) and to create live component instances. Dispose runs the module exit entry and
/// unloads the library.
/// </summary>
public sealed unsafe class Vst3Module : IDisposable
{
    private readonly nint _handle;
    private readonly delegate* unmanaged[Cdecl]<void> _exit;
    private void* _factory;
    private bool _disposed;

    public string Path { get; }
    public void* Factory => _factory;

    public Vst3Module(string path)
    {
        var binary = ResolveBinary(path) ?? throw new FileNotFoundException($"No VST3 binary for '{path}'.");
        Path = path;
        _handle = NativeLibrary.Load(binary);
        try
        {
            // Run the module init entry (InitDll / ModuleEntry / bundleEntry) before touching the factory.
            foreach (var sym in Vst3Api.InitSymbols)
                if (NativeLibrary.TryGetExport(_handle, sym, out var initPtr) && initPtr != 0)
                {
                    // ModuleEntry/bundleEntry take one pointer arg; InitDll takes none. Passing an extra
                    // register is harmless under the x64 calling convention, so a single shape covers all.
                    ((delegate* unmanaged[Cdecl]<void*, byte>)initPtr)(null);
                    break;
                }

            foreach (var sym in Vst3Api.ExitSymbols)
                if (NativeLibrary.TryGetExport(_handle, sym, out var exitPtr) && exitPtr != 0)
                {
                    _exit = (delegate* unmanaged[Cdecl]<void>)exitPtr;
                    break;
                }

            if (!NativeLibrary.TryGetExport(_handle, Vst3Api.FactorySymbol, out var factoryPtr) || factoryPtr == 0)
                throw new InvalidOperationException("no GetPluginFactory export.");

            _factory = ((delegate* unmanaged[Cdecl]<void*>)factoryPtr)();
            if (_factory == null) throw new InvalidOperationException("GetPluginFactory returned null.");
        }
        catch
        {
            NativeLibrary.Free(_handle);
            throw;
        }
    }

    /// <summary>Creates a component instance for the class with hex id <paramref name="uidHex"/>, or null.</summary>
    public void* CreateComponent(string uidHex)
    {
        var cid = Vst3Api.HexToTuid(uidHex);
        fixed (byte* cidp = cid) return CreateInstance(cidp, Vst3Api.IidComponent);
    }

    /// <summary>Creates an instance of class id <paramref name="cid"/> implementing interface <paramref name="iid"/>, or null.</summary>
    public void* CreateInstance(byte* cid, byte[] iid)
    {
        var fv = *(Vst3Api.PluginFactoryVtbl**)_factory;
        if (fv->CreateInstance == null) return null;
        void* obj = null;
        fixed (byte* iidp = iid)
            if (fv->CreateInstance(_factory, cid, iidp, &obj) != Vst3Api.ResultOk) return null;
        return obj;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_factory != null) { Vst3Api.Release(_factory); _factory = null; }
        try { if (_exit != null) _exit(); } catch { /* ignore */ }
        NativeLibrary.Free(_handle);
    }

    /// <summary>Reads the descriptors of every Audio Module class in a bundle (no instances created).</summary>
    public static IReadOnlyList<VstPluginDescriptor> ReadDescriptors(string path)
    {
        if (ResolveBinary(path) is null) return Array.Empty<VstPluginDescriptor>();

        using var module = new Vst3Module(path);
        var factory = module._factory;
        var list = new List<VstPluginDescriptor>();

        var baseV = *(Vst3Api.PluginFactoryVtbl**)factory;
        // getClassInfo2 (with sub-categories) lives on IPluginFactory2; query for it.
        var factory2 = Vst3Api.QueryInterface(factory, Vst3Api.IidPluginFactory2);
        var hasInfo2 = factory2 != null;
        var infoFactory = hasInfo2 ? factory2 : factory;
        var infoV = *(Vst3Api.PluginFactoryVtbl**)infoFactory;

        try
        {
            var count = baseV->CountClasses != null ? baseV->CountClasses(factory) : 0;
            for (var i = 0; i < count; i++)
            {
                string category, name, sub, vendor;
                byte[] cid;
                if (hasInfo2 && infoV->GetClassInfo2 != null)
                {
                    Vst3Api.PClassInfo2 info;
                    if (infoV->GetClassInfo2(infoFactory, i, &info) != Vst3Api.ResultOk) continue;
                    category = Vst3Api.ReadAscii(info.Category, 32);
                    name = Vst3Api.ReadAscii(info.Name, 64);
                    sub = Vst3Api.ReadAscii(info.SubCategories, 128);
                    vendor = Vst3Api.ReadAscii(info.Vendor, 64);
                    cid = ReadCid(info.Cid);
                }
                else
                {
                    Vst3Api.PClassInfo info;
                    if (baseV->GetClassInfo == null || baseV->GetClassInfo(factory, i, &info) != Vst3Api.ResultOk) continue;
                    category = Vst3Api.ReadAscii(info.Category, 32);
                    name = Vst3Api.ReadAscii(info.Name, 64);
                    sub = string.Empty;
                    vendor = string.Empty;
                    cid = ReadCid(info.Cid);
                }

                if (!string.Equals(category, Vst3Api.AudioModuleClass, StringComparison.Ordinal)) continue;

                var isInstrument = sub.Contains("Instrument", StringComparison.OrdinalIgnoreCase)
                                   || sub.Contains("Synth", StringComparison.OrdinalIgnoreCase);
                var uid = ToHex(cid);
                list.Add(new VstPluginDescriptor(VstFormat.Vst3, path, uid, name, vendor, isInstrument, !isInstrument));
            }
        }
        finally
        {
            if (factory2 != null) Vst3Api.Release(factory2);
        }

        return list;
    }

    private static byte[] ReadCid(byte* cid)
    {
        var b = new byte[16];
        for (var i = 0; i < 16; i++) b[i] = cid[i];
        return b;
    }

    private static string ToHex(byte[] cid)
    {
        var sb = new System.Text.StringBuilder(32);
        foreach (var b in cid) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    // Resolves the loadable binary inside a .vst3 bundle (or the file directly on Windows old-style).
    private static string? ResolveBinary(string path)
    {
        if (File.Exists(path) && !Directory.Exists(path))
        {
            // A plain .vst3 file (legacy Windows single-file). On other OSes a .vst3 is always a bundle.
            if (OperatingSystem.IsWindows()) return path;
        }

        if (!Directory.Exists(path)) return File.Exists(path) ? path : null;

        var contents = System.IO.Path.Combine(path, "Contents");
        if (!Directory.Exists(contents)) return null;

        if (OperatingSystem.IsMacOS())
        {
            var macOs = System.IO.Path.Combine(contents, "MacOS");
            return Directory.Exists(macOs) ? Directory.EnumerateFiles(macOs).FirstOrDefault() : null;
        }

        var archDir = System.IO.Path.Combine(contents, ArchFolder());
        if (!Directory.Exists(archDir)) return null;
        var ext = OperatingSystem.IsWindows() ? ".vst3" : ".so";
        return Directory.EnumerateFiles(archDir).FirstOrDefault(f => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
               ?? Directory.EnumerateFiles(archDir).FirstOrDefault();
    }

    private static string ArchFolder()
    {
        var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows()) return arm ? "arm64-win" : "x86_64-win";
        return arm ? "aarch64-linux" : "x86_64-linux";
    }
}
