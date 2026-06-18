using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Catalogue of available instruments. Built-ins are registered at construction; additional
/// instruments (e.g. discovered CLAP plugins) are added at runtime via <see cref="Register"/>.
/// </summary>
public interface IInstrumentRegistry
{
    /// <summary>All available instrument types (built-in + dynamically registered).</summary>
    IReadOnlyList<InstrumentInfo> Available { get; }

    /// <summary>Creates a fresh instrument instance for the given type id.</summary>
    IInstrument Create(string id);

    /// <summary>Adds a dynamically-discovered instrument type (idempotent by id) and raises <see cref="Changed"/>.</summary>
    void Register(InstrumentInfo info);

    /// <summary>Raised when the set of available instruments changes (so the UI can refresh).</summary>
    event Action? Changed;
}
