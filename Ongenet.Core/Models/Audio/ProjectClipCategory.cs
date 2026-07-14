using System;
using System.Collections.Generic;

namespace Ongenet.Core.Models.Audio;

/// <summary>How the Project Clips sidebar groups/sorts unique clips.</summary>
public enum ProjectClipsSortMode
{
    ByKind = 0,
    ByColour = 1,
    ByTrack = 2
}

/// <summary>User-owned category of project clips, keyed by content signature.</summary>
public sealed class ProjectClipCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<string> ClipKeys { get; } = new();
}
