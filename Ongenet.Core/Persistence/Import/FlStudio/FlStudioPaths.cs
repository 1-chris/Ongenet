using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ongenet.Core.Persistence.Import.FlStudio;

/// <summary>Resolves FL Studio install / factory-data locations on the current machine.</summary>
internal static class FlStudioPaths
{
    /// <summary>
    /// Roots that replace <c>%FLStudioFactoryData%</c>. FLP paths are typically
    /// <c>%FLStudioFactoryData%/Data/Patches/...</c>, so the root is the folder that <em>contains</em> <c>Data</c>
    /// (e.g. <c>FL Studio 2026.app/Contents/Resources/FL</c>).
    /// </summary>
    public static IEnumerable<string> FactoryDataRoots()
    {
        foreach (var root in DiscoverAppBundleFlRoots())
            yield return root;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return "/Users/Shared/Image-Line/FL Studio";
        yield return Path.Combine(home, "Documents", "Image-Line", "FL Studio");
        yield return Path.Combine(home, "Library", "Application Support", "Image-Line", "FL Studio");
        yield return @"C:\Program Files\Image-Line\FL Studio 21";
        yield return @"C:\Program Files\Image-Line\FL Studio 20";
        yield return @"C:\Program Files\Image-Line\FL Studio";
    }

    public static IEnumerable<string> UserDataRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "Documents", "Image-Line", "FL Studio");
        yield return Path.Combine(home, "Library", "Application Support", "Image-Line", "FL Studio");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Image-Line", "FL Studio");
    }

    private static IEnumerable<string> DiscoverAppBundleFlRoots()
    {
        foreach (var apps in new[] { "/Applications", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications") })
        {
            if (!Directory.Exists(apps)) continue;
            string[] bundles;
            try
            {
                bundles = Directory.GetDirectories(apps, "FL Studio*.app");
            }
            catch
            {
                continue;
            }

            foreach (var bundle in bundles.OrderByDescending(b => b, StringComparer.OrdinalIgnoreCase))
            {
                var fl = Path.Combine(bundle, "Contents", "Resources", "FL");
                if (Directory.Exists(Path.Combine(fl, "Data")))
                    yield return fl;
            }
        }
    }

    /// <summary>Expand FL placeholders; returns the first existing file path or the rewritten string.</summary>
    public static string ExpandPlaceholders(string sample)
    {
        if (string.IsNullOrWhiteSpace(sample)) return sample;

        if (sample.Contains("%FLStudioFactoryData%", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var root in FactoryDataRoots())
            {
                if (!Directory.Exists(root)) continue;
                var expanded = sample
                    .Replace("%FLStudioFactoryData%", root, StringComparison.OrdinalIgnoreCase)
                    .Replace('\\', Path.DirectorySeparatorChar);
                if (File.Exists(expanded)) return expanded;
            }
        }

        if (sample.Contains("%FLStudioUserData%", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var root in UserDataRoots())
            {
                if (!Directory.Exists(root)) continue;
                var expanded = sample
                    .Replace("%FLStudioUserData%", root, StringComparison.OrdinalIgnoreCase)
                    .Replace('\\', Path.DirectorySeparatorChar);
                if (File.Exists(expanded)) return expanded;
            }
        }

        return sample.Replace('\\', Path.DirectorySeparatorChar);
    }
}
