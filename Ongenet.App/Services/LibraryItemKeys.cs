using System;

namespace Ongenet.App.Services;

/// <summary>
/// Stable string keys for library favourites and user categories. Format is <c>kind:payload</c>
/// (payload may contain additional colons, e.g. absolute paths).
/// </summary>
public static class LibraryItemKeys
{
    public const string Instrument = "instrument";
    public const string Effect = "effect";
    public const string File = "file";
    public const string Folder = "folder";
    public const string SoundFont = "soundfont";
    public const string Preset = "preset";
    public const string EffectChain = "chain";
    public const string ModulatorChain = "modchain";
    public const string Project = "project";

    public static string Make(string kind, string payload) => kind + ":" + payload;

    public static string InstrumentKey(string id) => Make(Instrument, id);
    public static string EffectKey(string id) => Make(Effect, id);
    public static string FileKey(string path) => Make(File, NormalizePath(path));
    public static string FolderKey(string pathOrScope) => Make(Folder, NormalizePath(pathOrScope));
    public static string SoundFontKey(string path) => Make(SoundFont, NormalizePath(path));
    public static string PresetKey(string path) => Make(Preset, NormalizePath(path));
    public static string EffectChainKey(string path) => Make(EffectChain, NormalizePath(path));
    public static string ModulatorChainKey(string path) => Make(ModulatorChain, NormalizePath(path));
    public static string ProjectKey(string path) => Make(Project, NormalizePath(path));

    /// <summary>Named non-filesystem group, e.g. instrument category "Synth" → <c>folder:instruments:Synth</c>.</summary>
    public static string NamedFolderKey(string scope, string title) => Make(Folder, scope + ":" + title);

    public static bool TryParse(string key, out string kind, out string payload)
    {
        kind = "";
        payload = "";
        if (string.IsNullOrEmpty(key)) return false;
        var i = key.IndexOf(':');
        if (i <= 0 || i >= key.Length - 1) return false;
        kind = key[..i];
        payload = key[(i + 1)..];
        return true;
    }

    public static string? KindOf(string key) => TryParse(key, out var kind, out _) ? kind : null;

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        try { return System.IO.Path.GetFullPath(path); }
        catch { return path; }
    }
}
