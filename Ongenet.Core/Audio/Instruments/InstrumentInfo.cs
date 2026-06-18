using System;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Describes a built-in instrument type the user can add: a stable id, a display name, and a
/// factory that creates a fresh instance.
/// </summary>
public sealed record InstrumentInfo(string Id, string DisplayName, Func<IInstrument> Create);
