using System;
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

namespace Ongenet.Core.Audio;

/// <summary>
/// Default <see cref="IAudioEngine"/>. Mixes the project per track: each track renders its content
/// (an instrument's voices, or its audio clips while playing) into a scratch buffer, then a single
/// strip pass applies volume, constant-power pan, mute and solo while measuring that track's output
/// level. Instruments render every block (audible for live play); audio clips render only while the
/// transport is playing, sampled at the playhead. While playing, a sample-accurate sequencer fires
/// MIDI clip notes into the instruments. Per-track and master peak levels (with release ballistics)
/// are published for the UI meters.
/// </summary>
public sealed class AudioEngine : IAudioEngine
{
    private const float MeterRelease = 0.92f; // per-block decay → ~0.5s fall

    private readonly IAudioOutput _output;
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IAuditionPlayer _audition;

    private volatile Track[] _tracks = Array.Empty<Track>();

    private volatile bool _playing;
    private ScheduledNote[] _events = Array.Empty<ScheduledNote>();
    private AudioClipPlayback[] _audioClips = Array.Empty<AudioClipPlayback>();
    private readonly List<ScheduledNote> _active = new();
    private int _nextEvent;
    private double _currentBeat;
    private double _samplesPerBeat = 1;
    private double _arrangementEndBeat; // for whole-song auto-loop (snapshot at playback start)

    // --- Bus routing (groups + master). Rebuilt whenever the track topology changes; published as a
    //     single immutable snapshot so the audio thread always reads a consistent graph. ---
    private volatile Routing _routing = new();

    // --- Metronome count-in (recording pre-roll) ---
    private bool _countingIn;
    private long _countInElapsed;       // samples elapsed since the count-in began
    private long _countInTotalSamples;  // total count-in length in samples
    private int _countInClicks;         // clicks fired so far
    private int _countInClicksTotal;    // one click per count-in beat
    private int _beatsPerBar = 4;       // for the downbeat accent

    // Click oscillator (a short decaying sine added to the master bus).
    private int _clickRemaining;
    private int _clickTotal;
    private double _clickPhase;
    private double _clickPhaseInc;
    private float _clickAmp;

    private float[] _temp = Array.Empty<float>();
    private float[] _slotTemp = Array.Empty<float>(); // per-instrument render scratch (slot → slot fx → sum into _temp)
    private float _masterL;
    private float _masterR;

    // Per-block context + cross-track signal bus handed to effects that opt in via IContextualEffect.
    private readonly Effects.SidechainBus _sidechain = new();
    private readonly Effects.EffectContext _effectCtx = new();
    private bool _disposed;

    public AudioEngine(IAudioOutput output, IProjectService project, ITransportService transport,
        IEventAggregator events, IAuditionPlayer audition)
    {
        _output = output;
        _project = project;
        _transport = transport;
        _audition = audition;
        _project.ProjectChanged += RebuildTracks;
        _transport.StateChanged += OnTransportStateChanged;
        _output.FormatChanged += RebuildTracks; // re-prepare DSP when the device's sample rate changes
        events.Subscribe<TracksChangedEvent>(_ => RebuildTracks());
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

        _routing = new Routing
        {
            TrackById = trackById,
            BusById = busById,
            Master = master,
            MasterTrack = masterTrack,
            BusesDeepestFirst = busById.Values.OrderByDescending(b => b.Depth).ToArray()
        };
    }

    // The bus a track's output feeds into (its ParentId group, or the master when unset).
    private static Bus? ParentBusOf(Track track, Routing routing)
        => track.ParentId is { } pid && routing.BusById.TryGetValue(pid, out var b) ? b : routing.Master;

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
            AllNotesOff();
            _transport.NotifyPlayhead(_transport.StartBeat);
        }
    }

    private void BeginPlayback()
    {
        var sampleRate = _output.Format.SampleRate;
        var channels = _output.Format.Channels < 1 ? 1 : _output.Format.Channels;
        var bpm = _transport.Tempo.BeatsPerMinute;
        _samplesPerBeat = bpm > 0 ? sampleRate * 60.0 / bpm : sampleRate;
        var startBeat = _transport.StartBeat;

        var notes = new List<ScheduledNote>();
        var clips = new List<AudioClipPlayback>();

        foreach (var track in _tracks)
        {
            // MIDI clips drive the track's instrument (sound) and any MIDI-aware insert effect (gestures).
            var midiFx = MidiEffectsOf(track);
            if (track.Kind == TrackKind.Instrument && track.ActiveInstruments.Length > 0)
            {
                var slots = track.ActiveInstruments;
                foreach (var clip in track.Clips)
                {
                    if (!clip.IsMidi) continue;
                    foreach (var note in clip.Notes)
                    {
                        var onBeat = clip.StartBeat + note.StartBeat;
                        var offBeat = onBeat + note.LengthBeats;
                        if (offBeat <= startBeat) continue;
                        notes.Add(new ScheduledNote(onBeat, offBeat, slots, midiFx, note.Note, note.Velocity));
                    }
                }
            }
            else if (midiFx.Length > 0 && track.Kind != TrackKind.Audio)
            {
                // A non-instrument track (e.g. a bus) carrying MIDI clips can still trigger its effects.
                foreach (var clip in track.Clips)
                {
                    if (!clip.IsMidi) continue;
                    foreach (var note in clip.Notes)
                    {
                        var onBeat = clip.StartBeat + note.StartBeat;
                        var offBeat = onBeat + note.LengthBeats;
                        if (offBeat <= startBeat) continue;
                        notes.Add(new ScheduledNote(onBeat, offBeat, null, midiFx, note.Note, note.Velocity));
                    }
                }
            }

            if (track.Kind == TrackKind.Audio)
            {
                // Crossfades for overlapping clips on this track (per-clip fade gains, independent of the strip).
                var fades = Crossfade.Compute(track.Clips);
                foreach (var clip in track.Clips)
                {
                    if (clip.Samples is { } samples && clip.EndBeat > startBeat)
                    {
                        // Tempo-synced clips resample so the clip's source window spans its beat-length at
                        // the current tempo (keeps loops on the grid); others play at native speed. A sliced
                        // clip only spans part of the source (SourceLengthSeconds), so stretch off that.
                        var sourceDur = clip.SourceLengthSeconds
                            ?? Math.Max(0.0, samples.FrameCount / (double)samples.SampleRate - clip.SourceOffsetSeconds);
                        var fade = fades.TryGetValue(clip, out var f) ? f : (FadeInBeats: 0.0, FadeOutBeats: 0.0);
                        // Pitch-preserving stretch: one PSOLA shifter per channel, configured at the device rate.
                        var shifters = clip is { PitchCorrected: true, StretchToTempo: true }
                            ? BuildPitchShifters(channels, sampleRate)
                            : null;
                        // Store the stretch *inputs* (source window + flag), not a fixed ratio: the block
                        // renderer derives the read rate from the live tempo so speed follows tempo automation.
                        clips.Add(new AudioClipPlayback(track, clip.StartBeat, clip.LengthBeats, samples,
                            clip.StretchToTempo, sourceDur, clip.SourceOffsetSeconds,
                            fade.FadeInBeats, fade.FadeOutBeats, shifters));
                    }
                }
            }
        }

        notes.Sort((a, b) => a.OnBeat.CompareTo(b.OnBeat));

        // Snapshot the arrangement end (max of the user-set bar count and the furthest clip) so the
        // engine can auto-loop the whole song when there's no explicit loop region.
        var beatsPerBar = Math.Max(1, _project.Current.TimeSignature.Numerator);
        var contentEnd = 0.0;
        foreach (var track in _tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.EndBeat > contentEnd) contentEnd = clip.EndBeat;
            }
        }

        _arrangementEndBeat = Math.Max(_project.Current.BarCount * (double)beatsPerBar, contentEnd);

        _active.Clear();
        _currentBeat = startBeat;
        _nextEvent = 0;
        while (_nextEvent < notes.Count && notes[_nextEvent].OnBeat < startBeat) _nextEvent++;

        _events = notes.ToArray();
        _audioClips = clips.ToArray();

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

    private void Render(Span<float> buffer)
    {
        buffer.Clear();
        if (_temp.Length < buffer.Length) _temp = new float[buffer.Length];
        if (_slotTemp.Length < buffer.Length) _slotTemp = new float[buffer.Length];

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

        if (playing) ScheduleNotes(curBeat);

        var tracks = _tracks;
        var routing = _routing;
        var buses = routing.BusesDeepestFirst;

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
        }

        var soloActive = false;
        foreach (var track in tracks)
        {
            if (track.IsSoloed) { soloActive = true; break; }
        }

        var temp = _temp.AsSpan(0, buffer.Length);

        // Per-block context for tempo-aware / sidechain effects (the bus's tap buffers persist across blocks).
        _effectCtx.Format = _output.Format;
        // Tempo-aware effects (e.g. Stuttero) see the same live, automation-driven tempo the playhead uses;
        // when stopped this falls back to the manual transport tempo so live audition still has a real BPM.
        _effectCtx.Bpm = effectiveBpm;
        _effectCtx.PlayheadBeats = prevBeat;
        _effectCtx.Playing = playing;
        _effectCtx.Sidechain = _sidechain;

        // 1) Content tracks: render → insert effects → strip → sum into their parent bus.
        foreach (var track in tracks)
        {
            if (track.IsBus) continue;

            if (IsSilenced(track, soloActive, routing))
            {
                track.MeterLevel *= MeterRelease;
                // A muted / soloed-out track is still rendered when something taps it as a sidechain source
                // (e.g. Live Difference for vocal isolation, or a ducking trigger): its real signal is
                // published to the bus but NOT mixed into the output, so it can drive an effect while staying
                // silent. When nothing requests it, RenderTrackContent leaves `temp` cleared (publishes silence).
                if (_sidechain.IsRequested(track.Id))
                {
                    RenderTrackContent(track, temp, buffer.Length, channels, prevBeat, sampleRate, effectiveBpm, playing);
                    _sidechain.Publish(track.Id, temp, channels);
                }

                continue;
            }

            var hasContent = RenderTrackContent(track, temp, buffer.Length, channels, prevBeat, sampleRate, effectiveBpm, playing);

            // Make this track's post-effect output available to any sidechain consumer that asked for it.
            if (_sidechain.IsRequested(track.Id)) _sidechain.Publish(track.Id, temp, channels);

            if (!hasContent)
            {
                track.MeterLevel *= MeterRelease;
                continue;
            }

            // Sum into the parent bus (or straight to the device buffer if there is no master bus).
            var parent = ParentBusOf(track, routing);
            var target = parent is not null ? parent.Buffer.AsSpan(0, buffer.Length) : buffer;
            var (lg, rg) = Mixing.StripGains(track.Volume, track.Pan);
            track.MeterLevel = MixInto(target, temp, lg, rg, channels, frames, track.MeterLevel);
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

            var target = bus.Parent is not null ? bus.Parent.Buffer.AsSpan(0, buffer.Length) : buffer;
            var (lg, rg) = Mixing.BusGains(bt.Volume, bt.Pan);
            bt.MeterLevel = MixInto(target, busSpan, lg, rg, channels, frames, bt.MeterLevel);
        }

        if (playing)
        {
            _currentBeat = curBeat;
            _transport.NotifyPlayhead(curBeat);
        }

        // Metronome clicks (triggered during the count-in) are added to the master bus.
        RenderMetronome(buffer, frames, channels);

        // Library/file audition preview (independent of the transport) mixes into the master.
        _audition.Mix(buffer, _output.Format);

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
    }

    private static float MaxWithRelease(float peak, float current)
    {
        var decayed = current * MeterRelease;
        return peak > decayed ? peak : decayed;
    }

    // Renders a content track's signal into <paramref name="temp"/>: its instrument rack (each enabled slot
    // through its own pre-effect chain) or its audio clips, then the track's insert (post) effects. Returns
    // whether anything was rendered. Shared by the normal path and the "render a silenced sidechain source"
    // path, so a muted/soloed-out track can still feed an effect (Live Difference, ducking) without being heard.
    private bool RenderTrackContent(Track track, Span<float> temp, int bufferLength, int channels,
        double prevBeat, int sampleRate, double effectiveBpm, bool playing)
    {
        var hasContent = false;
        temp.Clear();

        var slots = track.ActiveInstruments;
        if (slots.Length > 0)
        {
            var slotTemp = _slotTemp.AsSpan(0, bufferLength);
            foreach (var slot in slots)
            {
                if (!slot.Enabled) continue;
                slotTemp.Clear();
                slot.Instrument.Render(slotTemp);
                foreach (var fx in slot.ActiveEffects)
                {
                    if (!fx.Enabled) continue;
                    if (fx is Effects.IContextualEffect cae) cae.SetContext(_effectCtx);
                    fx.Process(slotTemp);
                }

                for (var i = 0; i < slotTemp.Length; i++) temp[i] += slotTemp[i];
                hasContent = true;
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

        // Run the track's insert effects (e.g. reverb) before the strip. Effects run even on a near-silent
        // block so tails ring out; only skip when the track has no effects.
        var effects = track.ActiveEffects;
        if (effects.Length > 0)
        {
            foreach (var fx in effects)
            {
                if (!fx.Enabled) continue;
                if (fx is Effects.IContextualEffect cae) cae.SetContext(_effectCtx);
                fx.Process(temp);
            }

            hasContent = true;
        }

        return hasContent;
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

    // Resets playback to <paramref name="target"/> for a loop: silences sounding notes and re-points the
    // note scheduler at the first event from there. Audio clips re-render from the new beat automatically.
    private void WrapPlayback(double target)
    {
        for (var i = 0; i < _active.Count; i++) _active[i].Fire(on: false);
        _active.Clear();
        AllNotesOff();

        // Restart each clip's read cursor (and pitch-shifter tail) so a looped clip reads cleanly from the
        // wrap point rather than continuing from a stale position.
        foreach (var acp in _audioClips) acp.Reset();

        _currentBeat = target;
        _nextEvent = 0;
        var events = _events;
        while (_nextEvent < events.Length && events[_nextEvent].OnBeat < target) _nextEvent++;
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

    private void ScheduleNotes(double curBeat)
    {
        var events = _events;
        while (_nextEvent < events.Length && events[_nextEvent].OnBeat < curBeat)
        {
            var ev = events[_nextEvent];
            ev.Fire(on: true);
            _active.Add(ev);
            _nextEvent++;
        }

        for (var i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].OffBeat <= curBeat)
            {
                _active[i].Fire(on: false);
                _active.RemoveAt(i);
            }
        }
    }

    // Advances the count-in by one block: fires a click at each beat boundary (accented on the
    // downbeat) and, once the full pre-roll has elapsed, hands over to content playback.
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
        var master = _routing.MasterTrack;
        if (master is not null)
        {
            var recording = _transport.IsRecording;
            foreach (var lane in master.ActiveAutoLanes)
            {
                if (lane.Binding?.Kind != AutomationTargetKind.Tempo) continue;
                if (recording && lane.IsArmed) break;
                return Math.Clamp(lane.Evaluate(beat),
                    ProjectAutomationTargets.MinBpm, ProjectAutomationTargets.MaxBpm);
            }
        }

        return fallback > 0 ? fallback : 120.0;
    }

    // Drives each automation lane's target from its curve at the current beat. Armed lanes are
    // left alone while recording so the user's manual control moves are captured, not overwritten.
    private void ApplyAutomation(Track track, double beat)
    {
        var lanes = track.ActiveAutoLanes;
        if (lanes.Length == 0) return;
        var recording = _transport.IsRecording;
        foreach (var lane in lanes)
        {
            if (recording && lane.IsArmed) continue;
            lane.Target.Write(lane.Evaluate(beat));
        }
    }

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
        _project.ProjectChanged -= RebuildTracks;
        _transport.StateChanged -= OnTransportStateChanged;
        _output.FormatChanged -= RebuildTracks;
        _output.Stop();
        _output.Dispose();
    }

    // A scheduled note targets a track's instrument rack (sound — every enabled slot) and/or its
    // MIDI-aware effects (gestures); either may be absent. Slots and MidiEffects are the track's
    // snapshots, captured when playback began (slot.Enabled is read live so toggles take effect).
    private readonly record struct ScheduledNote(double OnBeat, double OffBeat, InstrumentSlot[]? Slots,
        IMidiAwareEffect[] MidiEffects, int Note, float Velocity)
    {
        public void Fire(bool on)
        {
            if (Slots is not null)
            {
                foreach (var slot in Slots)
                {
                    if (!slot.Enabled) continue;
                    if (on) slot.Instrument.NoteOn(Note, Velocity);
                    else slot.Instrument.NoteOff(Note);
                }
            }

            if (MidiEffects.Length == 0) return;
            var vel = (byte)Math.Clamp((int)(Velocity * 127f), 0, 127);
            var msg = new MidiMessage(on ? MidiMessageKind.NoteOn : MidiMessageKind.NoteOff, 0, (byte)Note, on ? vel : (byte)0);
            foreach (var fx in MidiEffects) fx.HandleMidi(msg);
        }
    }

    // A scheduled audio clip, plus the running read state the block renderer keeps across blocks. The cursor
    // (ReadPos) is advanced continuously so a changing tempo never jumps the source read position.
    private sealed class AudioClipPlayback
    {
        public AudioClipPlayback(Track track, double startBeat, double lengthBeats, AudioSampleBuffer samples,
            bool stretchToTempo, double sourceDurSeconds, double sourceOffsetSeconds,
            double fadeInBeats, double fadeOutBeats, PitchShifter[]? pitchShifters)
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

        // Pitch-preserving stretch: undo the (live) stretch ratio so pitch holds while the clip is time-stretched.
        var shifters = acp.PitchShifters;
        var usePitch = shifters is not null && acp.StretchToTempo;
        if (usePitch)
        {
            var stretch = TempoSync.Stretch(acp.SourceDurSeconds, bpm, acp.LengthBeats);
            var pitchRatio = stretch > 0 ? 1.0 / stretch : 1.0;
            foreach (var sh in shifters!) sh.SetRatio(pitchRatio);
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

            var frac = (float)(pos - f0);
            var gain = Crossfade.Gain(localBeat, acp.LengthBeats, acp.FadeInBeats, acp.FadeOutBeats);
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
    }

    // Immutable snapshot of the bus graph, swapped in atomically when the topology changes.
    private sealed class Routing
    {
        public Bus[] BusesDeepestFirst = Array.Empty<Bus>();
        public Dictionary<Guid, Bus> BusById = new();
        public Dictionary<Guid, Track> TrackById = new();
        public Bus? Master;
        public Track? MasterTrack;
    }
}
