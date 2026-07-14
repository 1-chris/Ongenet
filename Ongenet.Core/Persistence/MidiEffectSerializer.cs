using System.Collections.Generic;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Persistence;

/// <summary>Persists MIDI note-FX chains using the same parameter encoding as instruments/effects.</summary>
public static class MidiEffectSerializer
{
    public static void Write(OngenWriter w, IMidiEffect fx)
    {
        w.WriteChunk(c =>
        {
            c.WriteString(fx.TypeId);
            c.WriteBool(fx.Enabled);
            ComponentSerializer.WriteParameters(c, fx.Parameters);
            if (fx is IProjectStatefulComponent stateful)
            {
                c.WriteBool(true);
                c.WriteChunk(stateful.WriteProjectState);
            }
            else c.WriteBool(false);
        });
    }

    public static IMidiEffect? Read(OngenReader r, IMidiEffectRegistry registry, List<string> warnings)
    {
        IMidiEffect? fx = null;
        r.ReadChunk(c =>
        {
            var typeId = c.ReadString();
            var enabled = c.ReadBool();
            try { fx = registry.Create(typeId); }
            catch
            {
                warnings.Add($"MIDI effect '{typeId}' is unavailable; it was skipped.");
                fx = null;
            }

            var persisted = ComponentSerializer.ReadParameters(c);
            if (fx is not null)
            {
                fx.Enabled = enabled;
                ComponentSerializer.ApplyParameters(fx.Parameters, persisted);
            }

            ComponentSerializer.ReadCustomState(c, fx);
        });
        return fx;
    }
}
