using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;

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

    /// <summary>True for a bus (group or master) that sums child output rather than carrying clips.</summary>
    public bool IsBus => Kind is TrackKind.Group or TrackKind.Master;

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

    /// <summary>Automation lanes on this track (UI-facing). Edit, then call <see cref="CommitAutoLanes"/>.</summary>
    public List<AutomationLane> AutoLanes { get; } = new();

    private volatile AutomationLane[] _activeAutoLanes = Array.Empty<AutomationLane>();

    /// <summary>Lock-free snapshot of the automation lanes read by the audio engine.</summary>
    public AutomationLane[] ActiveAutoLanes => _activeAutoLanes;

    /// <summary>Publishes the current <see cref="AutoLanes"/> list to the audio thread.</summary>
    public void CommitAutoLanes() => _activeAutoLanes = AutoLanes.ToArray();

    /// <summary>Transient UI state: whether this track's automation lanes are collapsed in the timeline.</summary>
    public bool AutomationCollapsed { get; set; }

    /// <summary>Transient UI state: whether this group's nested rows (children + automation) are collapsed.</summary>
    public bool GroupCollapsed { get; set; }
}
