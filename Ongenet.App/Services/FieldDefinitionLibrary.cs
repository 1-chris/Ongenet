using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Persistence;

namespace Ongenet.App.Services;

/// <summary>One user Field definition on disk.</summary>
public sealed record FieldDefinitionItem(
    Guid DefinitionId,
    FieldGraphRole Role,
    string TypeId,
    string DisplayName,
    string Category,
    string FullPath);

public interface IFieldDefinitionLibrary
{
    IReadOnlyList<FieldDefinitionItem> Instruments { get; }
    IReadOnlyList<FieldDefinitionItem> Effects { get; }
    event Action? Changed;

    void Rescan();

    /// <summary>Validates and saves a new or updated definition from the live host graph/surface.</summary>
    FieldDefinitionValidation.Result SaveFromInstrument(FieldInstrument host, string displayName,
        string? category = null, string? author = null, Guid? existingDefinitionId = null);

    FieldDefinitionValidation.Result SaveFromEffect(FieldEffect host, string displayName,
        string? category = null, string? author = null, Guid? existingDefinitionId = null);

    bool Delete(string typeId);
    string? PathFor(string typeId);
}

/// <summary>
/// Scans <c>.ongenfielddef</c> packages under the user Instruments/Field and Effects/Field folders,
/// registers them with the instrument/effect registries, and handles save/update/delete.
/// </summary>
public sealed class FieldDefinitionLibrary : IFieldDefinitionLibrary
{
    private readonly IFieldNodeRegistry _nodes;
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;

    private readonly Dictionary<string, CachedDefinition> _cache = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public FieldDefinitionLibrary(IFieldNodeRegistry nodes, IInstrumentRegistry instruments, IEffectRegistry effects)
    {
        _nodes = nodes;
        _instruments = instruments;
        _effects = effects;
        Rescan();
    }

    public IReadOnlyList<FieldDefinitionItem> Instruments { get; private set; } = Array.Empty<FieldDefinitionItem>();
    public IReadOnlyList<FieldDefinitionItem> Effects { get; private set; } = Array.Empty<FieldDefinitionItem>();

    public event Action? Changed;

    public void Rescan()
    {
        lock (_lock)
        {
            var previous = _cache.Keys.ToHashSet(StringComparer.Ordinal);
            _cache.Clear();

            ScanFolder(AppPaths.UserFieldInstrumentsDirectory(), FieldGraphRole.Instrument);
            ScanFolder(AppPaths.UserFieldEffectsDirectory(), FieldGraphRole.Effect);

            foreach (var orphan in previous.Except(_cache.Keys, StringComparer.Ordinal))
            {
                if (FieldGraphDefinition.IsUserInstrumentType(orphan)) _instruments.Unregister(orphan);
                else if (FieldGraphDefinition.IsUserEffectType(orphan)) _effects.Unregister(orphan);
            }

            foreach (var (typeId, cached) in _cache)
                Register(cached);

            Instruments = _cache.Values
                .Where(c => c.Definition.Role == FieldGraphRole.Instrument)
                .Select(ToItem)
                .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Effects = _cache.Values
                .Where(c => c.Definition.Role == FieldGraphRole.Effect)
                .Select(ToItem)
                .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        Changed?.Invoke();
    }

    public FieldDefinitionValidation.Result SaveFromInstrument(FieldInstrument host, string displayName,
        string? category = null, string? author = null, Guid? existingDefinitionId = null)
    {
        var surface = host.Surface.Clone();
        FieldSurfaceSerializer.EnsureExposedFromParameterWidgets(surface);
        var validation = FieldDefinitionValidation.Validate(host.Graph, FieldGraphRole.Instrument, surface);
        if (!validation.Ok) return validation;

        var def = BuildDefinition(FieldGraphRole.Instrument, displayName, category, author,
            existingDefinitionId ?? host.DefinitionId, surface);
        var path = Path.Combine(AppPaths.UserFieldInstrumentsDirectory(), Sanitize(def.DisplayName) + "-" +
            def.DefinitionId.ToString("N")[..8] + FieldDefinitionFile.Extension);
        // Prefer a stable path keyed by definition id when updating.
        path = UniquePathFor(def, AppPaths.UserFieldInstrumentsDirectory(), path);

        AtomicSave(def, host.Graph, author ?? Environment.UserName, path);
        host.AdoptLibraryIdentity(def);
        Rescan();
        return validation;
    }

    public FieldDefinitionValidation.Result SaveFromEffect(FieldEffect host, string displayName,
        string? category = null, string? author = null, Guid? existingDefinitionId = null)
    {
        var surface = host.Surface.Clone();
        FieldSurfaceSerializer.EnsureExposedFromParameterWidgets(surface);
        var validation = FieldDefinitionValidation.Validate(host.Graph, FieldGraphRole.Effect, surface);
        if (!validation.Ok) return validation;

        var def = BuildDefinition(FieldGraphRole.Effect, displayName, category, author,
            existingDefinitionId ?? host.DefinitionId, surface);
        var path = UniquePathFor(def, AppPaths.UserFieldEffectsDirectory(),
            Path.Combine(AppPaths.UserFieldEffectsDirectory(), Sanitize(def.DisplayName) + "-" +
                def.DefinitionId.ToString("N")[..8] + FieldDefinitionFile.Extension));

        AtomicSave(def, host.Graph, author ?? Environment.UserName, path);
        host.AdoptLibraryIdentity(def);
        Rescan();
        return validation;
    }

    public bool Delete(string typeId)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(typeId, out var cached)) return false;
            try { if (File.Exists(cached.Path)) File.Delete(cached.Path); }
            catch { return false; }
        }

        Rescan();
        return true;
    }

    public string? PathFor(string typeId)
    {
        lock (_lock) return _cache.TryGetValue(typeId, out var c) ? c.Path : null;
    }

    private void ScanFolder(string dir, FieldGraphRole expectedRole)
    {
        foreach (var file in SafeEnumerate(dir))
        {
            try
            {
                using var fs = File.OpenRead(file);
                var loaded = FieldDefinitionFile.Load(fs, _nodes);
                if (loaded is null || loaded.Definition.Role != expectedRole) continue;
                _cache[loaded.Definition.TypeId] = new CachedDefinition(loaded.Definition, loaded.Graph, file);
            }
            catch
            {
                // skip corrupt packages
            }
        }
    }

    private void Register(CachedDefinition cached)
    {
        var def = cached.Definition;
        if (def.Role == FieldGraphRole.Instrument)
        {
            _instruments.Register(new InstrumentInfo(def.TypeId, def.DisplayName,
                () => CreateInstrument(cached), def.LibraryCategory));
        }
        else
        {
            _effects.Register(new EffectInfo(def.TypeId, def.DisplayName,
                () => CreateEffect(cached), def.LibraryCategory));
        }
    }

    private IInstrument CreateInstrument(CachedDefinition cached)
    {
        // Re-load from disk so projects get a fresh snapshot of the library definition at add-time.
        try
        {
            using var fs = File.OpenRead(cached.Path);
            var loaded = FieldDefinitionFile.Load(fs, _nodes);
            if (loaded is not null)
            {
                var inst = new FieldInstrument(_nodes, buildDefault: false);
                inst.ApplyDefinition(loaded.Definition, loaded.Graph);
                return inst;
            }
        }
        catch { /* fall through */ }

        var fallback = new FieldInstrument(_nodes, buildDefault: false);
        fallback.ApplyDefinition(cached.Definition, cached.Graph);
        return fallback;
    }

    private IAudioEffect CreateEffect(CachedDefinition cached)
    {
        try
        {
            using var fs = File.OpenRead(cached.Path);
            var loaded = FieldDefinitionFile.Load(fs, _nodes);
            if (loaded is not null)
            {
                var fx = new FieldEffect(_nodes, buildDefault: false);
                fx.ApplyDefinition(loaded.Definition, loaded.Graph);
                return fx;
            }
        }
        catch { /* fall through */ }

        var fallback = new FieldEffect(_nodes, buildDefault: false);
        fallback.ApplyDefinition(cached.Definition, cached.Graph);
        return fallback;
    }

    private FieldGraphDefinition BuildDefinition(FieldGraphRole role, string displayName,
        string? category, string? author, Guid? existingId, FieldSurfaceDefinition surface)
    {
        var id = existingId is { } eid && eid != Guid.Empty ? eid : Guid.NewGuid();
        var now = DateTime.UtcNow.Ticks;
        long created = now;
        lock (_lock)
        {
            foreach (var cached in _cache.Values)
            {
                if (cached.Definition.DefinitionId == id)
                {
                    created = cached.Definition.CreatedTicks;
                    break;
                }
            }
        }

        return new FieldGraphDefinition
        {
            DefinitionId = id,
            Role = role,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Untitled" : displayName.Trim(),
            Category = category?.Trim() ?? "",
            Author = author?.Trim() ?? Environment.UserName,
            CreatedTicks = created,
            ModifiedTicks = now,
            Surface = surface
        };
    }

    private string UniquePathFor(FieldGraphDefinition def, string folder, string preferred)
    {
        lock (_lock)
        {
            foreach (var cached in _cache.Values)
            {
                if (cached.Definition.DefinitionId == def.DefinitionId)
                    return cached.Path;
            }
        }

        if (!File.Exists(preferred)) return preferred;
        return Path.Combine(folder, def.DefinitionId.ToString("N") + FieldDefinitionFile.Extension);
    }

    private static void AtomicSave(FieldGraphDefinition def, FieldGraph graph, string author, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
            FieldDefinitionFile.Save(def, graph, author, fs);
        File.Move(tmp, path, overwrite: true);
    }

    private static FieldDefinitionItem ToItem(CachedDefinition c)
        => new(c.Definition.DefinitionId, c.Definition.Role, c.Definition.TypeId, c.Definition.DisplayName,
            c.Definition.LibraryCategory, c.Path);

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*" + FieldDefinitionFile.Extension, SearchOption.TopDirectoryOnly);
        }
        catch { return Array.Empty<string>(); }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim();
        return s.Length > 0 ? s : "field";
    }

    private sealed class CachedDefinition
    {
        public CachedDefinition(FieldGraphDefinition definition, FieldGraph graph, string path)
        {
            Definition = definition;
            Graph = graph;
            Path = path;
        }

        public FieldGraphDefinition Definition { get; }
        public FieldGraph Graph { get; }
        public string Path { get; }
    }
}
