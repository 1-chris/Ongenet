using System;

namespace Ongenet.Core.Models.Audio;

/// <summary>Where a track's main output is routed (in addition to optional sends).</summary>
public enum TrackOutputTarget
{
    /// <summary>Route to <see cref="Track.ParentId"/> group, or master when unset.</summary>
    ParentBus,

    /// <summary>Route directly to the master bus, bypassing intermediate groups.</summary>
    Master,

    /// <summary>Route to a specific bus by id (<see cref="Track.OutputBusId"/>).</summary>
    SpecificBus,

    /// <summary>Monitor-only: no main output (sends may still be active).</summary>
    None
}
