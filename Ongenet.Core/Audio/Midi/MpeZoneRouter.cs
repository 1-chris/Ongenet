using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Midi;

/// <summary>Routes MPE zone traffic: master-channel notes plus per-note expression on member channels.</summary>
public sealed class MpeZoneRouter
{
    private readonly MpeSettings _settings;
    private readonly Dictionary<int, int> _channelToNote = new();
    private readonly Dictionary<int, int> _noteToChannel = new();
    private int _nextMemberIndex;

    public MpeZoneRouter(MpeSettings settings) => _settings = settings;

    public bool Enabled => _settings.Enabled;

    /// <summary>
    /// Translates an incoming message into zero or more preview actions. Returns false when the caller
    /// should apply its default (non-MPE) routing.
    /// </summary>
    public bool TryRoute(in MidiMessage message, List<MpeRoutedAction> actions)
    {
        actions.Clear();
        if (!_settings.Enabled) return false;

        var master = _settings.MasterChannel - 1;
        if (message.Channel == master) return RouteMaster(message, actions);

        if (IsMemberChannel(message.Channel) && _channelToNote.TryGetValue(message.Channel, out var note))
            return RouteMemberExpression(message, note, actions);

        // Other channels are ignored while MPE is on.
        return true;
    }

    private bool RouteMaster(in MidiMessage message, List<MpeRoutedAction> actions)
    {
        switch (message.Kind)
        {
            case MidiMessageKind.NoteOn when message.Data2 > 0:
                AssignNote(message.Note, message.Velocity, actions);
                return true;
            case MidiMessageKind.NoteOn:
            case MidiMessageKind.NoteOff:
                ReleaseNote(message.Note, actions);
                return true;
            default:
                return false;
        }
    }

    private bool RouteMemberExpression(in MidiMessage message, int note, List<MpeRoutedAction> actions)
    {
        switch (message.Kind)
        {
            case MidiMessageKind.PitchBend:
                actions.Add(new MpeRoutedAction(MpeRoutedActionKind.NotePitchBend, note, Value: message.PitchBend14));
                return true;
            case MidiMessageKind.ChannelAftertouch:
            case MidiMessageKind.PolyAftertouch:
                actions.Add(new MpeRoutedAction(MpeRoutedActionKind.NotePressure, note, Value: message.Pressure));
                return true;
            case MidiMessageKind.ControlChange when message.Controller == 74:
                actions.Add(new MpeRoutedAction(MpeRoutedActionKind.NoteTimbre, note, Value: message.Value));
                return true;
            default:
                return true;
        }
    }

    private void AssignNote(int note, float velocity, List<MpeRoutedAction> actions)
    {
        if (_noteToChannel.TryGetValue(note, out var existing))
        {
            actions.Add(new MpeRoutedAction(MpeRoutedActionKind.NoteOn, note, velocity));
            return;
        }

        var channel = AllocateMemberChannel();
        if (_channelToNote.TryGetValue(channel, out var stolen))
        {
            _noteToChannel.Remove(stolen);
            actions.Add(new MpeRoutedAction(MpeRoutedActionKind.NoteOff, stolen));
        }

        _channelToNote[channel] = note;
        _noteToChannel[note] = channel;
        actions.Add(new MpeRoutedAction(MpeRoutedActionKind.NoteOn, note, velocity));
    }

    private void ReleaseNote(int note, List<MpeRoutedAction> actions)
    {
        if (!_noteToChannel.Remove(note, out var channel)) return;
        _channelToNote.Remove(channel);
        actions.Add(new MpeRoutedAction(MpeRoutedActionKind.NoteOff, note));
    }

    private int AllocateMemberChannel()
    {
        var start = _settings.MemberChannelStart - 1;
        var count = _settings.MemberChannelCount < 1 ? 1 : _settings.MemberChannelCount;
        for (var i = 0; i < count; i++)
        {
            var idx = (_nextMemberIndex + i) % count;
            var channel = start + idx;
            if (!_channelToNote.ContainsKey(channel))
            {
                _nextMemberIndex = (idx + 1) % count;
                return channel;
            }
        }

        var steal = start + (_nextMemberIndex % count);
        _nextMemberIndex = (_nextMemberIndex + 1) % count;
        return steal;
    }

    private bool IsMemberChannel(int channel)
    {
        var start = _settings.MemberChannelStart - 1;
        var end = start + (_settings.MemberChannelCount < 1 ? 1 : _settings.MemberChannelCount);
        return channel >= start && channel < end;
    }
}

public enum MpeRoutedActionKind
{
    NoteOn,
    NoteOff,
    NotePitchBend,
    NotePressure,
    NoteTimbre
}

public readonly record struct MpeRoutedAction(MpeRoutedActionKind Kind, int Note, float Velocity = 0f, int Value = 0);
