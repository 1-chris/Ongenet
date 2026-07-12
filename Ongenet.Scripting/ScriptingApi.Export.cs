using System;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

public sealed partial class ScriptingApi
{
    public string ExportProjectAsScript(ExportScriptOptions? options = null)
    {
        if (_projectExporter is null)
            throw new InvalidOperationException("Project script export is not available.");
        return _projectExporter.Export(_project.Current, options);
    }

    public string ExportInstrumentSlotAsScript(Guid trackId, int slotIndex, ExportScriptOptions? options = null)
    {
        if (_presetExporter is null)
            throw new InvalidOperationException("Preset script export is not available.");
        return _presetExporter.ExportInstrumentSlot(_project.Current, trackId, slotIndex, options);
    }

    public string ExportEffectChainAsScript(Guid trackId, int instrumentSlotIndex = -1, ExportScriptOptions? options = null)
    {
        if (_presetExporter is null)
            throw new InvalidOperationException("Preset script export is not available.");
        return _presetExporter.ExportEffectChain(_project.Current, trackId, instrumentSlotIndex, options);
    }

    public string ExportPresetAsScript(Guid trackId, int? slotIndex, int? effectIndex, ExportScriptOptions? options = null)
    {
        if (_presetExporter is null)
            throw new InvalidOperationException("Preset script export is not available.");
        return _presetExporter.ExportPreset(_project.Current, trackId, slotIndex, effectIndex, options);
    }
}
