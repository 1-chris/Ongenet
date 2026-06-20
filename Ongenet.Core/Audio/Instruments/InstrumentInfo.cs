using System;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Describes a built-in instrument type the user can add: a stable id, a display name, a factory
/// that creates a fresh instance, and a category used to group it in the instrument library.
/// </summary>
public sealed record InstrumentInfo(string Id, string DisplayName, Func<IInstrument> Create,
    string Category = "Instruments");
