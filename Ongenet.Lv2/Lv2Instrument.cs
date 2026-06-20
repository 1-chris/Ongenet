using System;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Lv2;

/// <summary>
/// An LV2 plugin hosted as an Ongenet instrument: MIDI in (forged onto its atom port), audio out
/// (added to the engine buffer). Shared hosting (loading, ports, params) lives in
/// <see cref="Lv2PluginBase"/>.
/// </summary>
public sealed class Lv2Instrument : Lv2PluginBase, IInstrument
{
    public Lv2Instrument(Lv2PluginDescriptor descriptor) : base(descriptor) { }

    string IInstrument.TypeId => MakeId(Descriptor.Uri);

    public void NoteOn(int midiNote, float velocity) => EnqueueNoteOn(midiNote, velocity);
    public void NoteOff(int midiNote) => EnqueueNoteOff(midiNote);
    public void AllNotesOff() => EnqueueAllNotesOff();

    void IInstrument.ControlChange(int controller, int value) => EnqueueControlChange(controller, value);
    void IInstrument.PitchBend(int value14) => EnqueuePitchBend(value14);
    void IInstrument.ChannelAftertouch(int value) => EnqueueAftertouch(value);

    public void Render(Span<float> buffer) => RenderAudio(buffer, feedInput: false, replace: false);

    public IInstrument Clone() => new Lv2Instrument(Descriptor);
}
