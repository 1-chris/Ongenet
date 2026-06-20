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

    /// <summary>Stable registry type id, used to recreate this instrument when loading a project.</summary>
    string TypeId { get; }

    /// <summary>Editable parameters, rendered generically by the instrument inspector.</summary>
    IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>Starts a note. <paramref name="velocity"/> is 0..1.</summary>
    void NoteOn(int midiNote, float velocity);

    /// <summary>Releases a note.</summary>
    void NoteOff(int midiNote);

    /// <summary>Releases all sounding notes (e.g. on stop or track change).</summary>
    void AllNotesOff();

    /// <summary>
    /// A MIDI Continuous Controller change (<paramref name="controller"/> 0..127,
    /// <paramref name="value"/> 0..127). Default no-op; instruments that respond to CCs (e.g. the SFZ
    /// sampler's mod matrix) override it.
    /// </summary>
    void ControlChange(int controller, int value) { }

    /// <summary>A pitch-bend change as a 14-bit value (0..16383, centre 8192). Default no-op.</summary>
    void PitchBend(int value14) { }

    /// <summary>Channel aftertouch (pressure), 0..127. Default no-op.</summary>
    void ChannelAftertouch(int value) { }

    /// <summary>Creates a fresh copy of this instrument with the same parameters (for track duplication).</summary>
    IInstrument Clone();
}
