using System.Collections.Generic;

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

    /// <summary>The tracks in the project, top to bottom.</summary>
    public List<Track> Tracks { get; } = new();
}
