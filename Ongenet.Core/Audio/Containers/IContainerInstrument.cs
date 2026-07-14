using System.Collections.Generic;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Containers;

/// <summary>
/// An instrument that hosts nested instrument slots (each with its own pre-FX chain), like rack
/// Drum Machine, Instrument Layer, or Chain devices.
/// </summary>
public interface IContainerInstrument : IInstrument
{
    /// <summary>Nested instrument slots owned by this container.</summary>
    IReadOnlyList<InstrumentSlot> Children { get; }
}
