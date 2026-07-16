using System.Collections.Generic;

namespace Ongenet.Core.Persistence.Import;

/// <summary>
/// Format-agnostic intermediate representation produced by DAW parsers before mapping into Ongenet.
/// </summary>
public sealed class ImportDocument
{
    public string Name { get; set; } = "Imported";
    public double TempoBpm { get; set; } = 120;
    public int TimeSigNumerator { get; set; } = 4;
    public int TimeSigDenominator { get; set; } = 4;
    public double Ppq { get; set; } = 96;
    public string? SourceVersion { get; set; }
    public List<ImportTrack> Tracks { get; } = new();
    public List<ImportPattern> Patterns { get; } = new();
    /// <summary>Playlist/arrangement pattern or audio placements (FL playlist, etc.).</summary>
    public List<ImportPlaylistItem> PlaylistItems { get; } = new();
    public List<string> Warnings { get; } = new();
    public Dictionary<string, int> Diagnostics { get; } = new();
}

public enum ImportTrackKind
{
    Audio,
    Instrument,
    Midi,
    Group,
    Return,
    Master
}

public sealed class ImportTrack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Track";
    public ImportTrackKind Kind { get; set; } = ImportTrackKind.Audio;
    public string? ParentId { get; set; }
    /// <summary>FL mixer insert index this channel routes to (0 = master), if known.</summary>
    public int? MixerInsertIndex { get; set; }
    public double Volume { get; set; } = 0.8;
    public double Pan { get; set; }
    public bool Muted { get; set; }
    public bool Soloed { get; set; }
    public string? ColorHex { get; set; }
    public string? SamplePath { get; set; }
    /// <summary>FL Layer child rack indices (event Children / 94), if this track is a Layer.</summary>
    public List<int> ChildChannelIds { get; } = new();
    /// <summary>FL Levels pitch_shift in cents (−4800..+4800).</summary>
    public double PitchCents { get; set; }
    /// <summary>FL sampler root note (MIDI), when known from ChanParams.</summary>
    public int? RootNote { get; set; }
    /// <summary>FL filter cutoff 0..1024 (channel rack), when known.</summary>
    public int? FilterCutoff { get; set; }
    /// <summary>FL filter resonance 0..1024, when known.</summary>
    public int? FilterResonance { get; set; }
    public List<ImportClip> Clips { get; } = new();
    public List<ImportDevice> Devices { get; } = new();
    public List<ImportSend> Sends { get; } = new();
}

public sealed class ImportClip
{
    public string Name { get; set; } = "Clip";
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; } = 4;
    public bool IsAudio { get; set; }
    public string? SamplePath { get; set; }
    public double SourceOffsetSeconds { get; set; }
    public double? SourceLengthSeconds { get; set; }
    public bool StretchToTempo { get; set; }
    public List<ImportNote> Notes { get; } = new();
    public List<ImportWarpMarker> WarpMarkers { get; } = new();
    public string? PatternId { get; set; }
}

public sealed class ImportNote
{
    public int Key { get; set; }
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; } = 0.25;
    public float Velocity { get; set; } = 0.8f;
}

public sealed class ImportWarpMarker
{
    public double BeatTime { get; set; }
    public double SourceSeconds { get; set; }
}

public sealed class ImportDevice
{
    public string Name { get; set; } = "";
    public string? VendorHint { get; set; }
    public bool IsInstrument { get; set; }
    public bool IsThirdParty { get; set; }
    public Dictionary<string, double> Parameters { get; } = new();
}

public sealed class ImportSend
{
    public string TargetTrackId { get; set; } = "";
    public double Level { get; set; } = 0.5;
    public bool Prefader { get; set; }
}

public sealed class ImportPattern
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Pattern";
    public double LengthBeats { get; set; } = 4;
    /// <summary>Notes keyed by channel/track id (e.g. <c>chan:0</c>) for multi-channel FL patterns.</summary>
    public Dictionary<string, List<ImportNote>> NotesByChannel { get; } = new();
    /// <summary>Legacy flat list (Ableton/DAWproject); prefer <see cref="NotesByChannel"/> for FL.</summary>
    public List<ImportNote> Notes { get; } = new();
    public string? ChannelId { get; set; }
}

/// <summary>One arrangement/playlist placement (pattern block or channel audio clip).</summary>
public sealed class ImportPlaylistItem
{
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; } = 4;
    public bool Muted { get; set; }
    public int PlaylistTrackIndex { get; set; }
    public string? PlaylistTrackName { get; set; }
    /// <summary>When set, this is a pattern block.</summary>
    public string? PatternId { get; set; }
    /// <summary>When set, this is a channel/sample clip on that channel track.</summary>
    public string? ChannelId { get; set; }
    public string? SamplePath { get; set; }
    public bool IsAudio { get; set; }
    public double StartOffsetBeats { get; set; }
    public double EndOffsetBeats { get; set; }
}
