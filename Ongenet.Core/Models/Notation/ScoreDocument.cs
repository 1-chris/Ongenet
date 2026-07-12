using System;
using System.Collections.Generic;

namespace Ongenet.Core.Models.Notation;

/// <summary>How the notation view lays out staves and chord symbols.</summary>
public enum ScoreLayoutMode
{
    /// <summary>Lead sheet: chord symbols prominent, simplified note display.</summary>
    LeadSheet,

    /// <summary>Full score: detailed note heads with chord symbols above the staff.</summary>
    FullScore
}

/// <summary>Lightweight score representation for notation view + MusicXML export.</summary>
public sealed class ScoreDocument
{
    public List<ScoreStaff> Staves { get; } = new();
    public List<ScorePart> Parts { get; } = new();
    public List<ScoreTuplet> Tuplets { get; } = new();
    public int Divisions { get; set; } = 480;
    public string Title { get; set; } = "";
    public ScoreLayoutMode LayoutMode { get; set; } = ScoreLayoutMode.FullScore;
}

/// <summary>A named part grouping one or more staves (e.g. "Piano", "Strings").</summary>
public sealed class ScorePart
{
    public string Name { get; set; } = "";
    public List<ScoreStaff> Staves { get; } = new();
}

/// <summary>Common articulation markings on a note.</summary>
public enum ScoreArticulation
{
    None,
    Staccato,
    Legato,
    Accent,
    Tenuto,
    Marcato
}

/// <summary>Dynamic markings (velocity hints for playback/export).</summary>
public enum ScoreDynamic
{
    None,
    Ppp,
    Pp,
    P,
    Mp,
    Mf,
    F,
    Ff,
    Fff
}

/// <summary>Irregular rhythm grouping (e.g. triplet = 3 notes in the space of 2).</summary>
public sealed class ScoreTuplet
{
    public int ActualNotes { get; set; } = 3;
    public int NormalNotes { get; set; } = 2;
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; }
}

/// <summary>Chord symbol anchored at a measure (or beat) on a staff.</summary>
public sealed class ScoreChordSymbol
{
    public double StartBeat { get; set; }
    public int MeasureNumber { get; set; } = 1;
    public string Text { get; set; } = "";
}

public sealed class ScoreStaff
{
    public Guid TrackId { get; set; }
    public string Clef { get; set; } = "treble";
    public List<ScoreNote> Notes { get; } = new();
    public List<ScoreChordSymbol> ChordSymbols { get; } = new();
}

public sealed class ScoreNote
{
    public int Pitch { get; set; }
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; }
    public int Velocity { get; set; } = 100;
    public ScoreArticulation Articulation { get; set; }
    public ScoreDynamic Dynamic { get; set; }
}
