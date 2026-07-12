using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Services;

/// <summary>Mackie Control Universal / HUI transport and mixer mapping with 8-strip bank switching.</summary>
public sealed class ControlSurfaceService
{
    private readonly IMidiInputService _input;
    private readonly IMidiOutputService _output;
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IAppSettingsService _settings;
    private readonly IEventAggregator _events;
    private readonly ControlSurfaceLibrary _library;
    private readonly ControlSurfaceRouter _router;
    private long _lastFeedbackMs;
    private int _faderBank;
    private (int MixerChannel, string Target)? _learnTarget;

    public event Action? LearnStateChanged;

    /// <summary>Active CC learn target, if any.</summary>
    public (int MixerChannel, string Target)? LearnTarget => _learnTarget;

    public ControlSurfaceService(IMidiInputService input, IMidiOutputService output, IProjectService project,
        ITransportService transport, IAppSettingsService settings, IEventAggregator events, IPlaybackClock clock,
        ControlSurfaceLibrary library, ControlSurfaceRouter router)
    {
        _input = input;
        _output = output;
        _project = project;
        _transport = transport;
        _settings = settings;
        _events = events;
        _library = library;
        _router = router;
        _library.Rescan();
        ApplyActiveDefinition();
        _input.MessageReceived += OnMessage;
        _input.EnabledDevicesChanged += OnDevicesChanged;
        _transport.StateChanged += SendTransportFeedback;
        _events.Subscribe<TrackChangedEvent>(e => SendTrackFeedback(e.Track));
        clock.Tick += () =>
        {
            var now = Environment.TickCount64;
            if (now - _lastFeedbackMs < 250) return;
            _lastFeedbackMs = now;
            SendTransportFeedback(_transport.State);
            SendMixerBankFeedback();
        };
    }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Active controller definition id; null/empty uses legacy MCU/HUI profile behavior.</summary>
    public string? DefinitionId
    {
        get => _settings.Current.ControlSurfaceDefinitionId;
        set
        {
            _settings.Current.ControlSurfaceDefinitionId = string.IsNullOrWhiteSpace(value) ? null : value;
            _faderBank = 0;
            ApplyActiveDefinition();
            EnsureDefaultMixerMappings(LegacyProfile);
            _settings.CaptureAndSave();
        }
    }

    /// <summary>Legacy profile enum; used when no JSON definition is selected.</summary>
    public ControlSurfaceProfile? LegacyProfile
    {
        get => ParseProfile(_settings.Current.ControlSurfaceProfile);
        set
        {
            _settings.Current.ControlSurfaceProfile = value?.ToString();
            _faderBank = 0;
            ApplyActiveDefinition();
            EnsureDefaultMixerMappings(value);
            _settings.CaptureAndSave();
        }
    }

    /// <summary>Active profile; null settings value keeps legacy MCU + Launchpad behavior.</summary>
    [Obsolete("Use DefinitionId and ControlSurfaceLibrary instead.")]
    public ControlSurfaceProfile? Profile
    {
        get => LegacyProfile;
        set => LegacyProfile = value;
    }

    public IReadOnlyList<ControlSurfaceDefinition> AvailableDefinitions => _library.Definitions;

    private static ControlSurfaceProfile? ParseProfile(string? value)
        => Enum.TryParse<ControlSurfaceProfile>(value, out var profile) ? profile : null;

    private bool UsesJsonDefinition => _router.ActiveDefinition is not null;

    private bool UsesLegacyCombined => !UsesJsonDefinition && LegacyProfile is null;

    private bool UsesMcuFamily => !UsesJsonDefinition && (UsesLegacyCombined
        || LegacyProfile is ControlSurfaceProfile.McuTransport or ControlSurfaceProfile.McuMixer);
    private bool UsesHuiFamily => !UsesJsonDefinition && LegacyProfile is ControlSurfaceProfile.HuiTransport
        or ControlSurfaceProfile.HuiMixer;
    private bool UsesMixerProfile => !UsesJsonDefinition && LegacyProfile is ControlSurfaceProfile.Push2
        or ControlSurfaceProfile.Apc40 or ControlSurfaceProfile.McuMixer or ControlSurfaceProfile.HuiMixer;

    private void OnDevicesChanged()
    {
        if (!string.IsNullOrEmpty(_settings.Current.ControlSurfaceDefinitionId)) return;
        var match = _library.MatchDevice(_input.EnabledDevices.Select(d => d.DisplayName));
        if (match is not null)
        {
            _settings.Current.ControlSurfaceDefinitionId = match.Id;
            ApplyActiveDefinition();
        }
    }

    private void ApplyActiveDefinition()
    {
        var id = _settings.Current.ControlSurfaceDefinitionId;
        _router.ActiveDefinition = _library.FindById(id)
            ?? _library.MatchDevice(_input.EnabledDevices.Select(d => d.DisplayName));
        _router.FaderBank = _faderBank;
    }

    private void SendTransportFeedback(TransportState state)
    {
        var playing = state == TransportState.Playing;
        if (UsesMcuFamily)
        {
            _output.SendControlChange(1, 102, playing ? 127 : 0);
            _output.SendControlChange(1, 105, playing ? 0 : 127);
        }
        if (UsesHuiFamily)
        {
            _output.SendNote(1, 89, playing, 127);
            _output.SendNote(1, 88, !playing, 127);
        }
    }

    private void SendTrackFeedback(Track changed)
    {
        if (!UsesMixerProfile) return;
        var tracks = TracksInBank();
        var index = tracks.IndexOf(changed);
        if (index < 0) return;

        var midiChannel = index + 1;
        if (LegacyProfile is ControlSurfaceProfile.McuMixer or ControlSurfaceProfile.HuiMixer)
        {
            SendFaderFeedback(midiChannel, changed.Volume);
            return;
        }

        foreach (var mapping in GetMappingsForProfile(LegacyProfile!.Value).Where(m => m.MixerChannel == midiChannel))
        {
            var normalized = string.Equals(mapping.Target, "Pan", StringComparison.OrdinalIgnoreCase)
                ? (changed.Pan + 1.0) * 0.5
                : changed.Volume;
            _output.SendControlChange(midiChannel, mapping.CcNumber,
                (int)Math.Round(Math.Clamp(normalized, 0, 1) * 127));
        }
    }

    private void SendMixerBankFeedback()
    {
        if (LegacyProfile is not (ControlSurfaceProfile.McuMixer or ControlSurfaceProfile.HuiMixer)) return;
        var tracks = TracksInBank();
        for (var i = 0; i < 8; i++)
        {
            var ch = i + 1;
            if (i < tracks.Count)
                SendFaderFeedback(ch, tracks[i].Volume);
            else
                SendFaderFeedback(ch, 0);
        }
    }

    private void SendFaderFeedback(int midiChannel, double volume)
    {
        var value = (int)Math.Round(Math.Clamp(volume, 0, 1) * 16383);
        var lsb = value & 0x7F;
        var msb = (value >> 7) & 0x7F;
        _output.SendRaw((byte)(0xE0 + midiChannel - 1), (byte)lsb, (byte)msb);
    }

    private void OnMessage(MidiMessage msg)
    {
        if (!IsEnabled) return;

        if (TryCompleteLearn(msg)) return;

        if (UsesJsonDefinition && _router.HandleMessage(msg)) return;

        if (UsesMcuFamily)
        {
            HandleMcuTransport(msg);
            if (LegacyProfile == ControlSurfaceProfile.McuMixer)
                HandleMcuMixer(msg);
        }

        if (UsesHuiFamily)
        {
            HandleHuiTransport(msg);
            if (LegacyProfile == ControlSurfaceProfile.HuiMixer)
                HandleHuiMixer(msg);
        }

        if (LegacyProfile is ControlSurfaceProfile.Push2 or ControlSurfaceProfile.Apc40)
            HandleMixerCc(msg);
    }

    private void HandleMcuTransport(MidiMessage msg)
    {
        if (msg.Kind != MidiMessageKind.ControlChange) return;

        switch (msg.Data1)
        {
            case 102 when msg.Data2 >= 64:
                if (_transport.State != TransportState.Playing) _transport.Play();
                break;
            case 105 when msg.Data2 >= 64:
                _transport.Stop();
                break;
        }
    }

    private void HandleHuiTransport(MidiMessage msg)
    {
        if (msg.Kind != MidiMessageKind.NoteOn || msg.Velocity <= 0) return;

        switch (msg.Data1)
        {
            case 89:
                if (_transport.State != TransportState.Playing) _transport.Play();
                break;
            case 88:
                _transport.Stop();
                break;
        }
    }

    private void HandleMcuMixer(MidiMessage msg)
    {
        if (msg.Kind == MidiMessageKind.NoteOn && msg.Velocity > 0 && msg.Channel == 0)
        {
            switch (msg.Data1)
            {
                case >= 8 and <= 15:
                    ToggleSolo(msg.Data1 - 8);
                    return;
                case >= 16 and <= 23:
                    ToggleMute(msg.Data1 - 16);
                    return;
                case 46: ShiftBank(-1); return;
                case 47: ShiftBank(+1); return;
            }
        }

        if (msg.Kind == MidiMessageKind.PitchBend && msg.Channel is >= 0 and <= 7)
        {
            ApplyFader(msg.Channel, msg.PitchBend14 / 16383.0);
            return;
        }

        if (msg.Kind == MidiMessageKind.ControlChange && msg.Channel is >= 0 and <= 7)
        {
            if (msg.Data1 == 16)
                ApplyPan(msg.Channel, msg.Data2 / 127.0);
            else if (msg.Data1 == 91)
                ApplySend(msg.Channel, 0, msg.Data2 / 127.0);
        }
    }

    private void HandleHuiMixer(MidiMessage msg)
    {
        if (msg.Kind == MidiMessageKind.NoteOn && msg.Velocity > 0 && msg.Channel == 0)
        {
            switch (msg.Data1)
            {
                case 53: ShiftBank(-1); return;
                case 54: ShiftBank(+1); return;
                case >= 48 and <= 55:
                    ToggleMute(msg.Data1 - 48);
                    return;
                case >= 56 and <= 63:
                    ToggleSolo(msg.Data1 - 56);
                    return;
            }
        }

        if (msg.Kind == MidiMessageKind.PitchBend && msg.Channel is >= 0 and <= 7)
            ApplyFader(msg.Channel, msg.PitchBend14 / 16383.0);
    }

    private void HandleMixerCc(MidiMessage msg)
    {
        if (msg.Kind != MidiMessageKind.ControlChange || LegacyProfile is not { } profile) return;

        var midiChannel = msg.Channel + 1;
        if (midiChannel is < 1 or > 8) return;

        var mappings = GetMappingsForProfile(profile);
        var mapping = mappings.FirstOrDefault(m =>
            m.MixerChannel == midiChannel && m.CcNumber == msg.Data1);
        if (mapping is null) return;

        var tracks = TracksInBank();
        var track = tracks.ElementAtOrDefault(midiChannel - 1);
        if (track is null) return;

        var value = msg.Data2 / 127.0;
        switch (mapping.Target.ToUpperInvariant())
        {
            case "PAN":
                track.Pan = value * 2.0 - 1.0;
                break;
            case "MUTE":
                track.IsMuted = value >= 0.5;
                break;
            case "SOLO":
                track.IsSoloed = value >= 0.5;
                break;
            case "SEND":
                ApplySendToTrack(track, 0, value);
                break;
            default:
                track.Volume = value;
                break;
        }

        _events.Publish(new TrackChangedEvent(track));
    }

    private void ShiftBank(int delta)
    {
        var trackCount = _project.Current.Tracks.Count(t => !t.IsBus);
        var maxBank = Math.Max(0, (trackCount - 1) / 8);
        _faderBank = Math.Clamp(_faderBank + delta, 0, maxBank);
        _router.FaderBank = _faderBank;
        SendMixerBankFeedback();
    }

    private List<Track> TracksInBank()
        => _project.Current.Tracks.Where(t => !t.IsBus).Skip(_faderBank * 8).Take(8).ToList();

    private void ApplyFader(int channelIndex, double volume)
    {
        var track = TracksInBank().ElementAtOrDefault(channelIndex);
        if (track is null) return;
        track.Volume = volume;
        _events.Publish(new TrackChangedEvent(track));
    }

    private void ApplyPan(int channelIndex, double normalized)
    {
        var track = TracksInBank().ElementAtOrDefault(channelIndex);
        if (track is null) return;
        track.Pan = normalized * 2.0 - 1.0;
        _events.Publish(new TrackChangedEvent(track));
    }

    private void ToggleMute(int channelIndex)
    {
        var track = TracksInBank().ElementAtOrDefault(channelIndex);
        if (track is null) return;
        track.IsMuted = !track.IsMuted;
        _events.Publish(new TrackChangedEvent(track));
    }

    private void ToggleSolo(int channelIndex)
    {
        var track = TracksInBank().ElementAtOrDefault(channelIndex);
        if (track is null) return;
        track.IsSoloed = !track.IsSoloed;
        _events.Publish(new TrackChangedEvent(track));
    }

    private void ApplySend(int channelIndex, int sendIndex, double level)
    {
        var track = TracksInBank().ElementAtOrDefault(channelIndex);
        if (track is null) return;
        ApplySendToTrack(track, sendIndex, level);
        _events.Publish(new TrackChangedEvent(track));
    }

    private static void ApplySendToTrack(Track track, int sendIndex, double level)
    {
        if (sendIndex < 0 || sendIndex >= track.Sends.Count) return;
        track.Sends[sendIndex].Level = level;
    }

    /// <summary>Stores a learned CC mapping for the active mixer profile.</summary>
    public void BeginLearn(int mixerChannel, string target)
    {
        if (LegacyProfile is not (ControlSurfaceProfile.Push2 or ControlSurfaceProfile.Apc40)) return;
        _learnTarget = (mixerChannel, target);
        LearnStateChanged?.Invoke();
    }

    public void LearnMapping(int mixerChannel, int ccNumber, string target)
    {
        if (LegacyProfile is not (ControlSurfaceProfile.Push2 or ControlSurfaceProfile.Apc40)) return;
        var profile = LegacyProfile.Value.ToString();
        _settings.Current.ControlSurfaceMappings.RemoveAll(m =>
            m.Profile == profile && m.MixerChannel == mixerChannel && m.Target == target);
        _settings.Current.ControlSurfaceMappings.Add(new ControlSurfaceMappingDto
        {
            Profile = profile,
            MixerChannel = mixerChannel,
            CcNumber = ccNumber,
            Target = target
        });
        _settings.CaptureAndSave();
        LearnStateChanged?.Invoke();
    }

    private bool TryCompleteLearn(MidiMessage msg)
    {
        if (_learnTarget is not { } learn) return false;
        if (msg.Kind != MidiMessageKind.ControlChange) return false;

        LearnMapping(learn.MixerChannel, msg.Data1, learn.Target);
        _learnTarget = null;
        LearnStateChanged?.Invoke();
        return true;
    }

    private IReadOnlyList<ControlSurfaceMappingDto> GetMappingsForProfile(ControlSurfaceProfile profile)
    {
        var key = profile.ToString();
        var custom = _settings.Current.ControlSurfaceMappings.Where(m => m.Profile == key).ToList();
        if (custom.Count > 0) return custom;
        return DefaultMixerMappings(profile).ToList();
    }

    private void EnsureDefaultMixerMappings(ControlSurfaceProfile? profile)
    {
        if (profile is not (ControlSurfaceProfile.Push2 or ControlSurfaceProfile.Apc40)) return;
        var key = profile.Value.ToString();
        if (_settings.Current.ControlSurfaceMappings.Any(m => m.Profile == key)) return;
        foreach (var m in DefaultMixerMappings(profile.Value))
            _settings.Current.ControlSurfaceMappings.Add(m);
    }

    private static IEnumerable<ControlSurfaceMappingDto> DefaultMixerMappings(ControlSurfaceProfile profile)
    {
        var key = profile.ToString();
        for (var ch = 1; ch <= 8; ch++)
        {
            yield return new ControlSurfaceMappingDto { Profile = key, MixerChannel = ch, CcNumber = 7, Target = "Volume" };
            yield return new ControlSurfaceMappingDto { Profile = key, MixerChannel = ch, CcNumber = 10, Target = "Pan" };
            yield return new ControlSurfaceMappingDto { Profile = key, MixerChannel = ch, CcNumber = 91, Target = "Send" };
        }
    }

    /// <summary>Refreshes the definition catalog (after import).</summary>
    public void RescanDefinitions()
    {
        _library.Rescan();
        ApplyActiveDefinition();
    }
}
