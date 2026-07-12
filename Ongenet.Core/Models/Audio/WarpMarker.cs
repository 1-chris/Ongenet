using System;
using System.Collections.Generic;

namespace Ongenet.Core.Models.Audio;

/// <summary>Warp marker for time-stretching audio clips (Ableton-style).</summary>
public sealed class WarpMarker
{
    public double SourceSeconds { get; set; }
    public double BeatPosition { get; set; }
}

public enum WarpMode
{
    Beats,
    Tones,
    Texture,
    Repitch,
    Complex
}
