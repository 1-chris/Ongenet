using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Core.Audio.Scheduling;

namespace Ongenet.App.Services;

/// <summary>
/// Routes incoming MIDI to transport, session, and mixer actions defined in a
/// <see cref="ControlSurfaceDefinition"/>.
/// </summary>
public sealed class ControlSurfaceRouter
{
    private readonly ITransportService _transport;
    private readonly IRecordingService _recording;
    private readonly IPlaybackModeService _playback;
    private readonly IProjectService _project;
    private readonly IEventAggregator _events;

    private ControlSurfaceDefinition? _definition;
    private int _faderBank;

    public ControlSurfaceRouter(ITransportService transport, IRecordingService recording,
        IPlaybackModeService playback, IProjectService project, IEventAggregator events)
    {
        _transport = transport;
        _recording = recording;
        _playback = playback;
        _project = project;
        _events = events;
    }

    public ControlSurfaceDefinition? ActiveDefinition
    {
        get => _definition;
        set => _definition = value;
    }

    public int FaderBank
    {
        get => _faderBank;
        set => _faderBank = Math.Max(0, value);
    }

    /// <summary>Handles a message when a definition is active. Returns true if consumed.</summary>
    public bool HandleMessage(MidiMessage msg)
    {
        if (_definition is null) return false;

        foreach (var binding in _definition.Bindings)
        {
            if (!Matches(msg, binding)) continue;
            Invoke(binding, msg);
            return true;
        }

        return false;
    }

    private static bool Matches(MidiMessage msg, ControlSurfaceBinding binding)
    {
        switch (msg.Kind)
        {
            case MidiMessageKind.NoteOn when msg.Velocity > 0:
                if (!binding.IsNote) return false;
                break;
            case MidiMessageKind.ControlChange when msg.Value >= 64:
                if (binding.IsNote) return false;
                break;
            case MidiMessageKind.ControlChange:
                if (binding.IsNote || !IsMixerAction(binding.Action)) return false;
                break;
            case MidiMessageKind.PitchBend:
                if (!string.Equals(binding.Action, "MixerVolume", StringComparison.OrdinalIgnoreCase)) return false;
                break;
            default:
                return false;
        }

        if (binding.Number >= 0 && binding.Number != (binding.IsNote ? msg.Note : msg.Controller)) return false;
        if (binding.Channel >= 0 && binding.Channel != msg.Channel) return false;
        return true;
    }

    private void Invoke(ControlSurfaceBinding binding, MidiMessage msg)
    {
        switch (binding.Action.ToLowerInvariant())
        {
            case "playpause":
                if (_transport.State == TransportState.Playing) _transport.Stop();
                else _transport.Play();
                return;
            case "stop":
                _transport.Stop();
                return;
            case "record":
                if (_recording.IsRecording) _recording.StopRecording();
                else _recording.StartRecording();
                return;
            case "launchscene" when binding.SceneIndex is { } scene:
                _playback.LaunchScene(scene);
                return;
            case "stopscene" when binding.SceneIndex is { } stopScene:
                foreach (var clip in _project.Current.SessionClips.Where(c => c.SceneIndex == stopScene))
                    _playback.StopClip(clip.Id);
                return;
            case "stopall":
                _playback.StopAll();
                return;
            case "launchslot":
                if (TryResolveSlot(binding, out var launch))
                    _playback.LaunchClip(launch.Id);
                return;
            case "queueslot":
                if (TryResolveSlot(binding, out var queue))
                    _playback.QueueClip(queue.Id);
                return;
            case "stopslot":
                if (TryResolveSlot(binding, out var stop))
                    _playback.StopClip(stop.Id);
                return;
            case "mixervolume":
                ApplyMixer(binding, msg, isPan: false);
                return;
            case "mixerpan":
                ApplyMixer(binding, msg, isPan: true);
                return;
            case "mixermute":
                ToggleMixerMute(binding, msg);
                return;
            case "mixersolo":
                ToggleMixerSolo(binding, msg);
                return;
            case "mixersend":
                ApplyMixerSend(binding, msg);
                return;
            case "bankprevious":
                ShiftBank(-1);
                return;
            case "banknext":
                ShiftBank(+1);
                return;
        }
    }

    private bool TryResolveSlot(ControlSurfaceBinding binding, out SessionClip clip)
    {
        clip = null!;
        if (binding.SceneIndex is not { } scene) return false;

        var tracks = _project.Current.Tracks.Where(t => t.Kind is TrackKind.Audio or TrackKind.Instrument).ToList();
        if (binding.TrackIndex is { } ti && ti >= 0 && ti < tracks.Count)
        {
            clip = _project.Current.SessionClips
                .FirstOrDefault(c => c.TrackId == tracks[ti].Id && c.SceneIndex == scene)!;
            return clip is not null;
        }

        clip = _project.Current.SessionClips
            .Where(c => c.SceneIndex == scene)
            .OrderBy(c => c.TrackId)
            .FirstOrDefault()!;
        return clip is not null;
    }

    private void ApplyMixer(ControlSurfaceBinding binding, MidiMessage msg, bool isPan)
    {
        var ch = binding.MixerChannel ?? (msg.Channel + 1);
        if (ch is < 1 or > 8) return;

        var track = TracksInBank().ElementAtOrDefault(ch - 1);
        if (track is null) return;

        double normalized;
        if (msg.Kind == MidiMessageKind.PitchBend)
            normalized = msg.PitchBend14 / 16383.0;
        else
            normalized = msg.Value / 127.0;

        if (isPan)
            track.Pan = normalized * 2.0 - 1.0;
        else
            track.Volume = normalized;

        _events.Publish(new TrackChangedEvent(track));
    }

    private void ToggleMixerMute(ControlSurfaceBinding binding, MidiMessage msg)
    {
        if (!IsPress(msg)) return;
        var track = ResolveMixerTrack(binding, msg);
        if (track is null) return;
        track.IsMuted = !track.IsMuted;
        _events.Publish(new TrackChangedEvent(track));
    }

    private void ToggleMixerSolo(ControlSurfaceBinding binding, MidiMessage msg)
    {
        if (!IsPress(msg)) return;
        var track = ResolveMixerTrack(binding, msg);
        if (track is null) return;
        track.IsSoloed = !track.IsSoloed;
        _events.Publish(new TrackChangedEvent(track));
    }

    private void ApplyMixerSend(ControlSurfaceBinding binding, MidiMessage msg)
    {
        var track = ResolveMixerTrack(binding, msg);
        if (track is null) return;

        var sendIndex = binding.MixerTarget is { } target && int.TryParse(target, out var idx) ? idx : 0;
        if (sendIndex < 0 || sendIndex >= track.Sends.Count) return;

        var normalized = msg.Kind == MidiMessageKind.PitchBend
            ? msg.PitchBend14 / 16383.0
            : msg.Value / 127.0;
        track.Sends[sendIndex].Level = normalized;
        _events.Publish(new TrackChangedEvent(track));
    }

    private Track? ResolveMixerTrack(ControlSurfaceBinding binding, MidiMessage msg)
    {
        var ch = binding.MixerChannel ?? (msg.Channel + 1);
        if (ch is < 1 or > 8) return null;
        return TracksInBank().ElementAtOrDefault(ch - 1);
    }

    private static bool IsPress(MidiMessage msg)
        => msg.Kind switch
        {
            MidiMessageKind.NoteOn => msg.Velocity > 0,
            MidiMessageKind.ControlChange => msg.Value >= 64,
            _ => false
        };

    private void ShiftBank(int delta)
    {
        var trackCount = _project.Current.Tracks.Count(t => !t.IsBus);
        var maxBank = Math.Max(0, (trackCount - 1) / 8);
        _faderBank = Math.Clamp(_faderBank + delta, 0, maxBank);
    }

    private List<Track> TracksInBank()
        => _project.Current.Tracks.Where(t => !t.IsBus).Skip(_faderBank * 8).Take(8).ToList();

    private static bool IsMixerAction(string action)
        => action.StartsWith("Mixer", StringComparison.OrdinalIgnoreCase)
           || action.Equals("BankPrevious", StringComparison.OrdinalIgnoreCase)
           || action.Equals("BankNext", StringComparison.OrdinalIgnoreCase);
}
