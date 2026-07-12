using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Optional project folder sync and read-only share manifest.</summary>
public static class CollaborationService
{
    public sealed class ShareManifest
    {
        public string ProjectName { get; set; } = "";
        public string ProjectPath { get; set; } = "";
        public DateTime ExportedUtc { get; set; }
        public bool ReadOnly { get; set; } = true;
        public string ShareId { get; set; } = Guid.NewGuid().ToString("N");
    }

    public static void ExportShareManifest(Project project, string projectFilePath, string syncFolder)
    {
        Directory.CreateDirectory(syncFolder);
        var manifest = new ShareManifest
        {
            ProjectName = project.Name,
            ProjectPath = Path.GetFileName(projectFilePath),
            ExportedUtc = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(syncFolder, "share.json"), json);

        var dest = Path.Combine(syncFolder, manifest.ProjectPath);
        if (File.Exists(projectFilePath) && !string.Equals(projectFilePath, dest, StringComparison.OrdinalIgnoreCase))
            File.Copy(projectFilePath, dest, overwrite: true);
    }

    public static ShareManifest? LoadManifest(string syncFolder)
    {
        var path = Path.Combine(syncFolder, "share.json");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<ShareManifest>(File.ReadAllText(path));
    }

    public static bool TryPullLatest(string syncFolder, out string? projectPath)
    {
        projectPath = null;
        var manifest = LoadManifest(syncFolder);
        if (manifest is null) return false;
        var candidate = Path.Combine(syncFolder, manifest.ProjectPath);
        if (!File.Exists(candidate)) return false;
        projectPath = candidate;
        return true;
    }

    /// <summary>Versioned self-hosted collab — snapshots project file with timestamp.</summary>
    public static string PushVersion(string projectFilePath, string syncFolder)
    {
        Directory.CreateDirectory(syncFolder);
        var versionsDir = Path.Combine(syncFolder, "versions");
        Directory.CreateDirectory(versionsDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var name = Path.GetFileNameWithoutExtension(projectFilePath);
        var ext = Path.GetExtension(projectFilePath);
        var dest = Path.Combine(versionsDir, $"{name}_{stamp}{ext}");
        File.Copy(projectFilePath, dest, overwrite: false);
        AppendVersionIndex(syncFolder, dest);
        return dest;
    }

    public static IReadOnlyList<string> ListVersions(string syncFolder)
    {
        var indexPath = Path.Combine(syncFolder, "versions", "index.txt");
        if (!File.Exists(indexPath)) return Array.Empty<string>();
        return File.ReadAllLines(indexPath);
    }

    private static void AppendVersionIndex(string syncFolder, string versionPath)
    {
        var indexPath = Path.Combine(syncFolder, "versions", "index.txt");
        File.AppendAllText(indexPath, versionPath + Environment.NewLine);
    }
}
