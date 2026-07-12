using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Ongenet.Core.Tests.Localization;

public sealed class LocalizationKeyParityTests
{
    private static readonly string ResourcesDir = FindResourcesDir();

    [Fact]
    public void Japanese_has_same_keys_as_English()
    {
        var en = LoadKeys(Path.Combine(ResourcesDir, "Strings.en.axaml"));
        var ja = LoadKeys(Path.Combine(ResourcesDir, "Strings.ja.axaml"));

        var missingInJa = new List<string>();
        foreach (var key in en.Keys)
        {
            if (!ja.ContainsKey(key))
                missingInJa.Add(key);
            else if (string.IsNullOrWhiteSpace(ja[key]))
                missingInJa.Add($"{key} (empty value)");
        }

        var orphanJa = new List<string>();
        foreach (var key in ja.Keys)
        {
            if (!en.ContainsKey(key))
                orphanJa.Add(key);
        }

        Assert.True(missingInJa.Count == 0,
            "Missing or empty in Strings.ja.axaml:\n" + string.Join("\n", missingInJa));
        Assert.True(orphanJa.Count == 0,
            "Orphan keys in Strings.ja.axaml (not in English):\n" + string.Join("\n", orphanJa));
    }

    private static Dictionary<string, string> LoadKeys(string path)
    {
        var text = File.ReadAllText(path);
        var dict = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(text, @"x:Key=""([^""]+)""[^>]*>([^<]*)</system:String>"))
            dict[m.Groups[1].Value] = m.Groups[2].Value;
        return dict;
    }

    private static string FindResourcesDir()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "Ongenet.App", "Resources");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Ongenet.App/Resources");
    }
}
