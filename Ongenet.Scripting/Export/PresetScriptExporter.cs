using System;
using System.Text;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;

namespace Ongenet.Scripting.Export;

/// <summary>Generates portable C# preset scripts for instruments and effect chains.</summary>
public sealed class PresetScriptExporter : IPresetScriptExporter
{
    public string ExportInstrumentSlot(Project project, Guid trackId, int slotIndex, ExportScriptOptions? options = null)
    {
        options ??= new ExportScriptOptions();
        var track = project.Tracks.Find(t => t.Id == trackId)
            ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        if (slotIndex < 0 || slotIndex >= track.Instruments.Count)
            throw new InvalidOperationException($"Instrument slot {slotIndex} does not exist.");

        var sb = new StringBuilder();
        sb.Append(ScriptCodeGenerator.Header("instrument preset", track.Name + " slot " + slotIndex));
        sb.AppendLine("// Apply to an open instrument track:");
        sb.AppendLine("var trackId = api.GetTracks().First(t => t.Kind == ScriptTrackKind.Instrument).Id;");
        sb.AppendLine("var trackVar = \"trackId\"; // use trackId variable below");
        sb.AppendLine();
        var slot = track.Instruments[slotIndex];
        ComponentScriptEmitter.EmitInstrumentSlot(sb, "trackId", slotIndex, slot, options);
        sb.AppendLine("api.Log(\"Instrument preset applied.\");");
        return sb.ToString();
    }

    public string ExportEffectChain(Project project, Guid trackId, int instrumentSlotIndex, ExportScriptOptions? options = null)
    {
        options ??= new ExportScriptOptions();
        var track = project.Tracks.Find(t => t.Id == trackId)
            ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");

        var sb = new StringBuilder();
        sb.Append(ScriptCodeGenerator.Header("effect chain", track.Name));
        sb.AppendLine("// Clears and rebuilds the track insert chain (instrumentSlotIndex -1) or slot pre-FX:");
        sb.AppendLine("var trackId = api.GetTracks().First(t => t.Id == /* your track */ Guid.Empty).Id;");
        sb.AppendLine();

        var chain = instrumentSlotIndex < 0
            ? track.Effects
            : track.Instruments[instrumentSlotIndex].Effects;

        for (var i = chain.Count - 1; i >= 0; i--)
            sb.AppendLine($"api.RemoveEffect(trackId, {i}, {instrumentSlotIndex});");

        ComponentScriptEmitter.EmitEffectChain(sb, "trackId", chain, instrumentSlotIndex, options ?? new ExportScriptOptions());
        sb.AppendLine("api.Log(\"Effect chain applied.\");");
        return sb.ToString();
    }

    public string ExportPreset(Project project, Guid trackId, int? slotIndex, int? effectIndex, ExportScriptOptions? options = null)
    {
        if (slotIndex is int si)
            return ExportInstrumentSlot(project, trackId, si, options);
        if (effectIndex is int ei)
        {
            var sb = new StringBuilder();
            sb.Append(ScriptCodeGenerator.Header("effect preset", "single effect"));
            sb.AppendLine("var trackId = /* target track */ Guid.Empty;");
            sb.AppendLine($"// Effect index {ei} on track inserts");
            var track = project.Tracks.Find(t => t.Id == trackId)!;
            if (ei >= 0 && ei < track.Effects.Count)
                ComponentScriptEmitter.EmitEffectChain(sb, "trackId", new[] { track.Effects[ei] }, -1, options ?? new ExportScriptOptions());
            return sb.ToString();
        }

        return ExportEffectChain(project, trackId, -1, options);
    }
}
