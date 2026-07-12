using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;

namespace Ongenet.Scripting.Export;

internal static class ComponentScriptEmitter
{
    public static void EmitInstrumentSlot(StringBuilder sb, string trackVar, int slotIndex, InstrumentSlot slot, ExportScriptOptions options)
    {
        sb.AppendLine($"api.SetInstrument({trackVar}, {slotIndex}, {ScriptCodeGenerator.StringLiteral(slot.Instrument.TypeId)});");
        if (!slot.Enabled)
            sb.AppendLine($"api.SetInstrumentEnabled({trackVar}, {slotIndex}, false);");

        if (options.DetectBuiltInPresets && slot.Instrument is IPresetProvider provider)
        {
            var snapshot = ScriptingParameterHelper.Snapshot(slot.Instrument.Parameters);
            for (var i = 0; i < provider.PresetNames.Count; i++)
            {
                provider.LoadPreset(i);
                if (ScriptingParameterHelper.Snapshot(slot.Instrument.Parameters).SequenceEqual(snapshot, ParamComparer.Instance))
                {
                    sb.AppendLine($"api.LoadInstrumentPreset({trackVar}, {slotIndex}, {ScriptCodeGenerator.StringLiteral(provider.PresetNames[i])});");
                    return;
                }
            }

            RestoreSnapshot(slot.Instrument.Parameters, snapshot);
        }

        EmitParameters(sb, trackVar, slotIndex, null, slot.Instrument.Parameters, isInstrument: true);
        for (var fx = 0; fx < slot.Effects.Count; fx++)
            EmitEffect(sb, trackVar, fx, slot.Effects[fx], slotIndex, options);
    }

    public static void EmitEffectChain(StringBuilder sb, string trackVar, IList<IAudioEffect> chain, int instrumentSlotIndex, ExportScriptOptions options)
    {
        for (var i = 0; i < chain.Count; i++)
            EmitEffect(sb, trackVar, i, chain[i], instrumentSlotIndex, options);
    }

    private static void EmitEffect(StringBuilder sb, string trackVar, int effectIndex, IAudioEffect effect, int instrumentSlotIndex, ExportScriptOptions options)
    {
        var slotArg = instrumentSlotIndex < 0 ? "-1" : instrumentSlotIndex.ToString();
        sb.AppendLine($"api.AddEffect({trackVar}, {ScriptCodeGenerator.StringLiteral(effect.TypeId)}, {slotArg});");
        if (!effect.Enabled)
            sb.AppendLine($"api.SetEffectEnabled({trackVar}, {effectIndex}, false, {slotArg});");
        EmitParameters(sb, trackVar, effectIndex, slotArg, effect.Parameters, isInstrument: false);
    }

    private static void EmitParameters(StringBuilder sb, string trackVar, int index, string? slotArg, IReadOnlyList<Core.Audio.Parameters.Parameter> parameters, bool isInstrument)
    {
        foreach (var p in parameters)
        {
            switch (p)
            {
                case Core.Audio.Parameters.FloatParameter f:
                    if (isInstrument)
                        sb.AppendLine($"api.SetInstrumentParameter({trackVar}, {index}, {ScriptCodeGenerator.StringLiteral(f.Name)}, {ScriptCodeGenerator.DoubleLiteral(f.Value)});");
                    else
                        sb.AppendLine($"api.SetEffectParameter({trackVar}, {index}, {ScriptCodeGenerator.StringLiteral(f.Name)}, {ScriptCodeGenerator.DoubleLiteral(f.Value)}, {slotArg});");
                    break;
                case Core.Audio.Parameters.BoolParameter b:
                    if (isInstrument)
                        sb.AppendLine($"api.SetInstrumentBoolParameter({trackVar}, {index}, {ScriptCodeGenerator.StringLiteral(b.Name)}, {(b.Value ? "true" : "false")});");
                    else
                        sb.AppendLine($"api.SetEffectBoolParameter({trackVar}, {index}, {ScriptCodeGenerator.StringLiteral(b.Name)}, {(b.Value ? "true" : "false")}, {slotArg});");
                    break;
                case Core.Audio.Parameters.ChoiceParameter c:
                    if (isInstrument)
                        sb.AppendLine($"api.SetInstrumentChoiceParameter({trackVar}, {index}, {ScriptCodeGenerator.StringLiteral(c.Name)}, {c.SelectedIndex});");
                    else
                        sb.AppendLine($"api.SetEffectChoiceParameter({trackVar}, {index}, {ScriptCodeGenerator.StringLiteral(c.Name)}, {c.SelectedIndex}, {slotArg});");
                    break;
            }
        }
    }

    private static void RestoreSnapshot(IReadOnlyList<Core.Audio.Parameters.Parameter> parameters, IReadOnlyList<ScriptParameterValue> snapshot)
    {
        foreach (var v in snapshot)
        {
            switch (v.Kind)
            {
                case ScriptParameterKind.Float:
                    ScriptingParameterHelper.SetByName(parameters, v.Name, v.FloatValue);
                    break;
                case ScriptParameterKind.Bool:
                    ScriptingParameterHelper.SetBoolByName(parameters, v.Name, v.BoolValue);
                    break;
                case ScriptParameterKind.Choice:
                    ScriptingParameterHelper.SetChoiceByName(parameters, v.Name, v.ChoiceIndex);
                    break;
            }
        }
    }

    private sealed class ParamComparer : IEqualityComparer<ScriptParameterValue>
    {
        public static readonly ParamComparer Instance = new();
        public bool Equals(ScriptParameterValue? x, ScriptParameterValue? y)
        {
            if (x is null || y is null) return x == y;
            return x.Kind == y.Kind && x.Name == y.Name &&
                   (x.Kind switch
                   {
                       ScriptParameterKind.Float => Math.Abs(x.FloatValue - y.FloatValue) < 1e-6,
                       ScriptParameterKind.Bool => x.BoolValue == y.BoolValue,
                       ScriptParameterKind.Choice => x.ChoiceIndex == y.ChoiceIndex,
                       _ => false
                   });
        }

        public int GetHashCode(ScriptParameterValue obj) => HashCode.Combine(obj.Name, obj.Kind);
    }
}
