using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ongenet.Clap.Interop;

namespace Ongenet.Clap;

/// <summary>
/// A loaded CLAP module (a <c>.clap</c> shared library): owns the native handle, the initialised
/// <c>clap_entry</c>, and its plugin factory. Used both to read descriptors (scanning) and to
/// create live plugin instances. Dispose unloads the module.
/// </summary>
public sealed unsafe class ClapModule : IDisposable
{
    private readonly nint _handle;
    private ClapApi.ClapPluginEntry* _entry;
    private ClapApi.ClapPluginFactory* _factory;
    private bool _disposed;

    public string Path { get; }

    public ClapModule(string path)
    {
        Path = path;
        _handle = NativeLibrary.Load(path);
        try
        {
            var entryPtr = NativeLibrary.GetExport(_handle, ClapApi.EntrySymbol);
            _entry = (ClapApi.ClapPluginEntry*)entryPtr;
            if (!ClapApi.IsCompatible(_entry->ClapVersion))
                throw new InvalidOperationException($"Incompatible CLAP version in '{path}'.");

            var pathUtf8 = ClapApi.Utf8(path);
            try
            {
                if (_entry->Init != null && _entry->Init(pathUtf8) == 0)
                    throw new InvalidOperationException($"clap_entry.init failed for '{path}'.");
            }
            finally
            {
                ClapApi.FreeUtf8(pathUtf8);
            }

            var factoryId = ClapApi.Utf8(ClapApi.FactoryId);
            try
            {
                _factory = _entry->GetFactory != null ? (ClapApi.ClapPluginFactory*)_entry->GetFactory(factoryId) : null;
            }
            finally
            {
                ClapApi.FreeUtf8(factoryId);
            }

            if (_factory == null)
                throw new InvalidOperationException($"No plugin factory in '{path}'.");
        }
        catch
        {
            NativeLibrary.Free(_handle);
            throw;
        }
    }

    /// <summary>Reads the descriptors of every plugin in this module.</summary>
    public IReadOnlyList<ClapPluginDescriptor> ReadDescriptors()
    {
        var list = new List<ClapPluginDescriptor>();
        if (_factory == null || _factory->GetPluginCount == null || _factory->GetPluginDescriptor == null)
            return list;

        var count = _factory->GetPluginCount(_factory);
        for (var i = 0u; i < count; i++)
        {
            var desc = _factory->GetPluginDescriptor(_factory, i);
            if (desc == null) continue;

            var id = ClapApi.ReadUtf8(desc->Id);
            if (string.IsNullOrEmpty(id)) continue;

            var name = ClapApi.ReadUtf8(desc->Name) ?? id;
            var vendor = ClapApi.ReadUtf8(desc->Vendor) ?? string.Empty;

            var isInstrument = false;
            var isEffect = false;
            for (var f = desc->Features; f != null && *f != null; f++)
            {
                var feature = ClapApi.ReadUtf8(*f);
                if (feature == ClapApi.FeatureInstrument) isInstrument = true;
                else if (feature == ClapApi.FeatureAudioEffect) isEffect = true;
            }

            list.Add(new ClapPluginDescriptor(Path, id, name, vendor, isInstrument, isEffect));
        }

        return list;
    }

    /// <summary>Creates a plugin instance for <paramref name="pluginId"/> bound to <paramref name="host"/>, or null.</summary>
    public ClapApi.ClapPlugin* CreatePlugin(string pluginId, ClapApi.ClapHost* host)
    {
        if (_factory == null || _factory->CreatePlugin == null) return null;
        var idUtf8 = ClapApi.Utf8(pluginId);
        try
        {
            return _factory->CreatePlugin(_factory, host, idUtf8);
        }
        finally
        {
            ClapApi.FreeUtf8(idUtf8);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_entry != null && _entry->Deinit != null) _entry->Deinit(); }
        catch { /* ignore */ }
        _entry = null;
        _factory = null;
        NativeLibrary.Free(_handle);
    }
}
