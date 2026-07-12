using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Core.Audio.Scheduling;

namespace Ongenet.Core.Audio;

/// <summary>
/// Default <see cref="IAudioEngine"/>. Mixes the project per track: each track renders its content
/// (an instrument's voices, or its audio clips while playing) into its own scratch buffer, then a
/// single strip pass applies volume, constant-power pan, mute and solo while measuring that track's
/// output level. Instruments render every block (audible for live play); audio clips render only
/// while the transport is playing, sampled at the playhead. While playing, a sample-accurate
/// sequencer fires MIDI clip notes into the instruments. Per-track and master peak levels (with
/// release ballistics) are published for the UI meters.
///
/// <para><b>Performance.</b> Each block runs in two phases. The RENDER phase — instruments, slot
/// effects and insert chains, by far the bulk of the CPU — fans out across cores via
/// <see cref="AudioWorkerPool"/>: every content track renders into its own buffer, touching only its
/// own DSP state, so no locks are needed. The MIXDOWN phase (sidechain publishes, strip gains, bus
/// summing) then runs serially on the audio thread in deterministic order. Sidechain consumers
/// therefore always read the previous block's published signal — a fixed one-block latency,
/// inaudible for ducking, and more consistent than the old order-dependent behaviour. Tracks whose
/// content has been silent longer than the longest plausible effect tail go DORMANT: their insert
/// chains are skipped entirely, so a big project only pays for the tracks actually sounding.</para>
/// </summary>
public sealed class AudioEngine : IAudioEngine
{
    private const float MeterRelease = 0.92f; // per-block decay → ~0.5s fall

    // How long a track's content must stay silent before its effect chain stops being processed —
    // generous enough for the longest delay/reverb tails to ring out fully first.
    private const double DormancyTailSeconds = 12.0;
    private const float SilenceThreshold = 1e-6f;
    private const int LookaheadBlocks = 2;

    private readonly IAudioOutput _output;
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IPlaybackModeService _playback;
    private readonly IAuditionPlayer _audition;
    private readonly IMidiOutputService _midiOut;
    private readonly IInputMonitorService _inputMonitor;

    private volatile Track[] _tracks = Array.Empty<Track>();

    private readonly ArrangementScheduler _arrangementScheduler = new();
    private readonly PatternScheduler _patternScheduler = new();

    private volatile bool _playing;
    private ScheduledNote[] _events = Array.Empty<ScheduledNote>();
    private ScheduledControlChange[] _ccEvents = Array.Empty<ScheduledControlChange>();
    private TrackActivityMap _trackActivity = TrackActivityMap.Empty;
    private AudioClipPlayback[] _audioClips = Array.Empty<AudioClipPlayback>();
    private readonly List<ScheduledNote> _active = new();
    private int _nextEvent;
    private int _nextCcEvent;
    private double _currentBeat;
    private double _samplesPerBeat = 1;
    private double _arrangementEndBeat; // for whole-song auto-loop (snapshot at playback start)

    // --- Bus routing (groups + master). Rebuilt whenever the track topology changes; published as a
    //     single immutable snapshot so the audio thread always reads a consistent graph. ---
    private volatile Routing _routing = new();
    private Dictionary<(Guid TrackId, int BusIndex), Guid> _multiOutRoutes = new();

    // --- Metronome count-in (recording pre-roll) ---
    private bool _countingIn;
    private long _countInElapsed;       // samples elapsed since the count-in began
    private long _countInTotalSamples;  // total count-in length in samples
    private int _countInClicks;         // clicks fired so far
    private int _countInClicksTotal;    // one click per count-in beat
    private int _beatsPerBar = 4;       // for the downbeat accent
    private int _metronomeNextBeat = -1; // next whole beat to click during playback metronome

    // Click oscillator (a short decaying sine added to the master bus).
    private int _clickRemaining;
    private int _clickTotal;
    private double _clickPhase;
    private double _clickPhaseInc;
    private float _clickAmp;

    private float _masterL;
    private float _masterR;

    // The render fan-out pool plus the cached per-block parameters its job delegate reads (kept in
    // fields so dispatching a block never allocates a closure on the audio thread).
    private readonly AudioWorkerPool _workers = new();
    private readonly Action<int> _renderJob;
    private TrackState[] _blkStates = Array.Empty<TrackState>();
    private Routing _blkRouting = new();
    private int _blkBufferLength;
    private int _blkChannels;
    private int _blkSampleRate;
    private int _blkDormantSamples;
    private double _blkPrevBeat;
    private double _blkCurBeat;
    private double _blkLookaheadBeats;
    private double _blkBpm;
    private bool _blkPlaying;
    private bool _blkSoloActive;

    // Per-block context + cross-track signal bus handed to effects that opt in via IContextualEffect.
    private readonly Effects.SidechainBus _sidechain = new();
    private readonly Effects.EffectContext _effectCtx = new();
    private bool _disposed;
    private volatile bool _rebuildPending;

    public AudioEngine(IAudioOutput output, IProjectService project, ITransportService transport,
        IPlaybackModeService playback, IEventAggregator events, IAuditionPlayer audition,
        IMidiOutputService? midiOut = null, IInputMonitorService? inputMonitor = null)
    {
        _output = output;
        _project = project;
        _transport = transport;
        _playback = playback;
        _audition = audition;
        _midiOut = midiOut ?? new NullMidiOutputService();
        _inputMonitor = inputMonitor ?? new NullInputMonitorService();
        _renderJob = RenderTrackJob;
        _project.ProjectChanged += OnProjectChanged;
        _transport.StateChanged += OnTransportStateChanged;
        _playback.ActiveClipsChanged += OnSessionClipsChanged;
        _playback.ModeChanged += OnPlaybackModeChanged;
        _output.FormatChanged += OnFormatChanged;
        events.Subscribe<TracksChangedEvent>(_ => RequestRebuild());
        events.Subscribe<AutomationChangedEvent>(e => e.Track.CommitAutoLanes());
    }

    public bool IsRunning => _output.IsRunning;
    public AudioFormat Format => _output.Format;
    public float MasterLevelLeft => _masterL;
    public float MasterLevelRight => _masterR;

    public void Start()
    {
        if (_output.IsRunning) return;
        _output.Start(Render);
        RebuildTracks();
    }

    public void Stop() => _output.Stop();

    private void OnProjectChanged() => RequestRebuild();

    private void OnFormatChanged() => RequestRebuild();

    /// <summary>
    /// Schedules a track-graph rebuild. While the audio device is running, preparation runs at the start
    /// of the next render block so effect <see cref="IAudioEffect.Prepare"/> never races
    /// <see cref="IAudioEffect.Process"/> on the worker pool (e.g. after "Render clip to new track").
    /// </summary>
    private void RequestRebuild()
    {
        if (!_output.IsRunning || _disposed)
            RebuildTracks();
        else
            _rebuildPending = true;
    }

    private void FlushPendingRebuild()
    {
        if (!_rebuildPending) return;
        _rebuildPending = false;
        RebuildTracks();
    }

    private void RebuildTracks()
    {
        var tracks = _project.Current.Tracks.ToArray();
        foreach (var track in tracks)
        {
            foreach (var slot in track.ActiveInstruments)
            {
                slot.Instrument.Prepare(_output.Format);
                foreach (var fx in slot.ActiveEffects) fx.Prepare(_output.Format);
            }

            foreach (var effect in track.ActiveEffects) effect.Prepare(_output.Format);
        }

        _tracks = tracks;
        _multiOutRoutes = MultiOutputRouter.BuildIndex(_project.Current.MultiOutputRoutes);
        BuildRouting(tracks);
    }

    // Builds the bus graph from the current tracks: a Bus per group/master, linked to its parent bus,
    // ordered deepest-first so a block can be mixed children → groups → master in a single pass.
    private void BuildRouting(Track[] tracks)
    {
        var trackById = new Dictionary<Guid, Track>(tracks.Length);
        foreach (var t in tracks) trackById[t.Id] = t;

        var busById = new Dictionary<Guid, Bus>();
        Bus? master = null;
        Track? masterTrack = null;
        foreach (var t in tracks)
        {
            if (!t.IsBus) continue;
            var bus = new Bus { Track = t };
            busById[t.Id] = bus;
            if (t.Kind == TrackKind.Master) { master = bus; masterTrack = t; }
        }

        foreach (var bus in busById.Values)
        {
            if (bus.Track.Kind == TrackKind.Master) bus.Parent = null;
            else bus.Parent = bus.Track.ParentId is { } pid && busById.TryGetValue(pid, out var p) ? p : master;
        }

        foreach (var bus in busById.Values)
        {
            var depth = 0;
            var p = bus.Parent;
            while (p is not null && depth < 64) { depth++; p = p.Parent; }
            bus.Depth = depth;
        }

        // Per-content-track render state (own buffer + dormancy counter), in project track order so
        // the serial mixdown phase is deterministic.
        var states = new List<TrackState>();
        foreach (var t in tracks)
        {
            if (IsDirectAudioTrack(t)) states.Add(new TrackState { Track = t });
        }

        _routing = new Routing
        {
            TrackById = trackById,
            BusById = busById,
            Master = master,
            MasterTrack = masterTrack,
            BusesDeepestFirst = busById.Values.OrderByDescending(b => b.Depth).ToArray(),
            ContentStates = states.ToArray(),
            SidechainSources = CollectSidechainSources(tracks),
            Pdc = LatencyCompensator.Compute(tracks)
        };

        var channels = _output.Format.Channels < 1 ? 1 : _output.Format.Channels;
        var maxFrames = 8192;
        foreach (var st in states)
        {
            if (_routing.Pdc.TryGetValue(st.Track.Id, out var comp))
            {
                st.PdcDelay = comp.DelaySamples;
                st.EnsurePdc(channels, maxFrames);
            }
        }

        foreach (var bus in busById.Values)
        {
            if (_routing.Pdc.TryGetValue(bus.Track.Id, out var comp))
            {
                bus.PdcDelay = comp.DelaySamples;
                bus.EnsurePdc(channels, maxFrames);
            }
        }
    }

    // Tracks referenced as sidechain sources must render even when muted/soloed-out.
    private static HashSet<Guid> CollectSidechainSources(Track[] tracks)
        => Automation.OfflineAutomationDriver.CollectSidechainSources(tracks);

    // The bus a track's output feeds into (respecting OutputTarget / RouteToMaster).
    private static Bus? MainOutputBusOf(Track track, Routing routing)
    {
        if (!track.RouteToMaster) return null;
        return track.OutputTarget switch
        {
            TrackOutputTarget.None => null,
            TrackOutputTarget.Master => routing.Master,
            TrackOutputTarget.SpecificBus when track.OutputBusId is { } id && routing.BusById.TryGetValue(id, out var b) => b,
            _ => track.ParentId is { } pid && routing.BusById.TryGetValue(pid, out var p) ? p : routing.Master
        };
    }

    // Legacy alias used by group hierarchy checks.
    private static Bus? ParentBusOf(Track track, Routing routing) => MainOutputBusOf(track, routing);

    // Pattern and MIDI lanes schedule notes elsewhere; they never render audio in the engine.
    private static bool IsDirectAudioTrack(Track track) =>
        !track.IsBus && track.Kind is TrackKind.Audio or TrackKind.Instrument;

    // The parent track in the routing tree (the master is the implicit parent of top-level tracks).
    private static Track? ParentTrackOf(Track track, Routing routing)
    {
        if (track.Kind == TrackKind.Master) return null;
        if (track.ParentId is { } pid && routing.TrackById.TryGetValue(pid, out var p)) return p;
        return routing.MasterTrack;
    }

    private static bool AnyAncestorSoloed(Track track, Routing routing)
    {
        var p = ParentTrackOf(track, routing);
        var n = 0;
        while (p is not null && n++ < 64)
        {
            if (p.IsSoloed) return true;
            p = ParentTrackOf(p, routing);
        }

        return false;
    }

    private void OnTransportStateChanged(TransportState state)
    {
        if (state == TransportState.Playing)
        {
            BeginPlayback();
        }
        else
        {
            _playing = false;
            _countingIn = false;
            _clickRemaining = 0;
            _metronomeNextBeat = -1;
            AllNotesOff();
            _transport.NotifyPlayhead(_transport.StartBeat);
        }
    }

    private void OnSessionClipsChanged()
    {
        if (_playing) RebuildPlaybackSchedule(_transport.PlayheadBeats);
    }

    private void OnPlaybackModeChanged()
    {
        if (_playing) RebuildPlaybackSchedule(_transport.PlayheadBeats);
    }

    private void BeginPlayback() => RebuildPlaybackSchedule(_transport.StartBeat);

    private void RebuildPlaybackSchedule(double startBeat)
    {
        var sampleRate = _output.Format.SampleRate;
        var channels = _output.Format.Channels < 1 ? 1 : _output.Format.Channels;
        var bpm = _transport.Tempo.BeatsPerMinute;
        _samplesPerBeat = bpm > 0 ? sampleRate * 60.0 / bpm : sampleRate;

        var context = new PlaybackScheduleContext
        {
            Project = _project.Current,
            Tracks = _tracks,
            StartBeat = startBeat,
            SampleRate = sampleRate,
            Channels = channels,
            Bpm = bpm
        };

        var schedule = BuildSchedule(context);

        var notes = new List<ScheduledNote>(schedule.Notes.Length);
        foreach (var n in schedule.Notes)
        {
            var track = _tracks.FirstOrDefault(t => t.Id == n.TrackId);
            notes.Add(new ScheduledNote(n.TrackId, n.OnBeat, n.OffBeat, n.Slots, n.MidiEffects, n.Note, n.Velocity, n.Gain,
                n.Pan, track?.RouteToExternalMidi ?? false, track?.ExternalMidiChannel ?? 1));
        }

        var ccEvents = new List<ScheduledControlChange>(schedule.ControlChanges.Length);
        foreach (var cc in schedule.ControlChanges)
        {
            var track = _tracks.FirstOrDefault(t => t.Id == cc.TrackId);
            ccEvents.Add(new ScheduledControlChange(cc.TrackId, cc.Beat, cc.Slots, cc.Controller, cc.Value,
                track?.RouteToExternalMidi ?? false, track?.ExternalMidiChannel ?? 1));
        }

        var clips = new List<AudioClipPlayback>(schedule.AudioClips.Length);
        foreach (var ac in schedule.AudioClips)
        {
            clips.Add(new AudioClipPlayback(ac.Track, ac.StartBeat, ac.LengthBeats, ac.Samples,
                ac.StretchToTempo, ac.SourceDurSeconds, ac.SourceOffsetSeconds,
                ac.FadeInBeats, ac.FadeOutBeats, ac.PitchShifters, ac.Warp, ac.WarpMode, ac.PitchCorrected,
                ac.AraPitchOffsetSemitones, ac.PitchSegments, ac.Gain));
        }

        _arrangementEndBeat = schedule.ArrangementEndBeat;

        _active.Clear();
        _currentBeat = startBeat;
        _nextEvent = 0;
        _nextCcEvent = 0;
        ResetPlaybackMetronome(startBeat);
        while (_nextEvent < notes.Count && notes[_nextEvent].OnBeat < startBeat) _nextEvent++;
        while (_nextCcEvent < ccEvents.Count && ccEvents[_nextCcEvent].Beat < startBeat) _nextCcEvent++;

        _events = notes.ToArray();
        _ccEvents = ccEvents.OrderBy(c => c.Beat).ToArray();
        _trackActivity = TrackActivityMap.Build(_events);
        _audioClips = clips.ToArray();

        foreach (var st in _routing.ContentStates) st.SilentSamples = 0;

        // A count-in (recording pre-roll) plays a bar of metronome clicks with the playhead parked
        // at the start marker; content begins only once the clicks have elapsed.
        var countInBeats = _transport.CountInBeats;
        _beatsPerBar = Math.Max(1, _project.Current.TimeSignature.Numerator);
        _clickRemaining = 0;
        if (countInBeats > 0)
        {
            _countingIn = true;
            _countInClicks = 0;
            _countInClicksTotal = countInBeats;
            _countInElapsed = 0;
            _countInTotalSamples = (long)Math.Round(countInBeats * _samplesPerBeat);
            _playing = false;
        }
        else
        {
            _countingIn = false;
            _playing = true;
        }
    }

    private PlaybackSchedule BuildSchedule(PlaybackScheduleContext context)
    {
        var sessionClips = context.Project.SessionClips;
        var launches = _playback.ActiveLaunches;

        return _playback.Mode switch
        {
            PlaybackMode.Session => new SessionScheduler(sessionClips, launches).Build(context),
            PlaybackMode.Hybrid => BuildHybridSchedule(context, sessionClips, launches),
            _ => MergeWithPatterns(_arrangementScheduler.Build(context), context)
        };
    }

    private PlaybackSchedule BuildHybridSchedule(PlaybackScheduleContext context,
        IReadOnlyList<SessionClip> sessionClips,
        IReadOnlyDictionary<Guid, SessionClipLaunchState> launches)
    {
        var crossfader = (float)_playback.SessionCrossfader;
        var arrGain = 1f - crossfader;
        var sesGain = crossfader;

        var arrangement = MergeWithPatterns(_arrangementScheduler.Build(context), context);
        var session = new SessionScheduler(sessionClips, launches).Build(context);

        return BlendSchedules(arrangement, session, arrGain, sesGain);
    }

    private PlaybackSchedule MergeWithPatterns(PlaybackSchedule baseSchedule, PlaybackScheduleContext context)
        => HybridScheduler.MergeSchedules(baseSchedule, _patternScheduler.Build(context));

    private static PlaybackSchedule BlendSchedules(PlaybackSchedule arrangement, PlaybackSchedule session,
        float arrangementGain, float sessionGain)
    {
        var notes = arrangement.Notes.Select(n => n with { Gain = arrangementGain })
            .Concat(session.Notes.Select(n => n with { Gain = sessionGain }))
            .OrderBy(n => n.OnBeat)
            .ToArray();
        var clips = arrangement.AudioClips.Select(c => CopyClip(c, arrangementGain))
            .Concat(session.AudioClips.Select(c => CopyClip(c, sessionGain)))
            .ToArray();
        return new PlaybackSchedule
        {
            Notes = notes,
            ControlChanges = arrangement.ControlChanges.Concat(session.ControlChanges).OrderBy(c => c.Beat).ToArray(),
            AudioClips = clips,
            ArrangementEndBeat = Math.Max(arrangement.ArrangementEndBeat, session.ArrangementEndBeat)
        };
    }

    private static ScheduledAudioClip CopyClip(ScheduledAudioClip c, float gain) => new()
    {
        Track = c.Track,
        StartBeat = c.StartBeat,
        LengthBeats = c.LengthBeats,
        Samples = c.Samples,
        StretchToTempo = c.StretchToTempo,
        SourceDurSeconds = c.SourceDurSeconds,
        SourceOffsetSeconds = c.SourceOffsetSeconds,
        FadeInBeats = c.FadeInBeats,
        FadeOutBeats = c.FadeOutBeats,
        PitchShifters = c.PitchShifters,
        Warp = c.Warp,
        WarpMode = c.WarpMode,
        PitchCorrected = c.PitchCorrected,
        Gain = gain
    };

    private void Render(Span<float> buffer)
    {
        FlushPendingRebuild();

        var blockStart = Stopwatch.GetTimestamp();
        buffer.Clear();

        var channels = _output.Format.Channels < 1 ? 1 : _output.Format.Channels;
        var frames = buffer.Length / channels;

        // Count-in runs before content: emit metronome clicks, keep the playhead parked.
        if (_countingIn) ProcessCountIn(frames);

        var playing = _playing;
        var prevBeat = _currentBeat;

        // Live tempo: re-evaluate the effective BPM at the start of this block and advance the playhead at
        // that rate. The effective tempo is the master track's Tempo automation curve when present, else the
        // manual transport tempo — so an automated tempo ramp (or a manual tempo nudge) takes effect
        // immediately and continuously while playing, exactly like Bitwig. Tempo-synced clips and the MIDI
        // sequencer follow automatically because they're positioned by beat, which now advances at the live rate.
        var sampleRate = _output.Format.SampleRate;
        var transportBpm = _transport.Tempo.BeatsPerMinute;
        var effectiveBpm = playing ? EffectiveBpm(prevBeat, transportBpm) : (transportBpm > 0 ? transportBpm : 120.0);
        if (playing && effectiveBpm > 0) _samplesPerBeat = sampleRate * 60.0 / effectiveBpm;

        var curBeat = prevBeat + frames / _samplesPerBeat;

        // Looping: wrap the playhead back when it reaches the loop end (an explicit "[ ]" region if set,
        // otherwise the whole arrangement). Not while recording — a take should run past the end. Done at
        // block granularity, so the wrap point is accurate to within one buffer.
        if (playing && !_transport.IsRecording)
        {
            var hasRegion = _transport.IsLoopActive;
            var wrapStart = hasRegion ? _transport.LoopStart : _transport.StartBeat;
            var wrapEnd = hasRegion ? _transport.LoopEnd : _arrangementEndBeat;
            if (wrapEnd > wrapStart + 1e-9 && curBeat >= wrapEnd)
            {
                WrapPlayback(wrapStart);
                prevBeat = wrapStart;
                curBeat = prevBeat + frames / _samplesPerBeat;
            }
        }

        if (playing) ScheduleControlChanges(curBeat);
        if (playing) ScheduleNotes(curBeat);

        if (playing && !_countingIn && _transport.MetronomeEnabled)
            ProcessPlaybackMetronome(prevBeat, curBeat);

        var tracks = _tracks;
        var routing = _routing;
        var states = routing.ContentStates;
        var buses = routing.BusesDeepestFirst;

        foreach (var st in states) st.EnsureCapacity(buffer.Length);

        // Prepare each bus's accumulation buffer for this block.
        foreach (var bus in buses)
        {
            if (bus.Buffer.Length < buffer.Length) bus.Buffer = new float[buffer.Length];
            Array.Clear(bus.Buffer, 0, buffer.Length);
        }

        // Automation drives volume/pan/effect params on every track — buses included.
        if (playing)
        {
            foreach (var track in tracks) ApplyAutomation(track, curBeat);
            var bpm = EffectiveBpm(curBeat, _transport.Tempo.BeatsPerMinute);
            foreach (var track in tracks)
                Modulation.TrackModulatorDriver.ApplyTrack(track, curBeat, bpm, _project.Current);
        }

        var soloActive = false;
        foreach (var track in tracks)
        {
            if (track.IsSoloed) { soloActive = true; break; }
        }

        // Per-block context for tempo-aware / sidechain effects (the bus's tap buffers persist across blocks).
        _effectCtx.Format = _output.Format;
        // Tempo-aware effects (e.g. Stuttero) see the same live, automation-driven tempo the playhead uses;
        // when stopped this falls back to the manual transport tempo so live audition still has a real BPM.
        _effectCtx.Bpm = effectiveBpm;
        _effectCtx.PlayheadBeats = prevBeat;
        _effectCtx.Playing = playing;
        _effectCtx.Sidechain = _sidechain;
        _sidechain.BeginBlock();

        // RENDER phase: fan out across worker threads (plus this audio thread). Each content track renders
        // into its own buffer with no shared scratch — safe without locks.
        _blkStates = states;
        _blkRouting = routing;
        _blkBufferLength = buffer.Length;
        _blkChannels = channels;
        _blkSampleRate = sampleRate;
        _blkDormantSamples = Math.Max(frames, (int)(DormancyTailSeconds * sampleRate));
        _blkPrevBeat = prevBeat;
        _blkCurBeat = curBeat;
        _blkLookaheadBeats = frames * LookaheadBlocks / _samplesPerBeat;
        _blkBpm = effectiveBpm;
        _blkPlaying = playing;
        _blkSoloActive = soloActive;
        if (states.Length > 0) _workers.Run(states.Length, _renderJob);

        FlushMultiOutputRoutes(states);

        // MIXDOWN phase: sidechain publishes, strip gains, bus summing — serial and deterministic.
        foreach (var st in states)
        {
            var track = st.Track;
            var silenced = IsSilenced(track, soloActive, routing);
            var sidechainSource = routing.SidechainSources.Contains(track.Id);

            if (silenced)
            {
                track.MeterLevel *= MeterRelease;
                if (st.Rendered && (sidechainSource || _sidechain.IsRequested(track.Id)))
                    _sidechain.Publish(track.Id, st.Buffer.AsSpan(0, buffer.Length), channels);
                continue;
            }

            if (!st.HasContent)
            {
                track.MeterLevel *= MeterRelease;
                continue;
            }

            if (sidechainSource || _sidechain.IsRequested(track.Id))
                _sidechain.Publish(track.Id, st.Buffer.AsSpan(0, buffer.Length), channels);

            ProcessSends(st, track, routing, channels, frames, soloActive, preFader: true);

            if (st.PdcDelay > 0)
                st.PdcLine.Process(st.Buffer.AsSpan(0, buffer.Length), frames);

            var parent = MainOutputBusOf(track, routing);
            if (parent is null)
            {
                ProcessSends(st, track, routing, channels, frames, soloActive, preFader: false);
                track.MeterLevel = Mixing.PeakLevel(st.Buffer.AsSpan(0, buffer.Length), channels, frames, track.MeterLevel, MeterRelease);
                continue;
            }

            var target = parent.Buffer.AsSpan(0, buffer.Length);
            if (channels >= 8)
            {
                var sg = Mixing.Surround71StripGains(track.Volume, track.Pan, track.SurroundWidth, track.SurroundPan);
                track.MeterLevel = MixIntoSurround71(target, st.Buffer.AsSpan(0, buffer.Length), sg, channels, frames, track.MeterLevel);
            }
            else if (channels >= 6)
            {
                var sg = Mixing.Surround51StripGains(track.Volume, track.Pan, track.SurroundWidth, track.SurroundPan);
                track.MeterLevel = MixIntoSurround51(target, st.Buffer.AsSpan(0, buffer.Length), sg, channels, frames, track.MeterLevel);
            }
            else
            {
                var (lg, rg) = Mixing.StripGains(track.Volume, track.Pan);
                track.MeterLevel = MixInto(target, st.Buffer.AsSpan(0, buffer.Length), lg, rg, channels, frames, track.MeterLevel);
            }

            ProcessSends(st, track, routing, channels, frames, soloActive, preFader: false);
        }

        // 2) Buses deepest-first: insert effects on the summed input → strip → into the parent bus
        //    (the master, having no parent, strips straight into the device output below).
        foreach (var bus in buses)
        {
            var bt = bus.Track;
            if (bt.IsMuted)
            {
                bt.MeterLevel *= MeterRelease;
                continue;
            }

            var busSpan = bus.Buffer.AsSpan(0, buffer.Length);
            var effects = bt.ActiveEffects;
            if (effects.Length > 0)
            {
                foreach (var fx in effects)
                {
                    if (!fx.Enabled) continue;
                    if (fx is Effects.IContextualEffect cae) cae.SetContext(_effectCtx);
                    fx.Process(busSpan);
                }
            }

            // A group/master bus can be a sidechain source too (e.g. a "Drums" group triggering a duck).
            if (_sidechain.IsRequested(bt.Id)) _sidechain.Publish(bt.Id, busSpan, channels);

            if (bus.PdcDelay > 0)
                bus.PdcLine.Process(busSpan, frames);

            var target = bus.Parent is not null ? bus.Parent.Buffer.AsSpan(0, buffer.Length) : buffer;
            var (lg, rg) = Mixing.BusGains(bt.Volume, bt.Pan);
            bt.MeterLevel = MixInto(target, busSpan, lg, rg, channels, frames, bt.MeterLevel);
        }

        if (playing)
        {
            _currentBeat = curBeat;
            _transport.NotifyPlayhead(curBeat);
            if (_playback.Mode != PlaybackMode.Arrangement)
            {
                _playback.ProcessPlayhead(curBeat);
                _playback.TickFollowActions(prevBeat, curBeat);
            }
        }

        // Metronome clicks (triggered during the count-in) are added to the master bus.
        RenderMetronome(buffer, frames, channels);

        // Library/file audition preview (independent of the transport) mixes into the master.
        _audition.Mix(buffer, _output.Format);

        // Software input monitoring for armed/on audio tracks.
        _inputMonitor.Mix(buffer, channels, frames);

        // Limit and measure the master output.
        float masterPeakL = 0, masterPeakR = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            var s = buffer[i];
            if (s > 1f) s = 1f;
            else if (s < -1f) s = -1f;
            buffer[i] = s;

            var a = s < 0 ? -s : s;
            if (channels >= 2 && (i & 1) == 1)
            {
                if (a > masterPeakR) masterPeakR = a;
            }
            else
            {
                if (a > masterPeakL) masterPeakL = a;
            }
        }

        _masterL = MaxWithRelease(masterPeakL, _masterL);
        _masterR = MaxWithRelease(masterPeakR, _masterR);

        var elapsedTicks = Stopwatch.GetTimestamp() - blockStart;
        var micros = elapsedTicks * 1_000_000 / Stopwatch.Frequency;
        AudioDiagnostics.RecordBlock(micros);
    }

    private void ProcessSends(TrackState st, Track track, Routing routing, int channels, int frames,
        bool soloActive, bool preFader)
    {
        if (track.Sends.Count == 0) return;
        if (IsSilenced(track, soloActive, routing)) return;
        if (!st.HasContent) return;

        var span = st.Buffer.AsSpan(0, _blkBufferLength);
        var (lg, rg) = Mixing.StripGains(track.Volume, track.Pan);
        foreach (var send in track.Sends)
        {
            if (!send.Enabled || send.Level <= 0) continue;
            if (send.PreFader != preFader) continue;
            if (!routing.BusById.TryGetValue(send.TargetTrackId, out var ret)) continue;

            var gain = (float)send.Level;
            var dst = ret.Buffer.AsSpan(0, _blkBufferLength);
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                var l = span[i] * (preFader ? gain : gain * lg);
                dst[i] += l;
                if (channels >= 2)
                {
                    var r = span[i + 1] * (preFader ? gain : gain * rg);
                    dst[i + 1] += r;
                }
            }
        }
    }

    private static float MaxWithRelease(float peak, float current)
    {
        var decayed = current * MeterRelease;
        return peak > decayed ? peak : decayed;
    }

    // Worker-pool job: render one content track into its own <see cref="TrackState"/> buffer.
    private void RenderTrackJob(int index)
    {
        var st = _blkStates[index];
        var track = st.Track;
        var silenced = IsSilenced(track, _blkSoloActive, _blkRouting);
        var sidechainSource = _blkRouting.SidechainSources.Contains(track.Id);

        st.Rendered = false;
        st.HasContent = false;

        if (silenced && !sidechainSource) return;

        if (_blkPlaying && !sidechainSource && !TrackNeedsRender(st, track)) return;

        st.HasContent = RenderTrackContent(st, _blkBufferLength, _blkChannels, _blkPrevBeat,
            _blkSampleRate, _blkBpm, _blkPlaying, _blkDormantSamples);
        st.Rendered = true;
    }

    // Renders a content track's signal into <paramref name="state"/>.Buffer: its instrument rack (each
    // enabled slot through its own pre-effect chain) or its audio clips, then the track's insert (post)
    // effects. Returns whether anything audible remains. Insert chains are skipped only after the track
    // has been silent long enough for tails to decay — instruments always render so a dormant track
    // wakes immediately when new notes arrive.
    private bool RenderTrackContent(TrackState state, int bufferLength, int channels,
        double prevBeat, int sampleRate, double effectiveBpm, bool playing, int dormantSamples)
    {
        var track = state.Track;
        var temp = state.Buffer.AsSpan(0, bufferLength);
        var frames = bufferLength / Math.Max(1, channels);
        temp.Clear();

        var effects = track.ActiveEffects;
        var insertDormant = effects.Length > 0 && state.SilentSamples >= dormantSamples;

        var hasContent = false;
        var slotTemp = state.SlotScratch.AsSpan(0, bufferLength);
        var slots = track.ActiveInstruments;
        var slotIndex = 0;
        if (slots.Length > 0)
        {
            foreach (var slot in slots)
            {
                if (!slot.Enabled) { slotIndex++; continue; }
                if (playing && !InstrumentNeedsRender(slot.Instrument, track.Id)) { slotIndex++; continue; }

                slotTemp.Clear();
                if (slot.Instrument is IMultiOutputInstrument multi)
                {
                    multi.RenderMulti(slotTemp, (busIndex, busAudio) =>
                        RouteExtraBus(state, track.Id, slotIndex, slot, busIndex, busAudio));
                }
                else
                {
                    slot.Instrument.Render(slotTemp);
                }

                var slotSignal = HasSignal(slotTemp);
                if (slotSignal)
                {
                    foreach (var fx in slot.ActiveEffects)
                    {
                        if (!fx.Enabled) continue;
                        if (fx is Effects.IContextualEffect cae) cae.SetContext(_effectCtx);
                        fx.Process(slotTemp);
                    }

                    for (var i = 0; i < slotTemp.Length; i++) temp[i] += slotTemp[i];
                    hasContent = true;
                }

                slotIndex++;
            }
        }
        else if (playing && track.Kind == TrackKind.Audio)
        {
            foreach (var acp in _audioClips)
            {
                if (ReferenceEquals(acp.Track, track))
                {
                    RenderClipBlock(temp, acp, prevBeat, _samplesPerBeat, sampleRate, channels, effectiveBpm);
                    hasContent = true;
                }
            }
        }

        // New instrument/audio content always wakes the track — don't leave it stuck dormant.
        var drySignal = HasSignal(temp);
        if (drySignal) state.SilentSamples = 0;

        // Skip insert FX only while dormant AND the dry path is still silent (tails have finished).
        if (effects.Length > 0 && (!insertDormant || drySignal))
        {
            foreach (var fx in effects)
            {
                if (!fx.Enabled) continue;
                if (fx is Effects.IContextualEffect cae) cae.SetContext(_effectCtx);
                fx.Process(temp);
            }

            hasContent = true;
        }

        if (HasSignal(temp))
        {
            state.SilentSamples = 0;
            return true;
        }

        if (drySignal) return true;

        if (effects.Length == 0) return false;

        state.SilentSamples += frames;
        return hasContent && state.SilentSamples < dormantSamples;
    }

    private void RouteExtraBus(TrackState sourceState, Guid sourceTrackId, int slotIndex,
        InstrumentSlot slot, int busIndex, ReadOnlySpan<float> busAudio)
    {
        Guid? destId = slot.OutputTrackId;
        if (!destId.HasValue && _multiOutRoutes.TryGetValue((sourceTrackId, busIndex), out var routed))
            destId = routed;
        if (!destId.HasValue) return;

        var copy = new float[busAudio.Length];
        busAudio.CopyTo(copy);
        sourceState.PendingRoutes.Add((destId.Value, copy));
    }

    private static void FlushMultiOutputRoutes(TrackState[] states)
    {
        var byId = new Dictionary<Guid, TrackState>(states.Length);
        foreach (var st in states) byId[st.Track.Id] = st;

        foreach (var st in states)
        {
            foreach (var (destId, samples) in st.PendingRoutes)
            {
                if (!byId.TryGetValue(destId, out var dest)) continue;
                var destSpan = dest.Buffer.AsSpan(0, samples.Length);
                for (var i = 0; i < samples.Length; i++) destSpan[i] += samples[i];
                dest.HasContent = true;
                dest.SilentSamples = 0;
            }

            st.PendingRoutes.Clear();
        }
    }

    private static bool HasSignal(ReadOnlySpan<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var a = buffer[i];
            if (a < 0) a = -a;
            if (a > SilenceThreshold) return true;
        }

        return false;
    }

    private bool TrackNeedsRender(TrackState st, Track track)
    {
        if (AnySlotHasActiveVoices(track)) return true;
        var endBeat = _blkCurBeat + _blkLookaheadBeats;
        if (_trackActivity.HasActivity(track.Id, _blkPrevBeat, endBeat)) return true;
        if (track.Kind == TrackKind.Audio && HasActiveAudioClip(track, _blkPrevBeat, _blkCurBeat)) return true;
        return false;
    }

    private bool InstrumentNeedsRender(IInstrument instrument, Guid trackId)
    {
        if (instrument is IInstrumentVoiceState vs && vs.HasActiveVoices) return true;
        return _trackActivity.HasActivity(trackId, _blkCurBeat, _blkCurBeat + _blkLookaheadBeats);
    }

    private static bool AnySlotHasActiveVoices(Track track)
    {
        foreach (var slot in track.ActiveInstruments)
        {
            if (!slot.Enabled) continue;
            if (slot.Instrument is IInstrumentVoiceState vs && vs.HasActiveVoices) return true;
        }

        return false;
    }

    private bool HasActiveAudioClip(Track track, double prevBeat, double curBeat)
    {
        foreach (var acp in _audioClips)
        {
            if (!ReferenceEquals(acp.Track, track)) continue;
            if (acp.StartBeat + acp.LengthBeats > prevBeat && acp.StartBeat < curBeat) return true;
        }

        return false;
    }

    // Mixes <paramref name="source"/> through per-channel gains additively into <paramref name="target"/>,
    // returning the strip's peak (with meter release) for the level meter.
    private static float MixInto(Span<float> target, Span<float> source, float leftGain, float rightGain,
        int channels, int frames, float currentMeter)
    {
        var peak = 0f;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var v = source[i + c] * Mixing.ChannelGain(c, leftGain, rightGain);
                target[i + c] += v;
                var a = v < 0 ? -v : v;
                if (a > peak) peak = a;
            }
        }

        return MaxWithRelease(peak, currentMeter);
    }

    private static float MixIntoSurround51(Span<float> target, Span<float> source,
        (float L, float R, float C, float Lfe, float Ls, float Rs) gains, int channels, int frames, float currentMeter)
    {
        var peak = 0f;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var l = source[i];
            var r = channels >= 2 ? source[i + 1] : l;
            var mid = (l + r) * 0.5f;

            if (channels > 0)
            {
                var v0 = l * gains.L;
                target[i] += v0;
                var a0 = v0 < 0 ? -v0 : v0;
                if (a0 > peak) peak = a0;
            }
            if (channels > 1)
            {
                var v1 = r * gains.R;
                target[i + 1] += v1;
                var a1 = v1 < 0 ? -v1 : v1;
                if (a1 > peak) peak = a1;
            }
            if (channels > 2)
            {
                var v2 = mid * gains.C;
                target[i + 2] += v2;
                var a2 = v2 < 0 ? -v2 : v2;
                if (a2 > peak) peak = a2;
            }
            if (channels > 3)
            {
                var v3 = mid * gains.Lfe;
                target[i + 3] += v3;
                var a3 = v3 < 0 ? -v3 : v3;
                if (a3 > peak) peak = a3;
            }
            if (channels > 4)
            {
                var v4 = l * gains.Ls;
                target[i + 4] += v4;
                var a4 = v4 < 0 ? -v4 : v4;
                if (a4 > peak) peak = a4;
            }
            if (channels > 5)
            {
                var v5 = r * gains.Rs;
                target[i + 5] += v5;
                var a5 = v5 < 0 ? -v5 : v5;
                if (a5 > peak) peak = a5;
            }
        }

        return MaxWithRelease(peak, currentMeter);
    }

    private static float MixIntoSurround71(Span<float> target, Span<float> source,
        (float L, float R, float C, float Lfe, float Ls, float Rs, float Sl, float Sr) gains,
        int channels, int frames, float currentMeter)
    {
        var peak = 0f;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var l = source[i];
            var r = channels >= 2 ? source[i + 1] : l;
            var mid = (l + r) * 0.5f;

            if (channels > 0)
            {
                var v0 = l * gains.L;
                target[i] += v0;
                var a0 = v0 < 0 ? -v0 : v0;
                if (a0 > peak) peak = a0;
            }
            if (channels > 1)
            {
                var v1 = r * gains.R;
                target[i + 1] += v1;
                var a1 = v1 < 0 ? -v1 : v1;
                if (a1 > peak) peak = a1;
            }
            if (channels > 2)
            {
                var v2 = mid * gains.C;
                target[i + 2] += v2;
                var a2 = v2 < 0 ? -v2 : v2;
                if (a2 > peak) peak = a2;
            }
            if (channels > 3)
            {
                var v3 = mid * gains.Lfe;
                target[i + 3] += v3;
                var a3 = v3 < 0 ? -v3 : v3;
                if (a3 > peak) peak = a3;
            }
            if (channels > 4)
            {
                var v4 = l * gains.Ls;
                target[i + 4] += v4;
                var a4 = v4 < 0 ? -v4 : v4;
                if (a4 > peak) peak = a4;
            }
            if (channels > 5)
            {
                var v5 = r * gains.Rs;
                target[i + 5] += v5;
                var a5 = v5 < 0 ? -v5 : v5;
                if (a5 > peak) peak = a5;
            }
            if (channels > 6)
            {
                var v6 = l * gains.Sl;
                target[i + 6] += v6;
                var a6 = v6 < 0 ? -v6 : v6;
                if (a6 > peak) peak = a6;
            }
            if (channels > 7)
            {
                var v7 = r * gains.Sr;
                target[i + 7] += v7;
                var a7 = v7 < 0 ? -v7 : v7;
                if (a7 > peak) peak = a7;
            }
        }

        return MaxWithRelease(peak, currentMeter);
    }

    // note scheduler at the first event from there. Audio clips re-render from the new beat automatically.
    private void WrapPlayback(double target)
    {
        for (var i = 0; i < _active.Count; i++) _active[i].Fire(on: false, _midiOut);
        _active.Clear();
        AllNotesOff();

        // Restart each clip's read cursor (and pitch-shifter tail) so a looped clip reads cleanly from the
        // wrap point rather than continuing from a stale position.
        foreach (var acp in _audioClips) acp.Reset();

        _currentBeat = target;
        _nextEvent = 0;
        _nextCcEvent = 0;
        ResetPlaybackMetronome(target);
        var events = _events;
        while (_nextEvent < events.Length && events[_nextEvent].OnBeat < target) _nextEvent++;
        var ccEvents = _ccEvents;
        while (_nextCcEvent < ccEvents.Length && ccEvents[_nextCcEvent].Beat < target) _nextCcEvent++;
        foreach (var st in _routing.ContentStates) st.SilentSamples = 0;
    }

    // The track's MIDI-aware insert effects (empty array when none), captured for the note scheduler.
    private static IMidiAwareEffect[] MidiEffectsOf(Track track)
    {
        var effects = track.ActiveEffects;
        List<IMidiAwareEffect>? aware = null;
        foreach (var fx in effects)
            if (fx is IMidiAwareEffect m) (aware ??= new List<IMidiAwareEffect>()).Add(m);
        return aware?.ToArray() ?? Array.Empty<IMidiAwareEffect>();
    }

    private void ScheduleControlChanges(double curBeat)
    {
        var events = _ccEvents;
        while (_nextCcEvent < events.Length && events[_nextCcEvent].Beat < curBeat)
        {
            events[_nextCcEvent].Fire(_midiOut);
            _nextCcEvent++;
        }
    }

    private void ScheduleNotes(double curBeat)
    {
        var events = _events;
        while (_nextEvent < events.Length && events[_nextEvent].OnBeat < curBeat)
        {
            var ev = events[_nextEvent];
            ev.Fire(on: true, _midiOut);
            _active.Add(ev);
            _nextEvent++;
        }

        for (var i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].OffBeat <= curBeat)
            {
                _active[i].Fire(on: false, _midiOut);
                _active.RemoveAt(i);
            }
        }
    }

    // Advances the count-in by one block: fires a click at each beat boundary (accented on the
    // downbeat) and, once the full pre-roll has elapsed, hands over to content playback.
    private void ProcessPlaybackMetronome(double prevBeat, double curBeat)
    {
        if (_metronomeNextBeat < 0)
            _metronomeNextBeat = (int)Math.Ceiling(prevBeat - 1e-9);

        while (_metronomeNextBeat <= curBeat + 1e-9)
        {
            TriggerClick(_metronomeNextBeat % _beatsPerBar == 0);
            _metronomeNextBeat++;
        }
    }

    private void ResetPlaybackMetronome(double beat)
        => _metronomeNextBeat = (int)Math.Ceiling(beat - 1e-9);

    private void ProcessCountIn(int frames)
    {
        while (_countInClicks < _countInClicksTotal &&
               _countInElapsed >= (long)(_countInClicks * _samplesPerBeat))
        {
            TriggerClick(_countInClicks % _beatsPerBar == 0);
            _countInClicks++;
        }

        _countInElapsed += frames;

        if (_countInElapsed >= _countInTotalSamples)
        {
            _countingIn = false;
            _playing = true;
            _transport.NotifyCountInFinished();
        }
    }

    private void TriggerClick(bool accent)
    {
        var sampleRate = _output.Format.SampleRate;
        _clickTotal = _clickRemaining = Math.Max(1, (int)(sampleRate * 0.06)); // ~60 ms
        _clickPhase = 0;
        var freq = accent ? 1760.0 : 1320.0;
        _clickPhaseInc = 2.0 * Math.PI * freq / sampleRate;
        _clickAmp = accent ? 0.5f : 0.32f;
    }

    private void RenderMetronome(Span<float> buffer, int frames, int channels)
    {
        if (_clickRemaining <= 0) return;
        for (var frame = 0; frame < frames && _clickRemaining > 0; frame++)
        {
            var env = (float)_clickRemaining / _clickTotal; // linear decay
            var s = _clickAmp * env * (float)Math.Sin(_clickPhase);
            _clickPhase += _clickPhaseInc;
            var i = frame * channels;
            for (var c = 0; c < channels; c++) buffer[i + c] += s;
            _clickRemaining--;
        }
    }

    // The tempo (BPM) in force at <paramref name="beat"/>: the master track's Tempo automation curve when
    // one exists, otherwise <paramref name="fallback"/> (the manual transport tempo). An armed tempo lane is
    // ignored while recording so the manual tempo being captured drives playback, not the old curve.
    private double EffectiveBpm(double beat, double fallback)
    {
        if (_transport.IsRecording && _routing.MasterTrack?.ActiveAutoLanes
                .Any(l => l.Binding?.Kind == AutomationTargetKind.Tempo && l.IsArmed) == true)
            return fallback > 0 ? fallback : 120.0;

        return Automation.OfflineAutomationDriver.ResolveTempo(_project.Current, beat, fallback);
    }

    // Drives each automation lane's target from its curve at the current beat. Armed lanes are
    // left alone while recording so the user's manual control moves are captured, not overwritten.
    private void ApplyAutomation(Track track, double beat)
        => Automation.OfflineAutomationDriver.ApplyTrack(track, beat, skipArmedLanes: _transport.IsRecording);

    // A content track is silenced by its own mute, or — when anything is soloed — unless it or one of its
    // ancestor groups is soloed (so soloing a group plays its children; buses always pass soloed signals).
    private static bool IsSilenced(Track track, bool soloActive, Routing routing)
        => track.IsMuted || (soloActive && !(track.IsSoloed || AnyAncestorSoloed(track, routing)));

    private void AllNotesOff()
    {
        foreach (var track in _tracks)
        {
            foreach (var slot in track.ActiveInstruments) slot.Instrument.AllNotesOff();
            foreach (var fx in track.ActiveEffects)
                if (fx is IMidiAwareEffect m) m.AllNotesOff();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _playing = false;
        _project.ProjectChanged -= OnProjectChanged;
        _transport.StateChanged -= OnTransportStateChanged;
        _playback.ActiveClipsChanged -= OnSessionClipsChanged;
        _playback.ModeChanged -= OnPlaybackModeChanged;
        _output.FormatChanged -= OnFormatChanged;
        _output.Stop();
        _output.Dispose();
        _workers.Dispose();
    }

    // Per-content-track render state: own buffer, slot scratch, and dormancy counter.
    private sealed class TrackState
    {
        public Track Track = null!;
        public float[] Buffer = Array.Empty<float>();
        public float[] SlotScratch = Array.Empty<float>();
        public int SilentSamples;
        public bool HasContent;
        public bool Rendered;
        public int PdcDelay;
        public PdcDelayLine PdcLine = new();
        public readonly List<(Guid DestId, float[] Samples)> PendingRoutes = new();

        public void EnsureCapacity(int length)
        {
            if (Buffer.Length < length) Buffer = new float[length];
            if (SlotScratch.Length < length) SlotScratch = new float[length];
        }

        public void EnsurePdc(int channels, int maxFrames)
        {
            PdcLine.Configure(PdcDelay, channels, maxFrames);
        }
    }

    // A scheduled note targets a track's instrument rack (sound — every enabled slot) and/or its
    // MIDI-aware effects (gestures); either may be absent. Slots and MidiEffects are the track's
    // snapshots, captured when playback began (slot.Enabled is read live so toggles take effect).
    private readonly record struct ScheduledNote(Guid TrackId, double OnBeat, double OffBeat, InstrumentSlot[]? Slots,
        IMidiAwareEffect[] MidiEffects, int Note, float Velocity, float Gain = 1f, float Pan = 0f,
        bool ExternalMidi = false, int ExternalChannel = 1)
    {
        public void Fire(bool on, IMidiOutputService? midiOut)
        {
            if (on && Math.Abs(Pan) > 1e-6f)
            {
                var cc10 = (int)Math.Clamp((Pan + 1f) * 0.5f * 127f, 0f, 127f);
                SendControlChange(10, cc10, midiOut);
            }

            var vel = Velocity * Gain;
            if (Slots is not null)
            {
                foreach (var slot in Slots)
                {
                    if (!slot.Enabled) continue;
                    if (on) slot.Instrument.NoteOn(Note, vel);
                    else slot.Instrument.NoteOff(Note);
                }
            }

            var midiVel = (byte)Math.Clamp((int)(vel * 127f), 0, 127);
            if (ExternalMidi && midiOut is not null && midiOut.IsAvailable)
                midiOut.SendNote(ExternalChannel, Note, on, midiVel);

            if (MidiEffects.Length == 0) return;
            var msg = new MidiMessage(on ? MidiMessageKind.NoteOn : MidiMessageKind.NoteOff, 0, (byte)Note, on ? midiVel : (byte)0);
            foreach (var fx in MidiEffects) fx.HandleMidi(msg);
        }

        private void SendControlChange(int controller, int value, IMidiOutputService? midiOut)
        {
            if (Slots is not null)
            {
                foreach (var slot in Slots)
                {
                    if (!slot.Enabled) continue;
                    slot.Instrument.ControlChange(controller, value);
                }
            }

            if (ExternalMidi && midiOut is not null && midiOut.IsAvailable)
                midiOut.SendControlChange(ExternalChannel, controller, value);
        }
    }

    private readonly record struct ScheduledControlChange(
        Guid TrackId, double Beat, InstrumentSlot[]? Slots, int Controller, int Value,
        bool ExternalMidi = false, int ExternalChannel = 1)
    {
        public void Fire(IMidiOutputService? midiOut)
        {
            var value = Math.Clamp(Value, 0, 127);
            if (Slots is not null)
            {
                foreach (var slot in Slots)
                {
                    if (!slot.Enabled) continue;
                    slot.Instrument.ControlChange(Controller, value);
                }
            }

            if (ExternalMidi && midiOut is not null && midiOut.IsAvailable)
                midiOut.SendControlChange(ExternalChannel, Controller, value);
        }
    }

    // A scheduled audio clip, plus the running read state the block renderer keeps across blocks. The cursor
    // (ReadPos) is advanced continuously so a changing tempo never jumps the source read position.
    private sealed class AudioClipPlayback
    {
        public AudioClipPlayback(Track track, double startBeat, double lengthBeats, AudioSampleBuffer samples,
            bool stretchToTempo, double sourceDurSeconds, double sourceOffsetSeconds,
            double fadeInBeats, double fadeOutBeats, PitchShifter[]? pitchShifters,
            WarpMap? warp, WarpMode warpMode, bool pitchCorrected, double araPitchOffsetSemitones = 0.0,
            IReadOnlyList<PitchNoteSegment>? pitchSegments = null, float gain = 1f)
        {
            Track = track;
            StartBeat = startBeat;
            LengthBeats = lengthBeats;
            Samples = samples;
            StretchToTempo = stretchToTempo;
            SourceDurSeconds = sourceDurSeconds;
            SourceOffsetSeconds = sourceOffsetSeconds;
            FadeInBeats = fadeInBeats;
            FadeOutBeats = fadeOutBeats;
            PitchShifters = pitchShifters;
            Warp = warp;
            WarpMode = warpMode;
            PitchCorrected = pitchCorrected;
            AraPitchOffsetSemitones = araPitchOffsetSemitones;
            PitchSegments = pitchSegments ?? Array.Empty<PitchNoteSegment>();
            Gain = gain;
        }

        public readonly Track Track;
        public readonly double StartBeat;
        public readonly double LengthBeats;
        public readonly AudioSampleBuffer Samples;
        public readonly bool StretchToTempo;
        public readonly double SourceDurSeconds; // source window length (s) a tempo-synced clip spans its beats
        public readonly double SourceOffsetSeconds;
        public readonly double FadeInBeats;
        public readonly double FadeOutBeats;
        public readonly PitchShifter[]? PitchShifters;
        public readonly WarpMap? Warp;
        public readonly WarpMode WarpMode;
        public readonly bool PitchCorrected;
        public readonly double AraPitchOffsetSemitones;
        public readonly IReadOnlyList<PitchNoteSegment> PitchSegments;
        public readonly float Gain;

        public double ReadPos;  // current read position in source frames
        public bool Started;    // false until the playhead first enters the clip (then ReadPos tracks live)

        // Restart from scratch — e.g. when the loop wraps the playhead back before the clip.
        public void Reset()
        {
            Started = false;
            ReadPos = 0;
            if (PitchShifters is { } shifters)
                foreach (var sh in shifters) sh.Reset();
        }
    }

    // One PSOLA pitch shifter per channel, configured at the device rate, for pitch-preserving stretch.
    private static PitchShifter[] BuildPitchShifters(int channels, int sampleRate)
    {
        var shifters = new PitchShifter[channels];
        for (var i = 0; i < channels; i++)
        {
            shifters[i] = new PitchShifter();
            shifters[i].Configure(sampleRate);
        }

        return shifters;
    }

    // Renders one audio clip for this block from its persistent read cursor. Because the cursor is advanced
    // continuously (rather than recomputed from the absolute beat each block), a changing tempo never jumps
    // the read position — the audio stays click-free through a tempo ramp. Tempo-synced clips advance their
    // source in lock-step with the playhead's (live) beat rate, so they speed up/slow down with tempo and
    // stay on the grid; other clips advance at their native rate (tempo-independent). At a constant tempo
    // this is sample-for-sample identical to <see cref="Mixing.RenderAudioClip"/>.
    private static void RenderClipBlock(Span<float> temp, AudioClipPlayback acp, double blockStartBeat,
        double samplesPerBeat, int deviceSampleRate, int channels, double bpm)
    {
        if (acp.Warp is { } warp && (warp.HasExplicitMarkers || acp.WarpMode != WarpMode.Beats))
        {
            Mixing.RenderWarpedAudioClip(temp, acp.Samples, warp, acp.StartBeat, acp.LengthBeats,
                blockStartBeat, samplesPerBeat, deviceSampleRate, channels, acp.WarpMode,
                acp.PitchCorrected, acp.FadeInBeats, acp.FadeOutBeats, acp.PitchShifters,
                acp.AraPitchOffsetSemitones, acp.PitchSegments);
            if (acp.Gain != 1f)
            {
                for (var i = 0; i < temp.Length; i++)
                    temp[i] *= acp.Gain;
            }

            return;
        }

        var samples = acp.Samples;
        var frameCount = samples.FrameCount;
        var frames = temp.Length / channels;
        var fileSampleRate = samples.SampleRate;
        var nativeRate = (double)fileSampleRate / deviceSampleRate;     // source frames per device frame, native speed
        var offsetFrames = acp.SourceOffsetSeconds * fileSampleRate;

        // Tempo-synced: the source window spans LengthBeats of the timeline, so the cursor moves
        // SourceFramesPerBeat each beat (tempo enters only through how fast the playhead crosses beats).
        var sourceFrames = acp.SourceDurSeconds * fileSampleRate;
        var framesPerBeatSynced = acp.LengthBeats > 0 ? sourceFrames / acp.LengthBeats : 0.0;
        var advanceSynced = samplesPerBeat > 0 ? framesPerBeatSynced / samplesPerBeat : 0.0;

        var shifters = acp.PitchShifters;
        var useSegments = AudioClipPitch.HasPitchSegments(acp.PitchSegments);
        var usePitch = shifters is not null
            && (acp.StretchToTempo || Math.Abs(acp.AraPitchOffsetSemitones) > 1e-6 || useSegments);
        var lastRatio = useSegments ? -1.0 : 0.0;
        if (usePitch && !useSegments)
        {
            var stretch = acp.StretchToTempo
                ? TempoSync.Stretch(acp.SourceDurSeconds, bpm, acp.LengthBeats)
                : 1.0;
            AudioClipPitch.ApplyRatios(shifters!, stretch, acp.AraPitchOffsetSemitones);
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var localBeat = blockStartBeat + frame / samplesPerBeat - acp.StartBeat;
            if (localBeat < 0) continue;       // playhead hasn't reached the clip yet
            if (localBeat >= acp.LengthBeats) break; // past the clip end

            if (!acp.Started)
            {
                // First frame inside the clip: seat the cursor at the matching source position (≈ the clip's
                // source offset when entering at the start; a real offset only when jumping in partway).
                acp.Started = true;
                acp.ReadPos = offsetFrames + (acp.StretchToTempo
                    ? localBeat * framesPerBeatSynced
                    : localBeat * samplesPerBeat * nativeRate);
            }

            var pos = acp.ReadPos;
            var f0 = (long)pos;
            if (f0 >= frameCount) break;

            if (usePitch && useSegments)
            {
                var stretch = acp.StretchToTempo
                    ? TempoSync.Stretch(acp.SourceDurSeconds, bpm, acp.LengthBeats)
                    : 1.0;
                var combined = AudioClipPitch.CombinedRatio(stretch, f0, acp.PitchSegments, acp.AraPitchOffsetSemitones);
                if (Math.Abs(combined - lastRatio) > 1e-5)
                {
                    AudioClipPitch.ApplyCombinedRatio(shifters!, combined);
                    lastRatio = combined;
                }
            }

            var frac = (float)(pos - f0);
            var gain = Crossfade.Gain(localBeat, acp.LengthBeats, acp.FadeInBeats, acp.FadeOutBeats) * acp.Gain;
            var baseIndex = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var fileChannel = c < samples.Channels ? c : samples.Channels - 1;
                var s0 = samples.Sample(f0, fileChannel);
                var s1 = samples.Sample(f0 + 1, fileChannel);
                var sample = s0 + (s1 - s0) * frac;
                if (usePitch) sample = shifters![c].Process(sample);
                temp[baseIndex + c] += sample * gain;
            }

            acp.ReadPos += acp.StretchToTempo ? advanceSynced : nativeRate;
        }
    }

    // A group/master mixing bus: an accumulation buffer that its children sum into, plus a link to the
    // parent bus it strips into (null for the master, which strips into the device output).
    private sealed class Bus
    {
        public Track Track = null!;
        public Bus? Parent;
        public float[] Buffer = Array.Empty<float>();
        public int Depth;
        public int PdcDelay;
        public PdcDelayLine PdcLine = new();

        public void EnsurePdc(int channels, int maxFrames)
        {
            PdcLine.Configure(PdcDelay, channels, maxFrames);
        }
    }

    // Immutable snapshot of the bus graph, swapped in atomically when the topology changes.
    private sealed class Routing
    {
        public Bus[] BusesDeepestFirst = Array.Empty<Bus>();
        public Dictionary<Guid, Bus> BusById = new();
        public Dictionary<Guid, Track> TrackById = new();
        public Bus? Master;
        public Track? MasterTrack;
        public TrackState[] ContentStates = Array.Empty<TrackState>();
        public HashSet<Guid> SidechainSources = new();
        public Dictionary<Guid, LatencyCompensator.Compensation> Pdc = new();
    }

    // Per-track MIDI note intervals built at playback start for skipping idle instrument tracks.
    private sealed class TrackActivityMap
    {
        public static readonly TrackActivityMap Empty = new(new Dictionary<Guid, (double On, double Off)[]>());

        private readonly Dictionary<Guid, (double On, double Off)[]> _intervals;

        private TrackActivityMap(Dictionary<Guid, (double On, double Off)[]> intervals) => _intervals = intervals;

        public static TrackActivityMap Build(ScheduledNote[] notes)
        {
            var building = new Dictionary<Guid, List<(double On, double Off)>>();
            foreach (var note in notes)
            {
                if (note.TrackId == Guid.Empty) continue;
                if (!building.TryGetValue(note.TrackId, out var list))
                {
                    list = new List<(double On, double Off)>();
                    building[note.TrackId] = list;
                }

                list.Add((note.OnBeat, note.OffBeat));
            }

            var intervals = new Dictionary<Guid, (double On, double Off)[]>(building.Count);
            foreach (var kv in building) intervals[kv.Key] = kv.Value.ToArray();
            return new TrackActivityMap(intervals);
        }

        public bool HasActivity(Guid trackId, double windowStart, double windowEnd)
        {
            if (!_intervals.TryGetValue(trackId, out var intervals)) return false;
            foreach (var (on, off) in intervals)
            {
                if (off > windowStart && on < windowEnd) return true;
            }

            return false;
        }
    }
}
