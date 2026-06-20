using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Tests.Midi;

public class MidiRunningStatusParserTests
{
    private static List<MidiMessage> Parse(params byte[] bytes)
    {
        var parser = new MidiRunningStatusParser();
        var messages = new List<MidiMessage>();
        parser.Push(bytes, messages.Add);
        return messages;
    }

    [Fact]
    public void NoteOn_then_NoteOff_parsed_with_channel_and_velocity()
    {
        var msgs = Parse(0x92, 60, 100, 0x82, 60, 64);

        Assert.Equal(2, msgs.Count);
        Assert.Equal(MidiMessageKind.NoteOn, msgs[0].Kind);
        Assert.Equal(2, msgs[0].Channel);
        Assert.Equal(60, msgs[0].Note);
        Assert.Equal(100 / 127f, msgs[0].Velocity);
        Assert.Equal(MidiMessageKind.NoteOff, msgs[1].Kind);
        Assert.Equal(60, msgs[1].Note);
    }

    [Fact]
    public void NoteOn_velocity_zero_is_normalized_to_NoteOff()
    {
        var msgs = Parse(0x90, 64, 0);

        Assert.Single(msgs);
        Assert.Equal(MidiMessageKind.NoteOff, msgs[0].Kind);
        Assert.Equal(64, msgs[0].Note);
    }

    [Fact]
    public void RunningStatus_reuses_last_status_for_subsequent_data_pairs()
    {
        // One 0x90 status byte followed by three note-on data pairs.
        var msgs = Parse(0x90, 60, 100, 62, 90, 64, 80);

        Assert.Equal(3, msgs.Count);
        Assert.All(msgs, m => Assert.Equal(MidiMessageKind.NoteOn, m.Kind));
        Assert.Equal(new[] { 60, 62, 64 }, new[] { msgs[0].Note, msgs[1].Note, msgs[2].Note });
    }

    [Fact]
    public void RunningStatus_with_zero_velocity_yields_noteoff_pairs()
    {
        // Common controller idiom: note-on status reused with velocity 0 to release.
        var msgs = Parse(0x90, 60, 100, 60, 0);

        Assert.Equal(2, msgs.Count);
        Assert.Equal(MidiMessageKind.NoteOn, msgs[0].Kind);
        Assert.Equal(MidiMessageKind.NoteOff, msgs[1].Kind);
    }

    [Fact]
    public void PitchBend_combines_two_data_bytes_into_14_bit_value()
    {
        // Centre value 8192 = 0x2000 -> lsb 0x00, msb 0x40.
        var msgs = Parse(0xE0, 0x00, 0x40);

        Assert.Single(msgs);
        Assert.Equal(MidiMessageKind.PitchBend, msgs[0].Kind);
        Assert.Equal(8192, msgs[0].PitchBend14);
    }

    [Fact]
    public void ControlChange_exposes_controller_and_value()
    {
        var msgs = Parse(0xB3, 74, 96);

        Assert.Single(msgs);
        Assert.Equal(MidiMessageKind.ControlChange, msgs[0].Kind);
        Assert.Equal(3, msgs[0].Channel);
        Assert.Equal(74, msgs[0].Controller);
        Assert.Equal(96, msgs[0].Value);
    }

    [Fact]
    public void ProgramChange_takes_a_single_data_byte()
    {
        // Two consecutive program changes via running status (one data byte each).
        var msgs = Parse(0xC0, 5, 7);

        Assert.Equal(2, msgs.Count);
        Assert.Equal(MidiMessageKind.ProgramChange, msgs[0].Kind);
        Assert.Equal(5, msgs[0].Data1);
        Assert.Equal(7, msgs[1].Data1);
    }

    [Fact]
    public void ChannelAftertouch_takes_a_single_data_byte()
    {
        var msgs = Parse(0xD2, 88);

        Assert.Single(msgs);
        Assert.Equal(MidiMessageKind.ChannelAftertouch, msgs[0].Kind);
        Assert.Equal(2, msgs[0].Channel);
        Assert.Equal(88, msgs[0].Pressure);
    }

    [Fact]
    public void RealTime_bytes_interleaved_mid_message_do_not_corrupt_it()
    {
        // A MIDI clock byte (0xF8) lands between the two data bytes of a note-on.
        var msgs = Parse(0x90, 60, 0xF8, 100);

        Assert.Single(msgs);
        Assert.Equal(MidiMessageKind.NoteOn, msgs[0].Kind);
        Assert.Equal(60, msgs[0].Note);
        Assert.Equal(100, msgs[0].Data2);
    }

    [Fact]
    public void SysEx_payload_is_swallowed_and_following_messages_resume()
    {
        var msgs = Parse(0xF0, 0x7E, 0x00, 0x01, 0x02, 0xF7, 0x90, 60, 100);

        Assert.Single(msgs);
        Assert.Equal(MidiMessageKind.NoteOn, msgs[0].Kind);
        Assert.Equal(60, msgs[0].Note);
    }

    [Fact]
    public void StatusByte_during_sysex_aborts_it_and_is_parsed()
    {
        // No 0xF7 terminator: a fresh channel status byte must abort the SysEx and start a new message.
        var msgs = Parse(0xF0, 0x10, 0x20, 0x90, 62, 70);

        Assert.Single(msgs);
        Assert.Equal(MidiMessageKind.NoteOn, msgs[0].Kind);
        Assert.Equal(62, msgs[0].Note);
    }

    [Fact]
    public void State_persists_across_separate_push_calls()
    {
        var parser = new MidiRunningStatusParser();
        var msgs = new List<MidiMessage>();

        parser.Push(new byte[] { 0x90, 60 }, msgs.Add); // status + first data byte only
        Assert.Empty(msgs);

        parser.Push(new byte[] { 100 }, msgs.Add);       // completing data byte arrives later
        Assert.Single(msgs);
        Assert.Equal(MidiMessageKind.NoteOn, msgs[0].Kind);
        Assert.Equal(60, msgs[0].Note);
        Assert.Equal(100, msgs[0].Data2);
    }

    [Fact]
    public void Leading_data_bytes_without_status_are_ignored()
    {
        var msgs = Parse(40, 41, 0x90, 60, 100);

        Assert.Single(msgs);
        Assert.Equal(60, msgs[0].Note);
    }
}
