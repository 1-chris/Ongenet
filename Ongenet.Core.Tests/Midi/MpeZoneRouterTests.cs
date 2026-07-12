using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Tests.Midi;

public sealed class MpeZoneRouterTests
{
    private static MpeSettings DefaultSettings() => new()
    {
        Enabled = true,
        MasterChannel = 1,
        MemberChannelStart = 2,
        MemberChannelCount = 4
    };

    private static MidiMessage NoteOn(int channel, int note, int velocity = 100)
        => new(MidiMessageKind.NoteOn, (byte)(channel - 1), (byte)note, (byte)velocity);

    private static MidiMessage NoteOff(int channel, int note)
        => new(MidiMessageKind.NoteOff, (byte)(channel - 1), (byte)note, 0);

    [Fact]
    public void Master_note_on_routes_preview_note_on()
    {
        var router = new MpeZoneRouter(DefaultSettings());
        var actions = new List<MpeRoutedAction>();

        Assert.True(router.TryRoute(NoteOn(1, 60), actions));
        Assert.Single(actions);
        Assert.Equal(MpeRoutedActionKind.NoteOn, actions[0].Kind);
        Assert.Equal(60, actions[0].Note);
    }

    [Fact]
    public void Member_pitch_bend_targets_active_note()
    {
        var router = new MpeZoneRouter(DefaultSettings());
        var actions = new List<MpeRoutedAction>();

        router.TryRoute(NoteOn(1, 60), actions);
        actions.Clear();

        var bend = new MidiMessage(MidiMessageKind.PitchBend, 1, 0, 64);
        Assert.True(router.TryRoute(bend, actions));
        Assert.Single(actions);
        Assert.Equal(MpeRoutedActionKind.NotePitchBend, actions[0].Kind);
        Assert.Equal(60, actions[0].Note);
        Assert.Equal(bend.PitchBend14, actions[0].Value);
    }

    [Fact]
    public void Member_cc74_routes_timbre_to_active_note()
    {
        var router = new MpeZoneRouter(DefaultSettings());
        var actions = new List<MpeRoutedAction>();

        router.TryRoute(NoteOn(1, 48), actions);
        actions.Clear();

        var timbre = new MidiMessage(MidiMessageKind.ControlChange, 1, 74, 90);
        Assert.True(router.TryRoute(timbre, actions));
        Assert.Single(actions);
        Assert.Equal(MpeRoutedActionKind.NoteTimbre, actions[0].Kind);
        Assert.Equal(48, actions[0].Note);
        Assert.Equal(90, actions[0].Value);
    }

    [Fact]
    public void Master_note_off_releases_note()
    {
        var router = new MpeZoneRouter(DefaultSettings());
        var actions = new List<MpeRoutedAction>();

        router.TryRoute(NoteOn(1, 60), actions);
        actions.Clear();

        Assert.True(router.TryRoute(NoteOff(1, 60), actions));
        Assert.Single(actions);
        Assert.Equal(MpeRoutedActionKind.NoteOff, actions[0].Kind);
        Assert.Equal(60, actions[0].Note);
    }

    [Fact]
    public void Disabled_router_defers_to_default_routing()
    {
        var settings = DefaultSettings();
        settings.Enabled = false;
        var router = new MpeZoneRouter(settings);
        var actions = new List<MpeRoutedAction>();

        Assert.False(router.TryRoute(NoteOn(1, 60), actions));
        Assert.Empty(actions);
    }
}
