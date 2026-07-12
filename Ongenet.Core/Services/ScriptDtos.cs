using System;
using System.Collections.Generic;

namespace Ongenet.Core.Services;

/// <summary>Track kind exposed to user scripts.</summary>
public enum ScriptTrackKind
{
    Audio,
    Instrument,
    Hybrid,
    Group,
    Return,
    Master,
    Midi,
    Pattern
}

/// <summary>Transport playback state exposed to user scripts.</summary>
public enum ScriptTransportState
{
    Stopped,
    Playing
}

/// <summary>Project playback mode exposed to scripts.</summary>
public enum ScriptPlaybackMode
{
    Arrangement,
    Session,
    Hybrid
}

/// <summary>Scale type exposed to scripts.</summary>
public enum ScriptScaleType
{
    Major,
    Minor,
    Dorian,
    Phrygian,
    Lydian,
    Mixolydian,
    HarmonicMinor,
    MelodicMinor,
    PentatonicMajor,
    PentatonicMinor,
    Blues,
    WholeTone,
    Chromatic
}

/// <summary>Automation target kind exposed to scripts.</summary>
public enum ScriptAutomationTargetKind
{
    TrackVolume,
    TrackPan,
    EffectEnabled,
    EffectParam,
    InstrumentParam,
    Tempo,
    TimeSignature
}

/// <summary>Parameter value kind for scripting.</summary>
public enum ScriptParameterKind
{
    Float,
    Bool,
    Choice
}

/// <summary>Pattern row source kind.</summary>
public enum ScriptPatternRowSourceKind
{
    InstrumentTrack,
    AudioSample
}

/// <summary>Session clip follow action.</summary>
public enum ScriptFollowAction
{
    Stop,
    PlayNext,
    PlayPrevious,
    PlayRandom,
    PlayFirst,
    PlayAgain
}

/// <summary>Session clip launch mode.</summary>
public enum ScriptSessionLaunchMode
{
    Trigger,
    Gate,
    Toggle,
    Repeat
}

/// <summary>Chord quality exposed to scripts.</summary>
public enum ScriptChordQuality
{
    Major,
    Minor,
    Dominant7,
    Major7,
    Minor7
}

/// <summary>Track modulator kind.</summary>
public enum ScriptModulatorKind
{
    Lfo,
    EnvelopeFollower
}

/// <summary>LFO wave shape for modulators.</summary>
public enum ScriptLfoWave
{
    Sine,
    Triangle,
    Square,
    Saw,
    Random
}

/// <summary>Read-only track snapshot for scripting.</summary>
public sealed record ScriptTrackInfo(
    Guid Id,
    string Name,
    ScriptTrackKind Kind,
    bool IsMuted,
    bool IsSoloed,
    bool IsArmed,
    double Volume,
    double Pan,
    Guid? ParentId = null,
    string ColorKey = "CatppuccinMauve",
    double SurroundWidth = 1.0,
    Guid? DrumMapId = null,
    bool AutomationCollapsed = false,
    bool GroupCollapsed = false);

/// <summary>Read-only clip snapshot for scripting.</summary>
public sealed record ScriptClipInfo(
    Guid Id,
    Guid TrackId,
    string Name,
    double StartBeat,
    double LengthBeats,
    bool IsAudio,
    int NoteCount,
    string? AudioFilePath = null,
    Guid? LinkedClipGroupId = null);

/// <summary>Project time signature exposed to scripts.</summary>
public sealed record ScriptTimeSignature(int Numerator, int Denominator);

/// <summary>Transport loop region exposed to scripts.</summary>
public sealed record ScriptLoopRegion(double Start, double End, bool IsActive);

/// <summary>MIDI note for scripting.</summary>
public sealed record ScriptMidiNote(
    int Note,
    double StartBeat,
    double LengthBeats,
    float Velocity,
    int SlideSemitones = 0,
    int PortamentoMs = 0,
    Guid? NoteGroupId = null,
    float Chance = 1f,
    int HumanizeTicks = 0);

/// <summary>MIDI control change for scripting.</summary>
public sealed record ScriptMidiControlChange(
    int Controller,
    int Value,
    double StartBeat,
    double LengthBeats);

/// <summary>Warp marker for audio clips.</summary>
public sealed record ScriptWarpMarker(double SourceSeconds, double BeatPosition);

/// <summary>Audio clip metadata (no PCM).</summary>
public sealed record ScriptAudioClipMetadata(
    string? AudioFilePath,
    double SourceOffsetSeconds,
    double SourceLengthSeconds,
    double? SourceTempo,
    string? SourceKey,
    bool StretchToTempo,
    bool PitchCorrected,
    string WarpMode,
    IReadOnlyList<ScriptWarpMarker>? WarpMarkers = null,
    double UserFadeInBeats = 0,
    double UserFadeOutBeats = 0,
    bool HasAraRegion = false,
    double AraPitchOffsetSemitones = 0);

/// <summary>Instrument slot snapshot.</summary>
public sealed record ScriptInstrumentInfo(
    int SlotIndex,
    string TypeId,
    string Name,
    bool Enabled,
    int OutputBusIndex,
    Guid? OutputTrackId,
    IReadOnlyList<ScriptEffectInfo> PreEffects);

/// <summary>Effect snapshot.</summary>
public sealed record ScriptEffectInfo(
    int Index,
    string TypeId,
    string Name,
    bool Enabled,
    IReadOnlyList<ScriptParameterValue> Parameters);

/// <summary>Parameter value snapshot.</summary>
public sealed record ScriptParameterValue(
    string Name,
    ScriptParameterKind Kind,
    double FloatValue = 0,
    bool BoolValue = false,
    int ChoiceIndex = 0);

/// <summary>Automation binding for scripting.</summary>
public sealed record ScriptAutomationBinding(
    ScriptAutomationTargetKind Kind,
    int EffectIndex,
    int ParamIndex);

/// <summary>Automation point for scripting.</summary>
public sealed record ScriptAutomationPoint(double Beat, double Value, double Curve = 0);

/// <summary>Automation lane snapshot.</summary>
public sealed record ScriptAutomationLaneInfo(
    ScriptAutomationBinding Binding,
    bool IsArmed,
    IReadOnlyList<ScriptAutomationPoint> Points);

/// <summary>Track modulator snapshot.</summary>
public sealed record ScriptTrackModulatorInfo(
    Guid Id,
    ScriptModulatorKind Kind,
    bool Enabled,
    double RateHz,
    double Depth,
    ScriptLfoWave Wave,
    ScriptAutomationBinding Target,
    bool TempoSync = false,
    double AttackSeconds = 0,
    double ReleaseSeconds = 0);

/// <summary>Track send snapshot.</summary>
public sealed record ScriptTrackSendInfo(
    Guid Id,
    Guid TargetTrackId,
    double Level,
    bool PreFader,
    bool Enabled);

/// <summary>Arrangement marker.</summary>
public sealed record ScriptMarkerInfo(Guid Id, string Name, double Beat);

/// <summary>Arrangement section referencing a marker.</summary>
public sealed record ScriptSectionInfo(Guid Id, Guid MarkerId);

/// <summary>Chord region on the chord track.</summary>
public sealed record ScriptChordRegionInfo(
    double StartBeat,
    double LengthBeats,
    string Symbol,
    ScriptChordQuality Quality);

/// <summary>Drum map entry.</summary>
public sealed record ScriptDrumMapEntryInfo(
    int Note,
    string Label,
    Guid? SampleClipId,
    double VelocityScale);

/// <summary>Drum map snapshot.</summary>
public sealed record ScriptDrumMapInfo(
    Guid Id,
    string Name,
    IReadOnlyList<ScriptDrumMapEntryInfo> Entries);

/// <summary>Expression map entry.</summary>
public sealed record ScriptExpressionMapEntryInfo(
    string Articulation,
    int KeyswitchNote,
    int CcNumber,
    int CcValue);

/// <summary>VST expression map snapshot.</summary>
public sealed record ScriptExpressionMapInfo(
    string Name,
    IReadOnlyList<ScriptExpressionMapEntryInfo> Entries);

/// <summary>Video track metadata (path only).</summary>
public sealed record ScriptVideoTrackInfo(
    Guid Id,
    string FilePath,
    double OffsetSeconds,
    double Fps,
    bool Muted,
    double InPointSeconds = 0,
    double OutPointSeconds = 0);

/// <summary>Step sequencer data.</summary>
public sealed record ScriptStepData(
    bool Active,
    int Note,
    float Velocity,
    float Pan,
    float Probability,
    int MicroTimingTicks);

/// <summary>Pattern channel snapshot.</summary>
public sealed record ScriptPatternChannelInfo(
    Guid Id,
    int Order,
    ScriptPatternRowSourceKind SourceKind,
    Guid TrackId,
    Guid? SampleClipId,
    string Name,
    bool Muted,
    double Volume,
    double Pan,
    IReadOnlyList<ScriptStepData>? Steps = null);

/// <summary>Pattern snapshot.</summary>
public sealed record ScriptPatternInfo(
    Guid Id,
    string Name,
    double LengthBeats,
    int ColorIndex,
    IReadOnlyList<ScriptPatternChannelInfo> Channels);

/// <summary>Pattern clip on the playlist.</summary>
public sealed record ScriptPatternClipInfo(
    Guid Id,
    Guid PatternId,
    Guid TrackId,
    double StartBeat,
    double LengthBeats);

/// <summary>Session clip slot.</summary>
public sealed record ScriptSessionClipInfo(
    Guid Id,
    Guid TrackId,
    int SceneIndex,
    string Name,
    double LengthBeats,
    ScriptSessionLaunchMode LaunchMode,
    ScriptFollowAction FollowAction,
    double LaunchQuantizeBeats,
    Guid? SourceClipId);

/// <summary>Multi-output routing entry.</summary>
public sealed record ScriptMultiOutputRouteInfo(
    Guid SourceTrackId,
    int SlotIndex,
    int PluginOutputBus,
    Guid DestinationTrackId,
    double Level);

/// <summary>MPE settings snapshot.</summary>
public sealed record ScriptMpeSettings(
    bool Enabled,
    int MasterChannel,
    int MemberChannelStart,
    int MemberChannelCount);

/// <summary>Groove template snapshot.</summary>
public sealed record ScriptGrooveTemplate(
    Guid Id,
    string Name,
    double SwingAmount,
    int Division,
    IReadOnlyList<double>? StepOffsets = null);

/// <summary>Control room profile snapshot.</summary>
public sealed record ScriptControlRoomProfileInfo(
    string Name,
    double CueVolume,
    double MainVolume,
    bool DimEnabled,
    double DimAmountDb);

/// <summary>Component custom state fallback (JSON or base64).</summary>
public sealed record ScriptComponentState(
    string TypeId,
    string StateJson);

/// <summary>Options for script export.</summary>
public sealed class ExportScriptOptions
{
    public bool IncludeComments { get; set; } = true;
    public bool PreserveStableIds { get; set; } = true;
    public bool DetectBuiltInPresets { get; set; } = true;
    public int MaxNotesPerBatch { get; set; } = 64;
}
