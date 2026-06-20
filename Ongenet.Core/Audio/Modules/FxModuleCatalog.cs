using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Modules;

/// <summary>
/// The catalog of built-in <see cref="FxModule"/>s: stable ids, display names, and factories. Drives
/// the "add module" menu and rebuilds a module from its saved id on load. Chorus/Phaser/Lo-Fi reuse the
/// existing effect DSP through <see cref="EffectBackedModule"/>; comb/low-pass/tape-stop are bespoke.
/// </summary>
public static class FxModuleCatalog
{
    public sealed record Entry(string Id, string Name, Func<FxModule> Create);

    public static readonly IReadOnlyList<Entry> All = new List<Entry>
    {
        new(TapeStopModule.ModuleId, "Tape Stop", () => new TapeStopModule()),
        new("lofi", "Lo-Fi", () => new EffectBackedModule("lofi", "Lo-Fi",
            new BitcrusherEffect(), fx => ((BitcrusherEffect)fx).Mix, (fx, v) => ((BitcrusherEffect)fx).Mix = v)),
        new(CombModule.ModuleId, "Comb", () => new CombModule()),
        new("phaser", "Phaser", () => new EffectBackedModule("phaser", "Phaser",
            new PhaserEffect(), fx => ((PhaserEffect)fx).Mix, (fx, v) => ((PhaserEffect)fx).Mix = v)),
        new("chorus", "Chorus", () => new EffectBackedModule("chorus", "Chorus",
            new ChorusEffect(), fx => ((ChorusEffect)fx).Mix, (fx, v) => ((ChorusEffect)fx).Mix = v)),
        new(LowPassModule.ModuleId, "Low-Pass", () => new LowPassModule()),
    };

    /// <summary>Creates a module by id, or null if the id is unknown (forward-compat on load).</summary>
    public static FxModule? Create(string id)
        => All.FirstOrDefault(e => e.Id == id)?.Create();

    /// <summary>A fresh rack containing every built-in module in the default order, all disabled.</summary>
    public static FxModuleRack DefaultRack()
    {
        var rack = new FxModuleRack();
        foreach (var e in All)
        {
            var m = e.Create();
            m.Enabled = false;
            rack.Modules.Add(m);
        }

        rack.Commit();
        return rack;
    }
}
