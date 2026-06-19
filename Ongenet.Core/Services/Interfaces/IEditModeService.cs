using System;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>Editing modes shared by the timeline and piano roll.</summary>
public enum EditMode
{
    /// <summary>Default: click-drag moves/resizes/creates a single object.</summary>
    Edit,

    /// <summary>Click-drag draws a rubber band to multi-select objects.</summary>
    Select,

    /// <summary>Clicking a clip slices it into two pieces at the grid-snapped pointer position.</summary>
    Slice
}

/// <summary>App-wide current edit mode, shared so the timeline and piano roll behave consistently.</summary>
public interface IEditModeService
{
    EditMode Mode { get; set; }
    event Action? ModeChanged;
}
