using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.App.Services;

/// <summary>Loads <c>.ongencontroller</c> definitions from the factory bundle and user config directory.</summary>
public sealed class ControlSurfaceLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private IReadOnlyList<ControlSurfaceDefinition> _definitions = Array.Empty<ControlSurfaceDefinition>();

    public IReadOnlyList<ControlSurfaceDefinition> Definitions => _definitions;

    public event Action? Changed;

    public void Rescan()
    {
        var list = new List<ControlSurfaceDefinition>();
        foreach (var dir in new[] { AppPaths.FactoryControllersDirectory(), AppPaths.UserControllersDirectory() })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in SafeEnumerate(dir))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var def = JsonSerializer.Deserialize<ControlSurfaceDefinition>(json, JsonOptions);
                    if (def is null || string.IsNullOrWhiteSpace(def.Id)) continue;
                    if (list.Any(d => d.Id == def.Id)) continue;
                    list.Add(def);
                }
                catch
                {
                    // Skip unreadable definitions.
                }
            }
        }

        _definitions = list;
        Changed?.Invoke();
    }

    public ControlSurfaceDefinition? FindById(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : _definitions.FirstOrDefault(d => d.Id == id);

    /// <summary>Picks the first definition whose port-name patterns match any enabled device.</summary>
    public ControlSurfaceDefinition? MatchDevice(IEnumerable<string> portDisplayNames)
    {
        var names = portDisplayNames.ToList();
        foreach (var def in _definitions)
        {
            var patterns = def.Match?.PortNameContains;
            if (patterns is null || patterns.Count == 0) continue;
            if (names.Any(n => patterns.Any(p => n.Contains(p, StringComparison.OrdinalIgnoreCase))))
                return def;
        }

        return null;
    }

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        try { return Directory.EnumerateFiles(root, "*.ongencontroller", SearchOption.AllDirectories); }
        catch { return Array.Empty<string>(); }
    }
}
