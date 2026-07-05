namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Implemented by nodes that manage their own polyphony from the raw note stream rather than the Field
/// voice model — chiefly the whole-instrument module wrapper. The runtime forwards every note event to
/// these nodes in addition to updating the per-voice state used by the primitive note-source nodes.
/// </summary>
public interface IFieldNoteReceiver
{
    void NoteOn(int midiNote, float velocity);
    void NoteOff(int midiNote);
    void AllNotesOff();
    void PitchBend(double semitones);
}
