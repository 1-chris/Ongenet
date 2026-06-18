using System;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Clap;

/// <summary>
/// A CLAP plugin hosted as an Ongenet instrument: notes in, audio out (added to the engine buffer).
/// Shared hosting (loading, params, GUI) lives in <see cref="ClapPluginBase"/>.
/// </summary>
public sealed class ClapInstrument : ClapPluginBase, IInstrument
{
    public ClapInstrument(string modulePath, string pluginId, string displayName)
        : base(modulePath, pluginId, displayName) { }

    public void NoteOn(int midiNote, float velocity) => EnqueueNoteOn(midiNote, velocity);
    public void NoteOff(int midiNote) => EnqueueNoteOff(midiNote);
    public void AllNotesOff() => EnqueueAllNotesOff();

    public void Render(Span<float> buffer) => RenderAudio(buffer, feedInput: false, replace: false);

    public IInstrument Clone() => new ClapInstrument(ModulePath, PluginId, Name);
}
