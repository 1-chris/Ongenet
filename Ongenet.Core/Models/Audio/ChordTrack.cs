using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Models.Audio;

/// <summary>Global chord track — harmony suggestions applied to MIDI on selected tracks.</summary>
public sealed class ChordTrack
{
    public bool Enabled { get; set; }
    public List<ChordRegion> Regions { get; } = new();
}

public sealed class ChordRegion
{
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; } = 4;
    public string Symbol { get; set; } = "C";
    public ChordQuality Quality { get; set; } = ChordQuality.Major;
}

public enum ChordQuality
{
    Major,
    Minor,
    Dominant7,
    Major7,
    Minor7
}

/// <summary>Maps MIDI articulation to plugin keyswitches (VST Expression map).</summary>
public sealed class VstExpressionMap
{
    public string Name { get; set; } = "Default";
    public List<ExpressionMapEntry> Entries { get; } = new();
}

public sealed class ExpressionMapEntry
{
    public string Articulation { get; set; } = "Legato";
    public int KeyswitchNote { get; set; } = 36;
    public int CcNumber { get; set; } = -1;
    public int CcValue { get; set; }
}

/// <summary>Monitor/cue mix profiles (Control Room).</summary>
public sealed class ControlRoomProfile
{
    public string Name { get; set; } = "Default";
    public double CueVolume { get; set; } = 1.0;
    public double MainVolume { get; set; } = 1.0;
    public bool DimEnabled { get; set; }
    public double DimAmountDb { get; set; } = -20;
}
