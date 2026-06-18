using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A playable instrument: an audio source (it renders into the mix) that responds to note
/// on/off events. The base building block of the built-in instrument framework.
/// </summary>
public interface IInstrument : ISampleSource
{
    /// <summary>Display name of the instrument.</summary>
    string Name { get; }

    /// <summary>Editable parameters, rendered generically by the instrument inspector.</summary>
    IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>Starts a note. <paramref name="velocity"/> is 0..1.</summary>
    void NoteOn(int midiNote, float velocity);

    /// <summary>Releases a note.</summary>
    void NoteOff(int midiNote);

    /// <summary>Releases all sounding notes (e.g. on stop or track change).</summary>
    void AllNotesOff();

    /// <summary>Creates a fresh copy of this instrument with the same parameters (for track duplication).</summary>
    IInstrument Clone();
}
