using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Opt-in interface for insert effects that respond to MIDI (notes/CC/pitch-bend) — e.g. a stutter
/// effect whose gestures are triggered by keys. The engine delivers messages from two sources:
/// the clip sequencer (on the audio thread, for MIDI clips on the effect's track) and live input
/// (on a background MIDI/UI thread, for the selected track). Because delivery can therefore race the
/// audio thread's <see cref="IAudioEffect.Process"/>, implementers MUST be thread-safe — the standard
/// pattern is to push into a <see cref="MidiEventFifo"/> and drain it at the start of Process.
/// Effects that don't need MIDI simply don't implement this.
/// </summary>
public interface IMidiAwareEffect
{
    /// <summary>Delivers one MIDI message. May be called from any thread; must not block.</summary>
    void HandleMidi(in MidiMessage message);

    /// <summary>Releases all sounding state (transport stop, loop wrap, track change). Thread-safe.</summary>
    void AllNotesOff();
}
