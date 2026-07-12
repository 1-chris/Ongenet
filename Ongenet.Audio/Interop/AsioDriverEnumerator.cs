using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace Ongenet.Audio.Interop;

/// <summary>Enumerates installed ASIO drivers from the Windows registry (HKLM\SOFTWARE\ASIO).</summary>
internal static class AsioDriverEnumerator
{
    public sealed record AsioDriverInfo(string Name, string Clsid, string Description);

    public static IReadOnlyList<AsioDriverInfo> Enumerate()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<AsioDriverInfo>();
        return EnumerateWindows();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<AsioDriverInfo> EnumerateWindows()
    {
        var list = new List<AsioDriverInfo>();
#if NET
        try
        {
            using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ASIO");
            if (root is null) return list;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key is null) continue;
                var clsid = key.GetValue("CLSID") as string ?? string.Empty;
                var desc = key.GetValue("Description") as string ?? name;
                list.Add(new AsioDriverInfo(name, clsid, desc));
            }
        }
        catch { /* no registry access */ }
#endif
        return list;
    }
}
