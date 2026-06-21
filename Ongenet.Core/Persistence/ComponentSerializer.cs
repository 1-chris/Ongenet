using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Persistence;

/// <summary>
/// Serializes a single instrument or effect — its type id, optional hosted sample, generic parameter map
/// and custom-state blob — to/from the chunked <see cref="OngenWriter"/>/<see cref="OngenReader"/> format.
/// Extracted from the project writer so the same component encoding backs both <c>.ongen</c> projects and
/// <c>.ongenpreset</c> presets; the byte layout is identical to what older projects already store.
/// </summary>
public static class ComponentSerializer
{
    /// <summary>One persisted parameter value (kind 0=float, 1=bool, 2=choice, -1=unknown).</summary>
    public readonly record struct PersistedParam(int Kind, double Number, bool Flag);

    /// <summary>
    /// Writes a component chunk: type id, enabled flag, sample ref (hash via <paramref name="store"/>) +
    /// name, parameters, then an optional custom-state blob for <see cref="IProjectStatefulComponent"/>s.
    /// </summary>
    public static void WriteComponent(OngenWriter w, string typeId, object component,
        IReadOnlyList<Parameter> parameters, SampleStore store, bool enabled, ISampleHost? host)
    {
        w.WriteChunk(c =>
        {
            c.WriteString(typeId);
            c.WriteBool(enabled);

            var sampleRef = host?.CurrentSample is { } buf ? store.Add(buf) : "";
            c.WriteString(sampleRef);
            c.WriteString(host?.SampleName ?? "");

            WriteParameters(c, parameters);

            // Custom state for anything not captured by parameters (e.g. the EQ's band list).
            if (component is IProjectStatefulComponent stateful)
            {
                c.WriteBool(true);
                c.WriteChunk(stateful.WriteProjectState);
            }
            else
            {
                c.WriteBool(false);
            }
        });
    }

    public static void WriteParameters(OngenWriter w, IReadOnlyList<Parameter> parameters)
    {
        w.WriteInt(parameters.Count);
        foreach (var p in parameters)
        {
            w.WriteString(p.Name);
            switch (p)
            {
                case FloatParameter f: w.WriteInt(0); w.WriteDouble(f.Value); break;
                case BoolParameter b: w.WriteInt(1); w.WriteBool(b.Value); break;
                case ChoiceParameter ch: w.WriteInt(2); w.WriteInt(ch.SelectedIndex); break;
                default: w.WriteInt(-1); break;
            }
        }
    }

    public static List<PersistedParam> ReadParameters(OngenReader r)
    {
        var count = r.ReadInt();
        var list = new List<PersistedParam>(count);
        for (var i = 0; i < count; i++)
        {
            r.ReadString(); // name (matched by index; kept for debugging/future use)
            var kind = r.ReadInt();
            switch (kind)
            {
                case 0: list.Add(new PersistedParam(0, r.ReadDouble(), false)); break;
                case 1: list.Add(new PersistedParam(1, 0, r.ReadBool())); break;
                case 2: list.Add(new PersistedParam(2, r.ReadInt(), false)); break;
                default: list.Add(new PersistedParam(-1, 0, false)); break;
            }
        }

        return list;
    }

    // Applies persisted values to live parameters by index (so duplicate names stay distinct). Mismatched
    // kinds or out-of-range indices are skipped, so added/removed parameters across versions degrade safely.
    public static void ApplyParameters(IReadOnlyList<Parameter> live, List<PersistedParam> persisted)
    {
        var n = Math.Min(live.Count, persisted.Count);
        for (var i = 0; i < n; i++)
        {
            var p = persisted[i];
            switch (live[i])
            {
                case FloatParameter f when p.Kind == 0: f.Value = p.Number; break;
                case BoolParameter b when p.Kind == 1: b.Value = p.Flag; break;
                case ChoiceParameter ch when p.Kind == 2: ch.SelectedIndex = (int)p.Number; break;
            }
        }
    }

    public static void ReadCustomState(OngenReader r, object? component)
    {
        if (!r.ReadBool()) return;
        r.ReadChunk(c => (component as IProjectStatefulComponent)?.ReadProjectState(c));
    }

    /// <summary>
    /// Reads one instrument component chunk, creating it via <paramref name="instruments"/> and restoring
    /// its parameters, hosted sample (resolved through <paramref name="sampleLookup"/>) and custom state.
    /// Returns a null instrument (with the persisted enabled flag) if the type is unavailable.
    /// </summary>
    public static (IInstrument? Instrument, bool Enabled) ReadInstrument(OngenReader r,
        IInstrumentRegistry instruments, Func<string, AudioSampleBuffer?> sampleLookup, List<string> warnings)
    {
        IInstrument? inst = null;
        var enabled = true;
        r.ReadChunk(c =>
        {
            var typeId = c.ReadString();
            enabled = c.ReadBool();
            var sampleRef = c.ReadString();
            var sampleName = c.ReadString();

            try { inst = instruments.Create(typeId); }
            catch { warnings.Add($"Instrument '{typeId}' is unavailable; it was skipped."); inst = null; }

            var persisted = ReadParameters(c);
            if (inst is not null) ApplyParameters(inst.Parameters, persisted);

            if (inst is ISampleHost host && sampleRef.Length > 0 && sampleLookup(sampleRef) is { } buf)
                host.LoadSample(buf, sampleName);

            ReadCustomState(c, inst);
        });
        return (inst, enabled);
    }

    /// <summary>Reads one effect component chunk, creating it via <paramref name="effects"/> and restoring
    /// its enabled flag, parameters and custom state. Returns null if the type is unavailable.</summary>
    public static IAudioEffect? ReadEffect(OngenReader r, IEffectRegistry effects, List<string> warnings)
    {
        IAudioEffect? fx = null;
        r.ReadChunk(c =>
        {
            var typeId = c.ReadString();
            var enabled = c.ReadBool();
            c.ReadString(); // sampleRef (effects don't host samples today)
            c.ReadString(); // sampleName

            try { fx = effects.Create(typeId); }
            catch { warnings.Add($"Effect '{typeId}' is unavailable; it was skipped."); fx = null; }

            var persisted = ReadParameters(c);
            if (fx is not null)
            {
                fx.Enabled = enabled;
                ApplyParameters(fx.Parameters, persisted);
            }

            ReadCustomState(c, fx);
        });
        return fx;
    }
}
