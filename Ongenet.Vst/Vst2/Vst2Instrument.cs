using System;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Vst.Vst2;

/// <summary>
/// A VST2 plugin hosted as an Ongenet instrument: notes in (via <c>effProcessEvents</c>), audio out
/// (added to the engine buffer). Shared hosting (loading, params, GUI) lives in
/// <see cref="Vst2PluginBase"/>.
/// </summary>
public sealed class Vst2Instrument : Vst2PluginBase, IInstrument
{
    public Vst2Instrument(string modulePath, string uid, string displayName)
        : base(modulePath, uid, displayName) { }

    string IInstrument.TypeId => MakeId(ModulePath, Uid);

    public void NoteOn(int midiNote, float velocity) => EnqueueNoteOn(midiNote, velocity);
    public void NoteOff(int midiNote) => EnqueueNoteOff(midiNote);
    public void AllNotesOff() => EnqueueAllNotesOff();

    void IInstrument.ControlChange(int controller, int value) => EnqueueControlChange(controller, value);
    void IInstrument.PitchBend(int value14) => EnqueuePitchBend(value14);
    void IInstrument.ChannelAftertouch(int value) => EnqueueAftertouch(value);

    public void Render(Span<float> buffer) => RenderAudio(buffer, feedInput: false, replace: false);

    public IInstrument Clone() => new Vst2Instrument(ModulePath, Uid, Name);
}
