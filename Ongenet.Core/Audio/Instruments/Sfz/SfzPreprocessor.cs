using System;
using System.Collections.Generic;
using System.Text;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// Resolves the SFZ textual preprocessor layer before tokenizing: strips comments (<c>//</c> line and
/// <c>/* */</c> block), inlines <c>#include</c> files, and applies <c>#define $VAR value</c> macro
/// substitution. Defines take effect from their point of declaration forward (ARIA semantics).
/// </summary>
public static class SfzPreprocessor
{
    public static string Expand(string text, SfzParseOptions? options, List<string> warnings)
    {
        var defines = new Dictionary<string, string>(StringComparer.Ordinal);
        if (options?.Defines is { } seed)
        {
            foreach (var kv in seed) defines[kv.Key] = kv.Value;
        }

        var sb = new StringBuilder(text.Length);
        ExpandInto(sb, text, options, defines, warnings, depth: 0);
        return sb.ToString();
    }

    private static void ExpandInto(StringBuilder sb, string text, SfzParseOptions? options,
        Dictionary<string, string> defines, List<string> warnings, int depth)
    {
        text = StripBlockComments(text);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = StripLineComment(rawLine).Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#define", StringComparison.OrdinalIgnoreCase))
            {
                ParseDefine(line, defines, warnings);
                continue;
            }

            if (line.StartsWith("#include", StringComparison.OrdinalIgnoreCase))
            {
                HandleInclude(sb, line, options, defines, warnings, depth);
                continue;
            }

            sb.Append(Substitute(line, defines, warnings)).Append('\n');
        }
    }

    private static void ParseDefine(string line, Dictionary<string, string> defines, List<string> warnings)
    {
        // #define $name value...
        var rest = line.Substring("#define".Length).Trim();
        var sep = IndexOfWhitespace(rest);
        if (sep < 0) { warnings.Add($"Malformed #define: '{line}'"); return; }

        var name = rest.Substring(0, sep);
        var value = rest.Substring(sep + 1).Trim();
        if (!name.StartsWith('$')) name = "$" + name;
        defines[name] = value;
    }

    private static void HandleInclude(StringBuilder sb, string line, SfzParseOptions? options,
        Dictionary<string, string> defines, List<string> warnings, int depth)
    {
        var path = ExtractIncludePath(line);
        if (path is null) { warnings.Add($"Malformed #include: '{line}'"); return; }

        if (depth >= (options?.MaxIncludeDepth ?? 16))
        {
            warnings.Add($"#include depth limit reached at '{path}'");
            return;
        }

        var resolver = options?.IncludeResolver;
        if (resolver is null) { warnings.Add($"#include '{path}' skipped (no resolver)"); return; }

        var included = resolver(path);
        if (included is null) { warnings.Add($"#include '{path}' not found"); return; }

        // Defines propagate into and out of included files, in declaration order.
        ExpandInto(sb, included, options, defines, warnings, depth + 1);
    }

    // Substitutes $VAR macros within a line. Unknown macros are left intact and reported once.
    private static string Substitute(string line, Dictionary<string, string> defines, List<string> warnings)
    {
        if (defines.Count == 0 || line.IndexOf('$') < 0) return line;

        var sb = new StringBuilder(line.Length);
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (c != '$') { sb.Append(c); i++; continue; }

            var j = i + 1;
            while (j < line.Length && IsIdentChar(line[j])) j++;
            var name = line.Substring(i, j - i);

            if (defines.TryGetValue(name, out var value)) sb.Append(value);
            else { sb.Append(name); warnings.Add($"Undefined macro '{name}'"); }

            i = j;
        }

        return sb.ToString();
    }

    private static string StripLineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx < 0 ? line : line.Substring(0, idx);
    }

    private static string StripBlockComments(string text)
    {
        if (text.IndexOf("/*", StringComparison.Ordinal) < 0) return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) break; // unterminated block comment: drop the rest
                // Preserve newlines inside the comment so line-based directives below stay aligned.
                for (var k = i; k < end; k++) if (text[k] == '\n') sb.Append('\n');
                i = end + 2;
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    private static string? ExtractIncludePath(string line)
    {
        var open = line.IndexOf('"');
        if (open < 0) return null;
        var close = line.IndexOf('"', open + 1);
        if (close <= open) return null;
        return line.Substring(open + 1, close - open - 1);
    }

    private static int IndexOfWhitespace(string s)
    {
        for (var i = 0; i < s.Length; i++) if (s[i] is ' ' or '\t' or '\r' or '\f' or '\v') return i;
        return -1;
    }

    private static bool IsIdentChar(char c)
        => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';
}
