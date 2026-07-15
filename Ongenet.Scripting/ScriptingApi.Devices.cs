using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

public sealed partial class ScriptingApi
{
    public IReadOnlyList<ScriptInstrumentInfo> GetInstruments(Guid trackId)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        return track.Instruments.Select((s, i) => ScriptingApiSupport.ToInstrumentInfo(i, s)).ToArray();
    }

    public void SetInstrument(Guid trackId, int slotIndex, string typeId)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        var instrument = _instruments.Create(typeId);
        _history.Capture("Set instrument");
        while (track.Instruments.Count <= slotIndex)
            track.Instruments.Add(new InstrumentSlot(_instruments.Create(InstrumentRegistry.DefaultInstrumentId)) { Enabled = false });
        track.Instruments[slotIndex] = new InstrumentSlot(instrument) { Enabled = true };
        track.CommitInstruments();
        _events.Publish(new TrackChangedEvent(track));
    }

    public void RemoveInstrument(Guid trackId, int slotIndex)
    {
        var track = FindTrack(trackId);
        if (track is null || slotIndex < 0 || slotIndex >= track.Instruments.Count) return;
        _history.Capture("Remove instrument");
        track.Instruments.RemoveAt(slotIndex);
        track.CommitInstruments();
        _events.Publish(new TrackChangedEvent(track));
    }

    public void SetInstrumentEnabled(Guid trackId, int slotIndex, bool enabled)
    {
        var slot = ScriptingApiSupport.GetInstrumentSlot(FindTrack(trackId)!, slotIndex);
        if (slot.Enabled == enabled) return;
        _history.Capture("Toggle instrument");
        slot.Enabled = enabled;
        FindTrack(trackId)!.CommitInstruments();
    }

    public void SetInstrumentParameter(Guid trackId, int slotIndex, string paramName, double value)
    {
        var slot = ScriptingApiSupport.GetInstrumentSlot(FindTrack(trackId)!, slotIndex);
        _history.Capture("Change instrument parameter");
        ScriptingParameterHelper.SetByName(slot.Instrument.Parameters, paramName, value);
    }

    public void SetInstrumentBoolParameter(Guid trackId, int slotIndex, string paramName, bool value)
    {
        var slot = ScriptingApiSupport.GetInstrumentSlot(FindTrack(trackId)!, slotIndex);
        _history.Capture("Change instrument parameter");
        ScriptingParameterHelper.SetBoolByName(slot.Instrument.Parameters, paramName, value);
    }

    public void SetInstrumentChoiceParameter(Guid trackId, int slotIndex, string paramName, int choiceIndex)
    {
        var slot = ScriptingApiSupport.GetInstrumentSlot(FindTrack(trackId)!, slotIndex);
        _history.Capture("Change instrument parameter");
        ScriptingParameterHelper.SetChoiceByName(slot.Instrument.Parameters, paramName, choiceIndex);
    }

    public void LoadInstrumentPreset(Guid trackId, int slotIndex, string presetName)
    {
        var slot = ScriptingApiSupport.GetInstrumentSlot(FindTrack(trackId)!, slotIndex);
        if (slot.Instrument is not IPresetProvider provider)
            throw new InvalidOperationException($"Instrument '{slot.Instrument.Name}' has no built-in presets.");
        var index = provider.PresetNames.ToList().FindIndex(n => string.Equals(n, presetName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidOperationException($"Preset '{presetName}' was not found.");
        _history.Capture("Load instrument preset");
        provider.LoadPreset(index);
    }

    public void SetInstrumentOutputBus(Guid trackId, int slotIndex, int busIndex, Guid? outputTrackId)
    {
        var slot = ScriptingApiSupport.GetInstrumentSlot(FindTrack(trackId)!, slotIndex);
        _history.Capture("Change instrument output");
        slot.OutputBusIndex = busIndex;
        slot.OutputTrackId = outputTrackId;
        FindTrack(trackId)!.CommitInstruments();
    }

    public void SetComponentStateJson(Guid trackId, int slotIndex, int? effectIndex, string typeId, string stateJson)
    {
        // Placeholder for Field graphs and exotic state — stored as comment in export; runtime no-op until deserializer wired.
        Log($"SetComponentStateJson({typeId}) — custom state replay is best-effort.");
    }

    public IReadOnlyList<ScriptEffectInfo> GetEffects(Guid trackId, int instrumentSlotIndex = -1)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        var chain = ScriptingApiSupport.GetEffectChain(track, instrumentSlotIndex);
        return chain.Select((e, i) => ScriptingApiSupport.ToEffectInfo(i, e)).ToArray();
    }

    public void AddEffect(Guid trackId, string typeId, int instrumentSlotIndex = -1)
    {
        var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' was not found.");
        var effect = _effects.Create(typeId);
        _history.Capture("Add effect");
        var chain = ScriptingApiSupport.GetEffectChain(track, instrumentSlotIndex);
        chain.Add(effect);
        if (instrumentSlotIndex < 0) track.CommitEffects();
        else track.CommitInstruments();
        _events.Publish(new TrackChangedEvent(track));
    }

    public void RemoveEffect(Guid trackId, int effectIndex, int instrumentSlotIndex = -1)
    {
        var track = FindTrack(trackId);
        if (track is null) return;
        var chain = ScriptingApiSupport.GetEffectChain(track, instrumentSlotIndex);
        if (effectIndex < 0 || effectIndex >= chain.Count) return;
        _history.Capture("Remove effect");
        chain.RemoveAt(effectIndex);
        if (instrumentSlotIndex < 0) track.CommitEffects();
        else track.CommitInstruments();
        _events.Publish(new TrackChangedEvent(track));
    }

    public void SetEffectEnabled(Guid trackId, int effectIndex, bool enabled, int instrumentSlotIndex = -1)
    {
        var track = FindTrack(trackId)!;
        var chain = ScriptingApiSupport.GetEffectChain(track, instrumentSlotIndex);
        if (effectIndex < 0 || effectIndex >= chain.Count) return;
        if (chain[effectIndex].Enabled == enabled) return;
        _history.Capture("Toggle effect");
        chain[effectIndex].Enabled = enabled;
        if (instrumentSlotIndex < 0) track.CommitEffects();
        else track.CommitInstruments();
    }

    public void SetEffectParameter(Guid trackId, int effectIndex, string paramName, double value, int instrumentSlotIndex = -1)
    {
        var track = FindTrack(trackId)!;
        var chain = ScriptingApiSupport.GetEffectChain(track, instrumentSlotIndex);
        if (effectIndex < 0 || effectIndex >= chain.Count) return;
        _history.Capture("Change effect parameter");
        ScriptingParameterHelper.SetByName(chain[effectIndex].Parameters, paramName, value);
    }

    public void SetEffectBoolParameter(Guid trackId, int effectIndex, string paramName, bool value, int instrumentSlotIndex = -1)
    {
        var track = FindTrack(trackId)!;
        var chain = ScriptingApiSupport.GetEffectChain(track, instrumentSlotIndex);
        if (effectIndex < 0 || effectIndex >= chain.Count) return;
        _history.Capture("Change effect parameter");
        ScriptingParameterHelper.SetBoolByName(chain[effectIndex].Parameters, paramName, value);
    }

    public void SetEffectChoiceParameter(Guid trackId, int effectIndex, string paramName, int choiceIndex, int instrumentSlotIndex = -1)
    {
        var track = FindTrack(trackId)!;
        var chain = ScriptingApiSupport.GetEffectChain(track, instrumentSlotIndex);
        if (effectIndex < 0 || effectIndex >= chain.Count) return;
        _history.Capture("Change effect parameter");
        ScriptingParameterHelper.SetChoiceByName(chain[effectIndex].Parameters, paramName, choiceIndex);
    }

    public void LoadEffectPreset(Guid trackId, int effectIndex, string presetName, int instrumentSlotIndex = -1)
    {
        var track = FindTrack(trackId)!;
        var chain = ScriptingApiSupport.GetEffectChain(track, instrumentSlotIndex);
        if (effectIndex < 0 || effectIndex >= chain.Count) return;
        if (chain[effectIndex] is not IPresetProvider provider)
            throw new InvalidOperationException($"Effect '{chain[effectIndex].Name}' has no built-in presets.");
        var index = provider.PresetNames.ToList().FindIndex(n => string.Equals(n, presetName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidOperationException($"Preset '{presetName}' was not found.");
        _history.Capture("Load effect preset");
        provider.LoadPreset(index);
    }

    public void ApplyMasteringChain(Guid masterTrackId, string chainName = "full")
    {
        var track = FindTrack(masterTrackId)
            ?? throw new InvalidOperationException($"Track '{masterTrackId}' was not found.");
        if (track.Kind != TrackKind.Master)
            throw new InvalidOperationException("ApplyMasteringChain requires the Master track.");
        _history.Capture("Apply mastering chain");
        track.Effects.Clear();
        foreach (var fx in MasteringChains.Create(chainName))
            track.Effects.Add(fx);
        track.CommitEffects();
        _events.Publish(new TrackChangedEvent(track));
    }

    /// <inheritdoc cref="IScriptingApi.GetMasterMeterTap"/>
    public ScriptMasterMeterTap GetMasterMeterTap()
    {
        if (_engine is null)
            return ScriptMasterMeterTap.PostFader;
        return ToScriptMeterTap(_engine.MasterMeterTap);
    }

    /// <inheritdoc cref="IScriptingApi.SetMasterMeterTap"/>
    public void SetMasterMeterTap(ScriptMasterMeterTap tap)
    {
        if (_engine is null)
            throw new InvalidOperationException("Master meter tap is not available without a running audio engine.");
        _engine.MasterMeterTap = ToEngineMeterTap(tap);
    }

    private static ScriptMasterMeterTap ToScriptMeterTap(MasterMeterTap tap) => tap switch
    {
        MasterMeterTap.PreLimiter => ScriptMasterMeterTap.PreLimiter,
        MasterMeterTap.PostChain => ScriptMasterMeterTap.PostChain,
        _ => ScriptMasterMeterTap.PostFader
    };

    private static MasterMeterTap ToEngineMeterTap(ScriptMasterMeterTap tap) => tap switch
    {
        ScriptMasterMeterTap.PreLimiter => MasterMeterTap.PreLimiter,
        ScriptMasterMeterTap.PostChain => MasterMeterTap.PostChain,
        _ => MasterMeterTap.PostFader
    };
}
