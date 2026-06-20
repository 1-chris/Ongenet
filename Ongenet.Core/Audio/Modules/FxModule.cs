using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Modules;

/// <summary>
/// A self-contained, in-place audio processor that can be slotted into an ordered
/// <see cref="FxModuleRack"/> and reordered by the user — the building block of a modular multi-FX
/// chain (tape stop, lo-fi, chorus, comb, phaser, low-pass, …). Smaller and more composable than a
/// full <see cref="Effects.IAudioEffect"/>: most modules are thin wrappers that reuse the existing DSP
/// toolkit. Reusable by any effect or plugin that wants a drag-to-reorder internal FX rack.
/// </summary>
public abstract class FxModule
{
    /// <summary>Stable id used for the rack's saved order and for the "add module" catalog.</summary>
    public abstract string Id { get; }

    /// <summary>Display name.</summary>
    public abstract string Name { get; }

    /// <summary>When false the module is skipped (signal passes through untouched).</summary>
    public bool Enabled { get; set; }

    /// <summary>Editable parameters, rendered generically (and persisted by index).</summary>
    public abstract IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>
    /// Per-block modulation in 0..1 from an assigned <see cref="Modulation.ModulationCurve"/>, or null
    /// when nothing is assigned. When set it REPLACES the module's headline "amount" (mix/depth/intensity)
    /// for this block; when null the module uses its own static parameter. Set by the host before
    /// <see cref="Process"/>. Use <see cref="Amount"/> to resolve the two.
    /// </summary>
    public double? ModulationOverride { get; set; }

    /// <summary>The effective headline amount: the curve override when assigned, else the static value.</summary>
    protected double Amount(double staticValue) => ModulationOverride ?? staticValue;

    public abstract void Prepare(AudioFormat format);

    /// <summary>Processes <paramref name="buffer"/> (interleaved) in place.</summary>
    public abstract void Process(Span<float> buffer);

    /// <summary>Clears any internal buffers/filters (on gesture start or transport stop).</summary>
    public virtual void Reset() { }

    /// <summary>A fresh copy with the same settings.</summary>
    public abstract FxModule Clone();
}
