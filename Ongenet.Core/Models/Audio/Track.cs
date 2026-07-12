using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.MidiFx;

namespace Ongenet.Core.Models.Audio;

/// <summary>
/// A single track in a <see cref="Project"/>: a named, coloured lane that holds clips
/// and carries mix settings. POCO by design — the Desktop layer wraps it in a view model
/// and raises change notifications.
/// </summary>
public sealed class Track
{
    /// <summary>Stable identity for selection and lookups.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "Track";

    /// <summary>The kind of material this track carries.</summary>
    public TrackKind Kind { get; set; } = TrackKind.Audio;

    /// <summary>
    /// The <see cref="Id"/> of the group/master bus this track's output routes into, or null to route
    /// straight to the master. Drives both audio routing and the timeline's nesting/indentation.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>True for a bus (group, return, or master) that sums child output rather than carrying clips.</summary>
    public bool IsBus => Kind is TrackKind.Group or TrackKind.Return or TrackKind.Master;

    /// <summary>Whether the track is muted.</summary>
    public bool IsMuted { get; set; }

    /// <summary>Whether the track is soloed.</summary>
    public bool IsSoloed { get; set; }

    /// <summary>
    /// Whether the track is armed for recording. Live MIDI input is captured into armed
    /// instrument tracks while the transport is recording. Not persisted.
    /// </summary>
    public bool IsArmed { get; set; }

    /// <summary>Default linear output gain for a new track / "Reset to default".</summary>
    public const double DefaultVolume = 0.8;

    /// <summary>Default stereo pan (centred) for a new track / "Reset to default".</summary>
    public const double DefaultPan = 0.0;

    /// <summary>Linear output gain, 0..1.</summary>
    public double Volume { get; set; } = DefaultVolume;

    /// <summary>Stereo pan, -1 (hard left) .. +1 (hard right).</summary>
    public double Pan { get; set; } = DefaultPan;

    /// <summary>Surround width for 5.1 export, 0 (mono/center) .. 1 (full stereo/sides).</summary>
    public double SurroundWidth { get; set; } = 1.0;

    /// <summary>
    /// The track's colour, stored as a palette key (e.g. "CatppuccinMauve") or a "#rrggbb"
    /// hex string. Kept as a string so Core stays free of any UI/Avalonia dependency; the
    /// Desktop layer resolves it to a brush.
    /// </summary>
    public string ColorKey { get; set; } = "CatppuccinMauve";

    /// <summary>The clips placed on this track, ordered loosely by <see cref="Clip.StartBeat"/>.</summary>
    public List<Clip> Clips { get; } = new();

    /// <summary>
    /// The instrument rack for an <see cref="TrackKind.Instrument"/> track: zero or more instruments,
    /// each with its own bypass flag and (pre) effect chain. The track's MIDI drives every enabled slot
    /// simultaneously. UI-facing list — edit, then call <see cref="CommitInstruments"/>.
    /// </summary>
    public List<InstrumentSlot> Instruments { get; } = new();

    private volatile InstrumentSlot[] _activeInstruments = Array.Empty<InstrumentSlot>();

    /// <summary>Lock-free snapshot of the instrument rack read by the audio engine.</summary>
    public InstrumentSlot[] ActiveInstruments => _activeInstruments;

    /// <summary>Publishes the current <see cref="Instruments"/> list to the audio thread.</summary>
    public void CommitInstruments() => _activeInstruments = Instruments.ToArray();

    /// <summary>Instrument/drum rack layout (macros, pad grid).</summary>
    public InstrumentRackSettings Rack { get; set; } = new();

    /// <summary>The first instrument in the rack, or null. Convenience for read-only call sites.</summary>
    public IInstrument? PrimaryInstrument => Instruments.Count > 0 ? Instruments[0].Instrument : null;

    /// <summary>
    /// Transient peak output level (0..1, with release) written by the audio engine each block and
    /// polled by the UI level meter. Not persisted.
    /// </summary>
    public float MeterLevel;

    /// <summary>The track's insert effect chain (UI-facing list). Edit, then call <see cref="CommitEffects"/>.</summary>
    public List<IAudioEffect> Effects { get; } = new();

    private volatile IAudioEffect[] _activeEffects = Array.Empty<IAudioEffect>();

    /// <summary>Lock-free snapshot of the effect chain read by the audio engine.</summary>
    public IAudioEffect[] ActiveEffects => _activeEffects;

    /// <summary>Publishes the current <see cref="Effects"/> list to the audio thread.</summary>
    public void CommitEffects() => _activeEffects = Effects.ToArray();

    /// <summary>MIDI-FX chain (UI-facing). Edit, then call <see cref="CommitMidiEffects"/>.</summary>
    public List<IMidiEffect> MidiEffects { get; } = new();

    private volatile IMidiEffect[] _activeMidiEffects = Array.Empty<IMidiEffect>();

    /// <summary>Lock-free snapshot of the MIDI-FX chain read by the scheduler.</summary>
    public IMidiEffect[] ActiveMidiEffects => _activeMidiEffects;

    /// <summary>Publishes the current <see cref="MidiEffects"/> list to the audio thread.</summary>
    public void CommitMidiEffects() => _activeMidiEffects = MidiEffects.ToArray();

    /// <summary>Automation lanes on this track (UI-facing). Edit, then call <see cref="CommitAutoLanes"/>.</summary>
    public List<AutomationLane> AutoLanes { get; } = new();

    private volatile AutomationLane[] _activeAutoLanes = Array.Empty<AutomationLane>();

    /// <summary>Lock-free snapshot of the automation lanes read by the audio engine.</summary>
    public AutomationLane[] ActiveAutoLanes => _activeAutoLanes;

    /// <summary>Publishes the current <see cref="AutoLanes"/> list to the audio thread.</summary>
    public void CommitAutoLanes() => _activeAutoLanes = AutoLanes.ToArray();

    /// <summary>Track-level modulators (LFO → parameter). Edit, then call <see cref="CommitModulators"/>.</summary>
    public List<TrackModulator> Modulators { get; } = new();

    private volatile TrackModulator[] _activeModulators = Array.Empty<TrackModulator>();

    /// <summary>Lock-free snapshot of modulators read by the audio engine.</summary>
    public TrackModulator[] ActiveModulators => _activeModulators;

    /// <summary>Publishes the current <see cref="Modulators"/> list to the audio thread.</summary>
    public void CommitModulators() => _activeModulators = Modulators.ToArray();

    /// <summary>Transient UI state: whether this track's automation lanes are collapsed in the timeline.</summary>
    public bool AutomationCollapsed { get; set; }

    /// <summary>Transient UI state: whether this group's nested rows (children + automation) are collapsed.</summary>
    public bool GroupCollapsed { get; set; }

    /// <summary>Main output routing target (parent group, master, specific bus, or none).</summary>
    public TrackOutputTarget OutputTarget { get; set; } = TrackOutputTarget.ParentBus;

    /// <summary>When <see cref="OutputTarget"/> is <see cref="TrackOutputTarget.SpecificBus"/>, the destination bus id.</summary>
    public Guid? OutputBusId { get; set; }

    /// <summary>When false, the track is excluded from the master chain (FL-style "route to master" off).</summary>
    public bool RouteToMaster { get; set; } = true;

    /// <summary>Auxiliary sends to return tracks.</summary>
    public List<TrackSend> Sends { get; } = new();

    /// <summary>Comping take lanes (recording alternates).</summary>
    public List<TakeLane> TakeLanes { get; } = new();

    /// <summary>Take lane that receives new recordings; null uses the first armed lane or first lane.</summary>
    public Guid? ActiveTakeLaneId { get; set; }

    /// <summary>
    /// For <see cref="TrackKind.Pattern"/> tracks: the pattern edited when no clip is selected and the
    /// default pattern used when creating new pattern clips on this lane.
    /// </summary>
    public Guid? ActivePatternId { get; set; }

    /// <summary>Optional drum map applied to MIDI notes on this track.</summary>
    public Guid? DrumMapId { get; set; }

    /// <summary>When true the track plays from a pre-rendered freeze buffer instead of live instruments/FX.</summary>
    public bool IsFrozen { get; set; }

    /// <summary>Pre-freeze instruments/FX/clips restored by unfreeze.</summary>
    public FreezeSnapshot? FreezeBackup { get; set; }

    /// <summary>Per-channel surround pan when mixing to 5.1/7.1.</summary>
    public SurroundChannelPan SurroundPan { get; set; } = new();

    /// <summary>When true, MIDI on this track is also sent to the external MIDI output device.</summary>
    public bool RouteToExternalMidi { get; set; }

    /// <summary>MIDI channel (1–16) used when <see cref="RouteToExternalMidi"/> is enabled.</summary>
    public int ExternalMidiChannel { get; set; } = 1;

    /// <summary>Software input monitoring for audio tracks (not persisted).</summary>
    public InputMonitoringMode InputMonitoring { get; set; } = InputMonitoringMode.Auto;
}
