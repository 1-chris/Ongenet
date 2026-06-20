using System;
using System.Runtime.InteropServices;
using Ongenet.Lv2.Interop;

namespace Ongenet.Lv2;

/// <summary>
/// A loaded LV2 plugin binary (the bundle's shared library): owns the native handle and resolves the
/// <c>lv2_descriptor</c> for a given plugin URI by walking the export's index. Used only for live
/// instances — discovery never loads a binary. Dispose unloads the library.
/// </summary>
public sealed unsafe class Lv2Module : IDisposable
{
    private readonly nint _handle;
    private bool _disposed;

    public string BinaryPath { get; }

    public Lv2Module(string binaryPath)
    {
        BinaryPath = binaryPath;
        _handle = NativeLibrary.Load(binaryPath);
    }

    /// <summary>Finds the <c>LV2_Descriptor</c> whose URI equals <paramref name="uri"/>, or null.</summary>
    public Lv2Api.LV2_Descriptor* FindDescriptor(string uri)
    {
        if (!NativeLibrary.TryGetExport(_handle, Lv2Api.EntrySymbol, out var entryPtr) || entryPtr == 0)
            return null;

        var entry = (delegate* unmanaged[Cdecl]<uint, Lv2Api.LV2_Descriptor*>)entryPtr;
        for (var i = 0u; ; i++)
        {
            var desc = entry(i);
            if (desc == null) break;
            if (Lv2Api.ReadUtf8(desc->Uri) == uri) return desc;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeLibrary.Free(_handle);
    }
}
