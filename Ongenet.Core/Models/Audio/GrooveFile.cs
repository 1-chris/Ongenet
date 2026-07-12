using System.Collections.Generic;

namespace Ongenet.Core.Models.Audio;

/// <summary>User-imported groove template (.ongenet-groove JSON).</summary>
public sealed class GrooveFile
{
    public string Name { get; set; } = "Groove";
    public double Swing { get; set; }
    public List<GrooveTimingOffset> Offsets { get; } = new();
}

public sealed class GrooveTimingOffset
{
    public int StepIndex { get; set; }
    public double OffsetBeats { get; set; }
}
