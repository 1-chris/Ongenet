using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Persistence;

namespace Ongenet.Desktop.Services;

/// <summary>One preset file on disk.</summary>
public sealed record PresetItem(string Name, string FullPath, string TypeId);

/// <summary>A named group of presets (one per instrument/effect type).</summary>
public sealed record PresetGroup(string Name, IReadOnlyList<PresetItem> Items);

public interface IPresetLibrary
{
    IReadOnlyList<PresetGroup> InstrumentPresets { get; }
    IReadOnlyList<PresetGroup> EffectPresets { get; }
    IReadOnlyList<PresetGroup> ChainPresets { get; }
    event Action? Changed;
    void Rescan();

    /// <summary>Saves the instrument's current settings as a user preset; returns the file path.</summary>
    string SaveInstrument(IInstrument instrument, string name);

    /// <summary>Saves the effect's current settings as a user preset; returns the file path.</summary>
    string SaveEffect(IAudioEffect effect, string name);

    /// <summary>Saves a whole effect chain as a user preset; returns the file path.</summary>
    string SaveChain(IReadOnlyList<IAudioEffect> effects, string name);
}

/// <summary>
/// Aggregates <c>.ongenpreset</c> files under the config presets directory into instrument/effect groups
/// for the library tabs, and writes new user presets. On first run it materializes the built-in
/// <see cref="IPresetProvider"/> presets (Kicka, Padda, …) as factory <c>.ongenpreset</c> files, so every
/// preset — built-in or user — flows through the same unified format and browser.
/// </summary>
public sealed class PresetLibrary : IPresetLibrary
{
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;

    public PresetLibrary(IInstrumentRegistry instruments, IEffectRegistry effects)
    {
        _instruments = instruments;
        _effects = effects;
        EnsureFactoryPresets();
        Rescan();
    }

    public IReadOnlyList<PresetGroup> InstrumentPresets { get; private set; } = Array.Empty<PresetGroup>();
    public IReadOnlyList<PresetGroup> EffectPresets { get; private set; } = Array.Empty<PresetGroup>();
    public IReadOnlyList<PresetGroup> ChainPresets { get; private set; } = Array.Empty<PresetGroup>();

    public event Action? Changed;

    public void Rescan()
    {
        var instr = new List<(string Group, PresetItem Item)>();
        var fx = new List<(string Group, PresetItem Item)>();
        var chains = new List<PresetItem>();

        var root = AppPaths.PresetsDirectory();
        foreach (var file in SafeEnumerate(root))
        {
            PresetMeta? meta;
            try { using var fs = File.OpenRead(file); meta = PresetFile.ReadMeta(fs); }
            catch { meta = null; }
            if (meta is null) continue;

            var name = meta.DisplayName.Length > 0 ? meta.DisplayName : Path.GetFileNameWithoutExtension(file);
            var item = new PresetItem(name, file, meta.TypeId);
            switch (meta.Kind)
            {
                case PresetKind.Instrument: instr.Add((DisplayNameFor(meta), item)); break;
                case PresetKind.EffectChain: chains.Add(item); break;
                default: fx.Add((DisplayNameFor(meta), item)); break;
            }
        }

        InstrumentPresets = Group(instr);
        EffectPresets = Group(fx);
        ChainPresets = chains.Count > 0
            ? new[] { new PresetGroup("FX Chains", chains.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList()) }
            : Array.Empty<PresetGroup>();
        Changed?.Invoke();
    }

    public string SaveInstrument(IInstrument instrument, string name)
    {
        var path = UserPresetPath("Instruments", instrument.TypeId, name);
        using (var fs = File.Create(path)) PresetFile.SaveInstrument(instrument, name, Environment.UserName, fs);
        Rescan();
        return path;
    }

    public string SaveEffect(IAudioEffect effect, string name)
    {
        var path = UserPresetPath("Effects", effect.TypeId, name);
        using (var fs = File.Create(path)) PresetFile.SaveEffect(effect, name, Environment.UserName, fs);
        Rescan();
        return path;
    }

    public string SaveChain(IReadOnlyList<IAudioEffect> effects, string name)
    {
        var dir = Path.Combine(AppPaths.PresetsDirectory(), "Chains");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Sanitize(name) + ".ongenpreset");
        using (var fs = File.Create(path)) PresetFile.SaveChain(effects, name, Environment.UserName, fs);
        Rescan();
        return path;
    }

    private static IReadOnlyList<PresetGroup> Group(List<(string Group, PresetItem Item)> items)
        => items
            .GroupBy(x => x.Group)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PresetGroup(g.Key, g.Select(x => x.Item)
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

    private string DisplayNameFor(PresetMeta meta)
    {
        var inst = _instruments.Available.FirstOrDefault(i => i.Id == meta.TypeId);
        if (inst is not null) return inst.DisplayName;
        var fx = _effects.Available.FirstOrDefault(e => e.Id == meta.TypeId);
        return fx?.DisplayName ?? meta.TypeId;
    }

    private static string UserPresetPath(string kindFolder, string typeId, string name)
    {
        var dir = Path.Combine(AppPaths.PresetsDirectory(), kindFolder, Sanitize(typeId));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Sanitize(name) + ".ongenpreset");
    }

    // Writes the built-in IPresetProvider presets as factory .ongenpreset files (once; skipped if present).
    private void EnsureFactoryPresets()
    {
        var factoryDir = AppPaths.FactoryPresetsDirectory();
        foreach (var info in _instruments.Available)
        {
            IInstrument instrument;
            try { instrument = _instruments.Create(info.Id); }
            catch { continue; }
            if (instrument is not IPresetProvider provider) continue;

            var dir = Path.Combine(factoryDir, Sanitize(info.DisplayName));
            for (var i = 0; i < provider.PresetNames.Count; i++)
            {
                var presetName = provider.PresetNames[i];
                var path = Path.Combine(dir, Sanitize(presetName) + ".ongenpreset");
                if (File.Exists(path)) continue;
                try
                {
                    Directory.CreateDirectory(dir);
                    provider.LoadPreset(i);
                    using var fs = File.Create(path);
                    PresetFile.SaveInstrument(instrument, presetName, "Factory", fs);
                }
                catch
                {
                    // Skip a preset that fails to materialize; the rest still work.
                }
            }
        }
    }

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        try { return Directory.EnumerateFiles(root, "*.ongenpreset", SearchOption.AllDirectories); }
        catch { return Array.Empty<string>(); }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim();
        return s.Length > 0 ? s : "preset";
    }
}
