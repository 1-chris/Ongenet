namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Per-voice note state owned by the runtime and read by note-source nodes (Pitch / Gate / Velocity).
/// For the effect use-case there is a single voice whose <see cref="Active"/> stays true and which
/// carries no note.
/// </summary>
public sealed class FieldVoiceState
{
    /// <summary>Whether this voice slot is currently in use.</summary>
    public bool Active;

    /// <summary>MIDI note number (valid while <see cref="Active"/>).</summary>
    public int Note;

    /// <summary>Note velocity, 0..1.</summary>
    public float Velocity;

    /// <summary>True while the note is held (before note-off).</summary>
    public bool Gate;

    /// <summary>Frequency in Hz for <see cref="Note"/>, including the global pitch-bend offset.</summary>
    public double Frequency;

    /// <summary>Monotonic allocation order, used for oldest-voice stealing.</summary>
    public uint StartOrder;

    /// <summary>Consecutive near-silent blocks observed after release; used to free the voice.</summary>
    public int SilentBlocks;

    public void Reset()
    {
        Active = false;
        Note = 0;
        Velocity = 0;
        Gate = false;
        Frequency = 0;
        SilentBlocks = 0;
    }
}
