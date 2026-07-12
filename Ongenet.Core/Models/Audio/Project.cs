using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Music;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Models.Audio;

/// <summary>
/// The top-level document: the set of tracks plus global musical settings.
/// </summary>
public sealed class Project
{
    /// <summary>Display name of the project.</summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>Global tempo.</summary>
    public Tempo Tempo { get; set; } = new(120.0);

    /// <summary>Global time signature.</summary>
    public TimeSignature TimeSignature { get; set; } = TimeSignature.FourFour;

    /// <summary>Length of the arrangement, in bars (the user-set minimum; content may extend it).</summary>
    public int BarCount { get; set; } = 16;

    /// <summary>The tracks in the project, top to bottom (flattened tree order; a group is immediately
    /// followed by its descendants). The single master bus lives here too.</summary>
    public List<Track> Tracks { get; } = new();

    /// <summary>The master bus all audio routes through, or null if one hasn't been created yet.</summary>
    public Track? Master => Tracks.FirstOrDefault(t => t.Kind == TrackKind.Master);

    /// <summary>MIDI-controller mappings ("MIDI learn"): CC → parameter. Managed by the mapping service.</summary>
    public List<MidiMapping> MidiMappings { get; } = new();

    /// <summary>FL-style patterns for the channel rack.</summary>
    public List<Pattern> Patterns { get; } = new();

    /// <summary>Pattern blocks on the playlist.</summary>
    public List<PatternClip> PatternClips { get; } = new();

    /// <summary>Session view clip slots.</summary>
    public List<SessionClip> SessionClips { get; } = new();

    /// <summary>Multi-output plugin routing table.</summary>
    public List<MultiOutputRoute> MultiOutputRoutes { get; } = new();

    public MpeSettings Mpe { get; set; } = new();
    public GrooveTemplate? ActiveGroove { get; set; }
    public List<DrumMap> DrumMaps { get; } = new();
    public List<StepSequence> OrphanStepSequences { get; } = new();

    /// <summary>Video tracks synced to the transport playhead.</summary>
    public List<VideoTrack> VideoTracks { get; } = new();

    /// <summary>Arrangement vs session vs hybrid playback (persisted per project).</summary>
    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.Arrangement;

    /// <summary>Default launch quantize grid for session clips (0 = immediate).</summary>
    public double LaunchQuantizeBeats { get; set; }

    /// <summary>Named arrangement markers for navigation and region export.</summary>
    public List<ArrangementMarker> Markers { get; } = new();

    /// <summary>Ordered marker references used for section-based playback.</summary>
    public List<ArrangementSection> ArrangementSections { get; } = new();

    /// <summary>User-imported groove templates (from .ongenet-groove files).</summary>
    public List<GrooveTemplate> UserGrooves { get; } = new();

    /// <summary>Global key root pitch class (0 = C) for scale-aware editing.</summary>
    public int KeyRootPitchClass { get; set; }

    /// <summary>Global scale/mode used by the piano roll and transport.</summary>
    public ScaleType KeyScale { get; set; } = ScaleType.Major;

    /// <summary>Global chord track for live harmony regions.</summary>
    public ChordTrack ChordTrack { get; set; } = new();

    /// <summary>VST Expression maps for orchestral articulation switching.</summary>
    public List<VstExpressionMap> ExpressionMaps { get; } = new();

    /// <summary>Control Room monitor/cue profiles.</summary>
    public List<ControlRoomProfile> ControlRoomProfiles { get; } = new();
}
