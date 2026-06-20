using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Modules;

/// <summary>
/// An ordered, reorderable chain of <see cref="FxModule"/>s processed front-to-back. Disabled modules
/// are skipped. The host sets each module's <see cref="FxModule.ModulationOverride"/> before calling
/// <see cref="Process"/>. Reusable by any effect/plugin wanting a drag-to-reorder internal FX rack.
/// </summary>
public sealed class FxModuleRack
{
    /// <summary>The editable module list (UI thread). Reorder/add/remove here, then call <see cref="Commit"/>.</summary>
    public List<FxModule> Modules { get; } = new();

    // Lock-free snapshot the audio thread iterates, so UI-thread reordering never races Process().
    private volatile FxModule[] _active = Array.Empty<FxModule>();

    /// <summary>The committed snapshot the audio thread reads (for per-module modulation, etc.).</summary>
    public FxModule[] Active => _active;

    /// <summary>Publishes the current <see cref="Modules"/> order to the audio thread.</summary>
    public void Commit() => _active = Modules.ToArray();

    public void Prepare(AudioFormat format)
    {
        foreach (var m in Modules) m.Prepare(format);
        Commit();
    }

    public void Reset()
    {
        foreach (var m in _active) m.Reset();
    }

    public void Process(Span<float> buffer)
    {
        var active = _active;
        foreach (var m in active)
            if (m.Enabled) m.Process(buffer);
    }

    /// <summary>Moves the module at <paramref name="from"/> to index <paramref name="to"/> (clamped).</summary>
    public void Move(int from, int to)
    {
        if (from < 0 || from >= Modules.Count) return;
        to = Math.Clamp(to, 0, Modules.Count - 1);
        if (from == to) return;
        var m = Modules[from];
        Modules.RemoveAt(from);
        Modules.Insert(to, m);
        Commit();
    }

    public FxModuleRack Clone()
    {
        var rack = new FxModuleRack();
        foreach (var m in Modules) rack.Modules.Add(m.Clone());
        rack.Commit();
        return rack;
    }
}
