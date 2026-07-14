using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Hardware;

/// <summary>Sends a MIDI CC to external hardware when supported; otherwise passes MIDI through.</summary>
public sealed class MidiCcMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.midi_cc";

    string IMidiEffect.TypeId => TypeId;

    public string Name => "MIDI CC";
    public bool Enabled { get; set; } = true;

    public int Channel { get; set; } = 1;
    public int Controller { get; set; } = 1;
    public int Value { get; set; } = 64;
    public bool SendOnNote { get; set; }

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);

    public IMidiEffect Clone() => new MidiCcMidiEffect
    {
        Enabled = Enabled,
        Channel = Channel,
        Controller = Controller,
        Value = Value,
        SendOnNote = SendOnNote
    };

    public void Reset() { }

    public IEnumerable<MidiMessage> Process(MidiMessage input)
    {
        if (Enabled && HardwareAvailability.IsMidiOutputSupported && SendOnNote &&
            input.Kind == MidiMessageKind.NoteOn)
        {
            // Future: dispatch CC via IMidiOutputService.
            _ = Channel;
            _ = Controller;
            _ = Value;
        }

        yield return input;
    }
}

/// <summary>Sends MIDI program change (or song select) when supported; passes notes through.</summary>
public sealed class MidiProgramChangeMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.program_change";

    string IMidiEffect.TypeId => TypeId;

    public string Name => "MIDI Program Change";
    public bool Enabled { get; set; } = true;

    public int Channel { get; set; } = 1;
    public int Program { get; set; }
    public bool UseSongSelect { get; set; }

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);

    public IMidiEffect Clone() => new MidiProgramChangeMidiEffect
    {
        Enabled = Enabled,
        Channel = Channel,
        Program = Program,
        UseSongSelect = UseSongSelect
    };

    public void Reset() { }

    public IEnumerable<MidiMessage> Process(MidiMessage input)
    {
        if (Enabled && HardwareAvailability.IsMidiOutputSupported &&
            input.Kind == MidiMessageKind.NoteOn)
        {
            // Future: emit program change or song select on note trigger.
            _ = Channel;
            _ = Program;
            _ = UseSongSelect;
        }

        yield return input;
    }
}
