using System;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Au;

/// <summary>
/// An Audio Unit (a music device / instrument) hosted as an Ongenet instrument: notes in, audio out
/// (added to the engine buffer). Shared hosting (loading, params, GUI) lives in <see cref="AuPluginBase"/>.
/// </summary>
public sealed class AuInstrument : AuPluginBase, IInstrument
{
    public AuInstrument(uint type, uint subType, uint manufacturer, string displayName)
        : base(type, subType, manufacturer, displayName) { }

    protected override bool FeedsInput => false;

    string IInstrument.TypeId => MakeId(Type, SubType, Manufacturer);

    public void NoteOn(int midiNote, float velocity) => EnqueueNoteOn(midiNote, velocity);
    public void NoteOff(int midiNote) => EnqueueNoteOff(midiNote);
    public void AllNotesOff() => EnqueueAllNotesOff();
    void IInstrument.ControlChange(int controller, int value) => EnqueueControlChange(controller, value);
    void IInstrument.PitchBend(int value14) => EnqueuePitchBend(value14);
    void IInstrument.ChannelAftertouch(int value) => EnqueueAftertouch(value);

    public void Render(Span<float> buffer) => RenderAudio(buffer, feedInput: false, replace: false);

    public IInstrument Clone() => new AuInstrument(Type, SubType, Manufacturer, Name);
}
