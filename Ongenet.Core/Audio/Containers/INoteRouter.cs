namespace Ongenet.Core.Audio.Containers;

/// <summary>
/// Routes incoming MIDI notes to one or more child slots in a container instrument or note-FX device.
/// </summary>
public interface INoteRouter
{
    /// <summary>
    /// Returns indices of child slots that should receive <paramref name="midiNote"/>, or null to
    /// fan out to every enabled child.
    /// </summary>
    int[]? RouteNote(int midiNote, float velocity);
}
