using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ongenet.Lv2.Interop;

/// <summary>
/// Builds and owns the NULL-terminated <c>LV2_Feature*</c> array handed to a plugin at
/// <c>instantiate()</c>. We advertise the minimum that the common synths/effects need:
/// <c>urid:map</c>/<c>unmap</c> (shared, process-wide), <c>buf-size:boundedBlockLength</c>, and an
/// <c>options:options</c> block exposing the min/max/nominal block length and sample rate. The option
/// value storage (and the feature/array memory) is kept alive for the plugin's whole lifetime and
/// freed on <see cref="Dispose"/>. Plugins requiring features we don't list here are filtered out
/// before instantiation by the caller.
/// </summary>
public sealed unsafe class Lv2HostFeatures : IDisposable
{
    private readonly List<nint> _allocs = new();
    private bool _disposed;

    /// <summary>The NULL-terminated feature array to pass to <c>instantiate()</c>.</summary>
    public Lv2Api.LV2_Feature** Array { get; }

    public Lv2HostFeatures(double sampleRate, int minBlock, int maxBlock, nint workerSchedule = 0)
    {
        // Option value cells (must outlive the plugin; read during instantiate or via options API).
        var minCell = (int*)Alloc(sizeof(int)); *minCell = minBlock;
        var maxCell = (int*)Alloc(sizeof(int)); *maxCell = maxBlock;
        var nomCell = (int*)Alloc(sizeof(int)); *nomCell = maxBlock;
        var srCell = (float*)Alloc(sizeof(float)); *srCell = (float)sampleRate;

        var atomInt = Lv2Api.MapUrid(Lv2Api.AtomInt);
        var atomFloat = Lv2Api.MapUrid(Lv2Api.AtomFloat);

        // Options array, terminated by a zeroed entry (key == 0).
        var options = (Lv2Api.LV2_Options_Option*)Alloc(sizeof(Lv2Api.LV2_Options_Option) * 5);
        options[0] = Option(Lv2Api.OptMinBlockLength, atomInt, sizeof(int), minCell);
        options[1] = Option(Lv2Api.OptMaxBlockLength, atomInt, sizeof(int), maxCell);
        options[2] = Option(Lv2Api.OptNominalBlockLength, atomInt, sizeof(int), nomCell);
        options[3] = Option(Lv2Api.OptSampleRate, atomFloat, sizeof(float), srCell);
        options[4] = default; // terminator

        // Feature structs. map/unmap point at shared singletons; bounded-block has no data; options
        // points at the array above. (Pointers stored as nint since they can't be generic args.)
        var featureList = new List<(string Uri, nint Data)>
        {
            (Lv2Api.FeatureUridMap, (nint)Lv2Api.UridMapData()),
            (Lv2Api.FeatureUridUnmap, (nint)Lv2Api.UridUnmapData()),
            (Lv2Api.FeatureBoundedBlock, 0),
            (Lv2Api.FeatureOptions, (nint)options),
        };
        if (workerSchedule != 0) featureList.Add((Lv2Api.FeatureWorkerSchedule, workerSchedule));
        var features = featureList.ToArray();

        var arr = (Lv2Api.LV2_Feature**)Alloc((features.Length + 1) * sizeof(void*));
        for (var i = 0; i < features.Length; i++)
        {
            var f = (Lv2Api.LV2_Feature*)Alloc(sizeof(Lv2Api.LV2_Feature));
            f->Uri = AllocUtf8(features[i].Uri);
            f->Data = (void*)features[i].Data;
            arr[i] = f;
        }

        arr[features.Length] = null; // NULL-terminated
        Array = arr;
    }

    private Lv2Api.LV2_Options_Option Option(string keyUri, uint type, uint size, void* value) => new()
    {
        Context = Lv2Api.OptionsContextInstance,
        Subject = 0,
        Key = Lv2Api.MapUrid(keyUri),
        Size = size,
        Type = type,
        Value = value,
    };

    private void* Alloc(int bytes)
    {
        var p = Marshal.AllocHGlobal(bytes);
        _allocs.Add(p);
        return (void*)p;
    }

    // Encodes a NUL-terminated UTF-8 copy into HGlobal memory so it frees with the rest in Dispose.
    private byte* AllocUtf8(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        var p = (byte*)Alloc(bytes.Length + 1);
        for (var i = 0; i < bytes.Length; i++) p[i] = bytes[i];
        p[bytes.Length] = 0;
        return p;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var p in _allocs) Marshal.FreeHGlobal(p);
        _allocs.Clear();
    }
}
