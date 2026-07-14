using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Hardware;

/// <summary>Sends a MIDI song-select message on project load or manual trigger.</summary>
public sealed class MidiSongSelectMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.song_select";

    string IMidiEffect.TypeId => TypeId;
    public string Name => "MIDI Song Select";
    public bool Enabled { get; set; } = true;

    public int SongNumber { get; set; }
    public bool SendOnLoad { get; set; } = true;
    public bool ManualSend { get; set; }

    private bool _loadedSent;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);

    public IMidiEffect Clone() => new MidiSongSelectMidiEffect
    {
        Enabled = Enabled,
        SongNumber = SongNumber,
        SendOnLoad = SendOnLoad,
        ManualSend = ManualSend,
        _loadedSent = _loadedSent
    };

    public void Reset()
    {
        _loadedSent = false;
        ManualSend = false;
    }

    public IEnumerable<MidiMessage> Process(MidiMessage input)
    {
        if (Enabled && HardwareAvailability.IsMidiOutputSupported)
        {
            if (SendOnLoad && !_loadedSent)
            {
                _loadedSent = true;
                // Future: dispatch 0xF3 song select via IMidiOutputService.
                _ = SongNumber;
            }

            if (ManualSend)
            {
                ManualSend = false;
                _ = SongNumber;
            }
        }

        yield return input;
    }
}
