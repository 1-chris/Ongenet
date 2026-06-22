using System;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Vst.Vst3;

/// <summary>
/// A VST3 plugin hosted as an Ongenet instrument: notes in (via the event-input bus), audio out (added
/// to the engine buffer). Shared hosting (component/processor/controller, params, GUI) lives in
/// <see cref="Vst3PluginBase"/>.
/// </summary>
public sealed class Vst3Instrument : Vst3PluginBase, IInstrument
{
    public Vst3Instrument(string modulePath, string uid, string displayName)
        : base(modulePath, uid, displayName) { }

    string IInstrument.TypeId => MakeId(ModulePath, Uid);

    public void NoteOn(int midiNote, float velocity) => EnqueueNoteOn(midiNote, velocity);
    public void NoteOff(int midiNote) => EnqueueNoteOff(midiNote);
    public void AllNotesOff() => EnqueueAllNotesOff();

    public void Render(Span<float> buffer) => RenderAudio(buffer, feedInput: false, replace: false);

    public IInstrument Clone() => new Vst3Instrument(ModulePath, Uid, Name);
}
