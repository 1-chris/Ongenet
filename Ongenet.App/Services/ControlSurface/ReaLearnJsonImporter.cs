using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.App.Services.ControlSurface;

/// <summary>Best-effort conversion of ReaLearn JSON exports to <c>.ongencontroller</c> files.</summary>
public sealed class ReaLearnJsonImporter : IControlSurfaceImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string FormatId => "realearn-json";

    public bool CanImport(string filePath)
        => Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase);

    public ImportResult Import(string filePath, string outputDirectory)
    {
        var report = new ImportReport();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = doc.RootElement;
            var mappings = FindMappingsArray(root);
            if (mappings is not { } mappingsEl)
            {
                report.Messages.Add("No mappings array found in JSON.");
                return new ImportResult { Success = false, Report = report };
            }

            var def = new ControlSurfaceDefinition
            {
                SchemaVersion = 1,
                Id = SanitizeId(Path.GetFileNameWithoutExtension(filePath)),
                Name = $"Imported ({Path.GetFileName(filePath)})"
            };

            foreach (var item in mappingsEl.EnumerateArray())
            {
                if (!TryConvertMapping(item, def.Bindings, report))
                    report.BindingsSkipped++;
            }

            if (def.Bindings.Count == 0)
            {
                report.Messages.Add("No compatible bindings could be converted.");
                return new ImportResult { Success = false, Report = report };
            }

            Directory.CreateDirectory(outputDirectory);
            var outPath = Path.Combine(outputDirectory, def.Id + ".ongencontroller");
            File.WriteAllText(outPath, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true }));
            report.BindingsImported = def.Bindings.Count;
            report.Messages.Add($"Wrote {def.Bindings.Count} binding(s) to {Path.GetFileName(outPath)}.");
            return new ImportResult { Success = true, DefinitionId = def.Id, Report = report };
        }
        catch (Exception ex)
        {
            report.Messages.Add(ex.Message);
            return new ImportResult { Success = false, Report = report };
        }
    }

    private static JsonElement? FindMappingsArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        foreach (var name in new[] { "mappings", "Mappings", "actions", "Actions", "items" })
        {
            if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr;
        }

        return null;
    }

    private static bool TryConvertMapping(JsonElement item, List<ControlSurfaceBinding> bindings, ImportReport report)
    {
        var source = GetString(item, "source") ?? GetString(item, "Source");
        if (source is null && item.TryGetProperty("sourceSpec", out var spec))
            source = GetString(spec, "type") ?? GetString(spec, "Type");

        var target = GetString(item, "target") ?? GetString(item, "Target");
        if (target is null && item.TryGetProperty("targetSpec", out var tspec))
            target = GetString(tspec, "type") ?? GetString(tspec, "Type");

        var channel = GetInt(item, "channel") ?? GetInt(item, "Channel") ?? -1;
        var number = GetInt(item, "number") ?? GetInt(item, "Number")
                     ?? GetInt(item, "note") ?? GetInt(item, "Note")
                     ?? GetInt(item, "controller") ?? GetInt(item, "Controller");
        if (number is null) return false;

        var isNote = source?.Contains("note", StringComparison.OrdinalIgnoreCase) == true
                     || item.TryGetProperty("note", out _);
        var action = MapTargetAction(target, number.Value);
        if (action is null)
        {
            report.Messages.Add($"Skipped unsupported target: {target ?? "(unknown)"}");
            return false;
        }

        bindings.Add(new ControlSurfaceBinding
        {
            Action = action,
            IsNote = isNote,
            Channel = channel,
            Number = number.Value,
            SceneIndex = action == "LaunchScene" && number.Value is >= 36 and <= 51 ? number.Value - 36 : null
        });
        return true;
    }

    private static string? MapTargetAction(string? target, int number)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            // Heuristic: Launchpad-style note grid → scene launch by note offset.
            if (number is >= 36 and <= 51)
                return "LaunchScene";
            return null;
        }

        var t = target.ToLowerInvariant();
        if (t.Contains("play")) return "PlayPause";
        if (t.Contains("stop") && t.Contains("all")) return "StopAll";
        if (t.Contains("stop")) return "Stop";
        if (t.Contains("record")) return "Record";
        if (t.Contains("scene")) return "LaunchScene";
        if (t.Contains("clip") || t.Contains("slot")) return "LaunchSlot";
        if (t.Contains("volume")) return "MixerVolume";
        if (t.Contains("pan")) return "MixerPan";
        return null;
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string SanitizeId(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        var id = new string(chars).Trim('-');
        return string.IsNullOrEmpty(id) ? "imported-controller" : id.ToLowerInvariant();
    }
}
