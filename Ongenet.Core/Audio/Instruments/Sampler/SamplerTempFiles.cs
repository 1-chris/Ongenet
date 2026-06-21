using System;
using System.Collections.Generic;
using System.IO;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// Tracks the temporary float32 raw files written for disk-streamed samples and deletes them when the
/// process exits. They are kept for the whole session (not deleted on patch reload) because undo/redo
/// snapshots share the same <see cref="SamplerSample"/> handles — deleting a raw file early would leave a
/// restored snapshot pointing at a missing file.
/// </summary>
public static class SamplerTempFiles
{
    private static readonly object Lock = new();
    private static readonly List<string> Files = new();
    private static bool _hooked;

    public static void Track(string path)
    {
        lock (Lock)
        {
            Files.Add(path);
            if (!_hooked)
            {
                _hooked = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteAll();
            }
        }
    }

    private static void DeleteAll()
    {
        lock (Lock)
        {
            foreach (var path in Files)
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch { /* best effort on shutdown */ }
            }

            Files.Clear();
        }
    }
}
