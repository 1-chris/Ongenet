using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Project manipulation surface exposed to user scripts.</summary>
public interface IScriptingApi
{
    // Output
    IReadOnlyList<string> OutputLines { get; }
    event Action? OutputChanged;
    void Log(string message);
    void ClearOutput();

    // Project lifecycle
    void ClearProject();
    string GetProjectName();
    void SetProjectName(string name);
    double GetTempo();
    void SetTempo(double bpm);
    ScriptTimeSignature GetTimeSignature();
    void SetTimeSignature(int numerator, int denominator);
    int GetBarCount();
    void SetBarCount(int bars);
    void SetKeySignature(int rootPitchClass, ScriptScaleType scale);
    (int RootPitchClass, ScriptScaleType Scale) GetKeySignature();
    ScriptPlaybackMode GetPlaybackMode();
    void SetPlaybackMode(ScriptPlaybackMode mode);
    double GetLaunchQuantizeBeats();
    void SetLaunchQuantizeBeats(double beats);
    ScriptMpeSettings GetMpeSettings();
    void SetMpeSettings(ScriptMpeSettings settings);
    ScriptGrooveTemplate? GetActiveGroove();
    void SetActiveGroove(ScriptGrooveTemplate? groove);

    // Transport
    ScriptTransportState GetTransportState();
    void Play();
    void Stop();
    void SeekToBeat(double beat);
    double GetPlayheadBeats();
    ScriptLoopRegion GetLoopRegion();
    void SetLoopRegion(double start, double end);
    void SetLoopActive(bool active);

    // Tracks — read
    IReadOnlyDictionary<Guid, string> GetTrackNames();
    IReadOnlyList<ScriptTrackInfo> GetTracks();
    ScriptTrackInfo? GetTrack(Guid trackId);

    // Tracks — write
    void RenameTrack(Guid trackId, string name);
    Guid AddInstrumentTrack(string name);
    Guid AddInstrumentTrackWithId(Guid id, string name, string? colorKey = null);
    Guid AddAudioTrack(string name);
    Guid AddAudioTrackWithId(Guid id, string name, string? colorKey = null);
    Guid AddGroupTrack(string name);
    Guid AddGroupTrackWithId(Guid id, string name, string? colorKey = null);
    Guid AddReturnTrack(string name);
    Guid AddReturnTrackWithId(Guid id, string name, string? colorKey = null);
    Guid AddHybridTrack(string name);
    Guid AddHybridTrackWithId(Guid id, string name, string? colorKey = null);
    Guid AddMasterTrackWithId(Guid id, string name);
    void RemoveTrack(Guid trackId);
    void SetTrackVolume(Guid trackId, double volume);
    void SetTrackPan(Guid trackId, double pan);
    void SetTrackMuted(Guid trackId, bool muted);
    void SetTrackSoloed(Guid trackId, bool soloed);
    void SetTrackArmed(Guid trackId, bool armed);
    void SetTrackColor(Guid trackId, string colorKey);
    void SetTrackParent(Guid trackId, Guid? parentId);
    void SetTrackSurroundWidth(Guid trackId, double width);
    void SetTrackDrumMapId(Guid trackId, Guid? drumMapId);

    // Instruments
    IReadOnlyList<ScriptInstrumentInfo> GetInstruments(Guid trackId);
    void SetInstrument(Guid trackId, int slotIndex, string typeId);
    void RemoveInstrument(Guid trackId, int slotIndex);
    void SetInstrumentEnabled(Guid trackId, int slotIndex, bool enabled);
    void SetInstrumentParameter(Guid trackId, int slotIndex, string paramName, double value);
    void SetInstrumentBoolParameter(Guid trackId, int slotIndex, string paramName, bool value);
    void SetInstrumentChoiceParameter(Guid trackId, int slotIndex, string paramName, int choiceIndex);
    void LoadInstrumentPreset(Guid trackId, int slotIndex, string presetName);
    void SetInstrumentOutputBus(Guid trackId, int slotIndex, int busIndex, Guid? outputTrackId);
    void SetComponentStateJson(Guid trackId, int slotIndex, int? effectIndex, string typeId, string stateJson);

    // Effects (track inserts; effectIndex -1 = slot pre-FX on instrument slot 0)
    IReadOnlyList<ScriptEffectInfo> GetEffects(Guid trackId, int instrumentSlotIndex = -1);
    void AddEffect(Guid trackId, string typeId, int instrumentSlotIndex = -1);
    void RemoveEffect(Guid trackId, int effectIndex, int instrumentSlotIndex = -1);
    void SetEffectEnabled(Guid trackId, int effectIndex, bool enabled, int instrumentSlotIndex = -1);
    void SetEffectParameter(Guid trackId, int effectIndex, string paramName, double value, int instrumentSlotIndex = -1);
    void SetEffectBoolParameter(Guid trackId, int effectIndex, string paramName, bool value, int instrumentSlotIndex = -1);
    void SetEffectChoiceParameter(Guid trackId, int effectIndex, string paramName, int choiceIndex, int instrumentSlotIndex = -1);
    void LoadEffectPreset(Guid trackId, int effectIndex, string presetName, int instrumentSlotIndex = -1);

    // Clips — read
    IReadOnlyList<ScriptClipInfo> GetClips(Guid? trackId = null);
    IReadOnlyList<ScriptMidiNote> GetMidiNotes(Guid clipId);
    ScriptAudioClipMetadata? GetAudioClipMetadata(Guid clipId);

    // Clips — write
    Guid CreateMidiClip(Guid trackId, string name, double startBeat, double lengthBeats);
    Guid CreateMidiClipWithId(Guid id, Guid trackId, string name, double startBeat, double lengthBeats);
    Guid CreateAudioClip(Guid trackId, string name, double startBeat, double lengthBeats, ScriptAudioClipMetadata metadata);
    Guid CreateAudioClipWithId(Guid id, Guid trackId, string name, double startBeat, double lengthBeats, ScriptAudioClipMetadata metadata);
    void DeleteClip(Guid clipId);
    void MoveClip(Guid clipId, double startBeat);
    void ResizeClip(Guid clipId, double lengthBeats);
    Guid DuplicateClip(Guid clipId, double? offsetBeats = null);
    void RenameClip(Guid clipId, string name);
    void SetClipLinkedGroup(Guid clipId, Guid? groupId);
    void ClearMidiNotes(Guid clipId);
    void AddMidiNote(Guid clipId, ScriptMidiNote note);
    void AddMidiNotes(Guid clipId, IReadOnlyList<ScriptMidiNote> notes);
    void AddMidiControlChange(Guid clipId, ScriptMidiControlChange cc);
    void ClearMidiControlChanges(Guid clipId);

    // MIDI bulk edits
    void QuantizeAllMidiClips(double gridBeats);
    void QuantizeClip(Guid clipId, double gridBeats);
    void TransposeAllMidiClips(int semitones);
    void ScaleAllMidiVelocities(double factor);
    void HumanizeAllMidiClips(int maxTicks);
    void ApplyChanceToAllMidiClips(float chance);

    // Automation
    IReadOnlyList<ScriptAutomationLaneInfo> GetAutomationLanes(Guid trackId);
    void AddAutomationLane(Guid trackId, ScriptAutomationBinding binding);
    void ClearAutomationLane(Guid trackId, ScriptAutomationBinding binding);
    void AddAutomationPoint(Guid trackId, ScriptAutomationBinding binding, ScriptAutomationPoint point);
    void AddTrackModulator(Guid trackId, ScriptTrackModulatorInfo modulator);

    // Routing
    IReadOnlyList<ScriptTrackSendInfo> GetSends(Guid trackId);
    void AddSend(Guid trackId, Guid sendId, Guid targetTrackId, double level, bool preFader, bool enabled);
    void SetSendLevel(Guid trackId, Guid sendId, double level);
    IReadOnlyList<ScriptMultiOutputRouteInfo> GetMultiOutputRoutes();
    void AddMultiOutputRoute(ScriptMultiOutputRouteInfo route);

    // Patterns & session
    IReadOnlyList<ScriptPatternInfo> GetPatterns();
    Guid AddPatternWithId(Guid id, string name, double lengthBeats, int colorIndex);
    void AddPatternChannel(Guid patternId, ScriptPatternChannelInfo channel);
    void SetPatternSteps(Guid patternId, Guid channelId, IReadOnlyList<ScriptStepData> steps);
    IReadOnlyList<ScriptPatternClipInfo> GetPatternClips();
    void AddPatternClip(ScriptPatternClipInfo clip);
    IReadOnlyList<ScriptSessionClipInfo> GetSessionClips();
    void AddSessionClip(ScriptSessionClipInfo clip);

    // Timeline meta
    IReadOnlyList<ScriptMarkerInfo> GetMarkers();
    void AddMarker(ScriptMarkerInfo marker);
    IReadOnlyList<ScriptSectionInfo> GetSections();
    void AddSection(ScriptSectionInfo section);
    IReadOnlyList<ScriptChordRegionInfo> GetChordRegions();
    void SetChordTrackEnabled(bool enabled);
    void AddChordRegion(ScriptChordRegionInfo region);
    IReadOnlyList<ScriptDrumMapInfo> GetDrumMaps();
    void AddDrumMap(ScriptDrumMapInfo map);
    IReadOnlyList<ScriptExpressionMapInfo> GetExpressionMaps();
    void AddExpressionMap(ScriptExpressionMapInfo map);
    IReadOnlyList<ScriptVideoTrackInfo> GetVideoTracks();
    void AddVideoTrack(ScriptVideoTrackInfo track);
    IReadOnlyList<ScriptControlRoomProfileInfo> GetControlRoomProfiles();
    void AddControlRoomProfile(ScriptControlRoomProfileInfo profile);

    // Export
    string ExportProjectAsScript(ExportScriptOptions? options = null);
    string ExportInstrumentSlotAsScript(Guid trackId, int slotIndex, ExportScriptOptions? options = null);
    string ExportEffectChainAsScript(Guid trackId, int instrumentSlotIndex = -1, ExportScriptOptions? options = null);
    string ExportPresetAsScript(Guid trackId, int? slotIndex, int? effectIndex, ExportScriptOptions? options = null);

    // Live scripting
    IDisposable OnTransportStateChanged(Action<ScriptTransportState> handler);
    IDisposable OnBeat(Action<double> handler, double gridBeats = 1.0);
    IDisposable OnClipChanged(Action<ScriptClipInfo> handler);
    void StopLive();
}

/// <summary>Generates portable C# project scripts from the live project graph.</summary>
public interface IProjectScriptExporter
{
    string Export(Project project, ExportScriptOptions? options = null);
}

/// <summary>Generates portable C# preset scripts for instruments and effect chains.</summary>
public interface IPresetScriptExporter
{
    string ExportInstrumentSlot(Project project, Guid trackId, int slotIndex, ExportScriptOptions? options = null);
    string ExportEffectChain(Project project, Guid trackId, int instrumentSlotIndex, ExportScriptOptions? options = null);
    string ExportPreset(Project project, Guid trackId, int? slotIndex, int? effectIndex, ExportScriptOptions? options = null);
}
