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

namespace Ongenet.Core.Audio;

/// <summary>
/// Renders a whole project to a WAV file offline (faster than real time), using the same mixing
/// maths as the live <see cref="AudioEngine"/>. It works on <b>clones</b> of each track's instrument
/// and effects, so rendering never disturbs live playback or shares voice/effect state.
/// </summary>
public sealed class OfflineRenderer
{
    private const int BlockFrames = 512;
    private const double TailSeconds = 2.0; // let instrument/effect tails ring out

    /// <summary>Renders the project to <paramref name="path"/> as a 16-bit PCM WAV.
    /// <paramref name="progress"/> (optional) receives the completed fraction, 0..1, at most once
    /// per whole percent so UI marshalling stays cheap.</summary>
    public void RenderToWav(Project project, AudioFormat format, double bpm, string path,
        IProgress<double>? progress = null)
    {
        var session = new RenderSession();
        session.Build(project, format, bpm, scope: null);
        using var writer = new WavWriter(path, session.Channels, session.SampleRate);
        session.RenderToWriter(writer, progress);
    }

    /// <summary>
    /// Offline-renders a scoped beat region through slot, track, and ancestor group FX (excluding master),
    /// returning interleaved PCM at the engine format sample rate.
    /// </summary>
    public AudioSampleBuffer RenderScopeToBuffer(Project project, AudioFormat format, double bpm,
        ClipRenderScope scope, IProgress<double>? progress = null)
    {
        var session = new RenderSession();
        session.Build(project, format, bpm, scope);
        return session.RenderScopeToBuffer(progress);
    }

    private sealed class RenderSession
    {
        private Project _project = null!;
        private AudioFormat _format;
        private double _fallbackBpm;
        private ClipRenderScope? _scope;
        private int _channels;
        private int _sampleRate;
        private long _totalFrames;
        private double _startBeat;
        private HashSet<Guid> _contentTrackIds = new();
        private HashSet<Guid> _sidechainOnlyIds = new();
        private HashSet<Guid> _includedTrackIds = new();

        private readonly List<RenderTrack> _renderTracks = new();
        private readonly List<ScheduledNote> _events = new();
        private readonly List<RenderBus> _buses = new();
        private Dictionary<Guid, RenderBus> _busByTrackId = new();
        private RenderBus? _masterBus;
        private Track? _masterTrack;
        private Dictionary<Guid, Track> _trackById = new();

        public int Channels => _channels;
        public int SampleRate => _sampleRate;

        public void Build(Project project, AudioFormat format, double bpm, ClipRenderScope? scope)
        {
            _project = project;
            _format = format;
            _fallbackBpm = bpm;
            _scope = scope;
            _channels = format.Channels < 1 ? 1 : format.Channels;
            _sampleRate = format.SampleRate;
            _startBeat = scope?.StartBeat ?? 0;
            _trackById = project.Tracks.ToDictionary(t => t.Id);

            if (scope is not null)
            {
                foreach (var kv in scope.ContentByTrack) _contentTrackIds.Add(kv.Key.Id);
                foreach (var id in scope.SidechainSourceIds)
                {
                    if (!_contentTrackIds.Contains(id)) _sidechainOnlyIds.Add(id);
                }

                _includedTrackIds = new HashSet<Guid>(_contentTrackIds);
                foreach (var id in _sidechainOnlyIds) _includedTrackIds.Add(id);
                foreach (var track in scope.ContentByTrack.Keys)
                    AddAncestorGroups(track, scope.TapAfterGroupId);
                if (scope.TapAfterGroupId is { } tap) _includedTrackIds.Add(tap);
            }

            _renderTracks.Clear();
            _events.Clear();

            foreach (var track in project.Tracks)
            {
                if (scope is not null && !_includedTrackIds.Contains(track.Id)) continue;

                var clips = scope?.ContentByTrack.GetValueOrDefault(track);
                var rt = BuildRenderTrack(track, clips);
                _renderTracks.Add(rt);
            }

            _events.Sort((a, b) => a.OnBeat.CompareTo(b.OnBeat));

            _busByTrackId = new Dictionary<Guid, RenderBus>();
            _buses.Clear();
            foreach (var rt in _renderTracks)
            {
                if (!rt.Source.IsBus) continue;
                var rb = new RenderBus(rt) { Buffer = new float[BlockFrames * _channels] };
                _busByTrackId[rt.Source.Id] = rb;
                _buses.Add(rb);
            }

            _masterBus = _buses.FirstOrDefault(b => b.Track.Source.Kind == TrackKind.Master);
            _masterTrack = _masterBus?.Track.Source;
            foreach (var rb in _buses)
            {
                rb.Parent = rb.Track.Source.Kind == TrackKind.Master ? null
                    : rb.Track.Source.ParentId is { } pid && _busByTrackId.TryGetValue(pid, out var p) ? p : _masterBus;
            }

            foreach (var rb in _buses)
            {
                var d = 0;
                var p = rb.Parent;
                while (p is not null && d < 64) { d++; p = p.Parent; }
                rb.Depth = d;
            }

            _buses.Sort((a, b) => b.Depth.CompareTo(a.Depth));

            if (scope is not null)
            {
                var samplesPerBeat = bpm > 0 ? _sampleRate * 60.0 / bpm : _sampleRate;
                _totalFrames = (long)Math.Ceiling(scope.LengthBeats * samplesPerBeat);
            }
            else
            {
                var beatsPerBar = Math.Max(1, project.TimeSignature.Numerator);
                var contentEnd = project.Tracks.SelectMany(t => t.Clips).Select(c => c.EndBeat).DefaultIfEmpty(0).Max();
                var renderBeats = Math.Max(project.BarCount * beatsPerBar, contentEnd);
                var samplesPerBeat = bpm > 0 ? _sampleRate * 60.0 / bpm : _sampleRate;
                _totalFrames = (long)(renderBeats * samplesPerBeat) + (long)(TailSeconds * _sampleRate);
            }
        }

        private void AddAncestorGroups(Track track, Guid? stopAtInclusive)
        {
            var pid = track.ParentId;
            var guard = 0;
            while (pid is { } gid && guard++ < 64)
            {
                _includedTrackIds.Add(gid);
                if (stopAtInclusive is not null && gid == stopAtInclusive) break;
                pid = _trackById.GetValueOrDefault(gid)?.ParentId;
            }
        }

        private RenderTrack BuildRenderTrack(Track track, IReadOnlyList<Clip>? scopedClips)
        {
            var rt = new RenderTrack(track);
            rt.Effects = track.Effects.Select(e => { var c = e.Clone(); c.Prepare(_format); return c; }).ToArray();
            var midiFx = MidiEffectsOf(rt.Effects);

            var midiClips = scopedClips is not null
                ? scopedClips.Where(c => c.IsMidi)
                : track.Clips.Where(c => c.IsMidi);

            if (track.Kind == TrackKind.Instrument && track.Instruments.Count > 0)
            {
                foreach (var s in track.Instruments)
                {
                    var inst = s.Instrument.Clone();
                    inst.Prepare(_format);
                    var slotFx = s.Effects.Select(e => { var c = e.Clone(); c.Prepare(_format); return c; }).ToArray();
                    rt.Slots.Add(new RenderSlot(inst, s.Enabled, slotFx, s));
                }

                var slots = rt.Slots.ToArray();
                foreach (var clip in midiClips)
                {
                    foreach (var note in clip.Notes)
                    {
                        var onBeat = clip.StartBeat + note.StartBeat;
                        _events.Add(new ScheduledNote(onBeat, onBeat + note.LengthBeats, slots, midiFx, note.Note, note.Velocity));
                    }
                }
            }
            else if (midiFx.Length > 0 && track.Kind != TrackKind.Audio)
            {
                foreach (var clip in midiClips)
                {
                    foreach (var note in clip.Notes)
                    {
                        var onBeat = clip.StartBeat + note.StartBeat;
                        _events.Add(new ScheduledNote(onBeat, onBeat + note.LengthBeats, null, midiFx, note.Note, note.Velocity));
                    }
                }
            }

            if (track.Kind == TrackKind.Audio)
            {
                var audioClips = scopedClips is not null
                    ? scopedClips.Where(c => c.IsAudio).ToList()
                    : track.Clips.Where(c => c.IsAudio).ToList();
                var fades = Crossfade.Compute(audioClips);
                foreach (var clip in audioClips)
                {
                    if (clip.Samples is not { } samples) continue;
                    var sourceDur = clip.SourceLengthSeconds
                        ?? Math.Max(0.0, samples.FrameCount / (double)samples.SampleRate - clip.SourceOffsetSeconds);
                    var stretch = clip.StretchToTempo
                        ? TempoSync.Stretch(sourceDur, _fallbackBpm, clip.LengthBeats)
                        : 1.0;
                    var fade = fades.TryGetValue(clip, out var f) ? f : (FadeInBeats: 0.0, FadeOutBeats: 0.0);
                    PitchShifter[]? shifters = null;
                    if (clip is { PitchCorrected: true, StretchToTempo: true })
                    {
                        shifters = new PitchShifter[_channels];
                        for (var i = 0; i < _channels; i++) { shifters[i] = new PitchShifter(); shifters[i].Configure(_sampleRate); }
                    }

                    rt.AudioClips.Add((clip.StartBeat, clip.LengthBeats, samples, stretch, clip.SourceOffsetSeconds,
                        fade.FadeInBeats, fade.FadeOutBeats, shifters));
                }
            }

            return rt;
        }

        public void RenderToWriter(WavWriter writer, IProgress<double>? progress)
        {
            RenderCore(
                block => writer.Write(block),
                progress);
        }

        public AudioSampleBuffer RenderScopeToBuffer(IProgress<double>? progress)
        {
            var output = new float[_totalFrames * _channels];
            var framesWritten = 0L;
            RenderCore(
                block =>
                {
                    var frames = block.Length / _channels;
                    block.CopyTo(output.AsSpan((int)(framesWritten * _channels), block.Length));
                    framesWritten += frames;
                },
                progress);
            return new AudioSampleBuffer(output, _channels, _sampleRate);
        }

        private void RenderCore(Action<ReadOnlySpan<float>> writeBlock, IProgress<double>? progress)
        {
            var soloActive = _scope is null && _project.Tracks.Any(t => t.IsSoloed);
            var block = new float[BlockFrames * _channels];
            var temp = new float[BlockFrames * _channels];
            var slotTemp = new float[BlockFrames * _channels];
            var active = new List<ScheduledNote>();
            var nextEvent = 0;
            var currentBeat = _startBeat;
            long framesWritten = 0;
            var lastPercent = -1;

            var sidechain = new SidechainBus();
            var ctx = new EffectContext { Format = _format, Bpm = _fallbackBpm, Playing = true, Sidechain = sidechain };

            while (framesWritten < _totalFrames)
            {
                var bpm = OfflineAutomationDriver.ResolveTempo(_project, currentBeat, _fallbackBpm);
                var samplesPerBeat = bpm > 0 ? _sampleRate * 60.0 / bpm : _sampleRate;
                var framesThisBlock = (int)Math.Min(BlockFrames, _totalFrames - framesWritten);
                var sampleCount = framesThisBlock * _channels;
                var blockSpan = block.AsSpan(0, sampleCount);
                blockSpan.Clear();

                var prevBeat = currentBeat;
                currentBeat = prevBeat + framesThisBlock / samplesPerBeat;
                ctx.Bpm = bpm;
                ctx.PlayheadBeats = prevBeat;
                sidechain.BeginBlock();
                if (_scope is not null)
                {
                    foreach (var id in _sidechainOnlyIds) sidechain.Request(id);
                }
                else
                {
                    foreach (var id in OfflineAutomationDriver.CollectSidechainSources(_project.Tracks))
                        sidechain.Request(id);
                }

                foreach (var track in _project.Tracks)
                    OfflineAutomationDriver.ApplyTrack(track, prevBeat);

                foreach (var rt in _renderTracks)
                {
                    OfflineAutomationDriver.SyncEffects(rt.Source.ActiveEffects, rt.Effects);
                    if (rt.Source.Kind == TrackKind.Instrument)
                    {
                        for (var i = 0; i < rt.Slots.Count; i++)
                        {
                            var slot = rt.Slots[i];
                            slot.Enabled = slot.Live.Enabled;
                            OfflineAutomationDriver.SyncEffects(slot.Live.ActiveEffects, slot.Effects);
                            OfflineAutomationDriver.SyncInstrument(slot.Live.Instrument, slot.Instrument);
                        }
                    }
                }

                while (nextEvent < _events.Count && _events[nextEvent].OnBeat < currentBeat)
                {
                    var ev = _events[nextEvent++];
                    ev.Fire(on: true);
                    active.Add(ev);
                }

                for (var i = active.Count - 1; i >= 0; i--)
                {
                    if (active[i].OffBeat <= currentBeat)
                    {
                        active[i].Fire(on: false);
                        active.RemoveAt(i);
                    }
                }

                foreach (var rb in _buses) Array.Clear(rb.Buffer, 0, sampleCount);

                foreach (var rt in _renderTracks)
                {
                    if (rt.Source.IsBus) continue;
                    var silenced = IsSilenced(rt.Source, soloActive);
                    if (silenced && !sidechain.IsRequested(rt.Source.Id)) continue;

                    var tempSpan = temp.AsSpan(0, sampleCount);
                    tempSpan.Clear();
                    var hasContent = false;

                    if (rt.Slots.Count > 0)
                    {
                        var slotSpan = slotTemp.AsSpan(0, sampleCount);
                        foreach (var slot in rt.Slots)
                        {
                            if (!slot.Enabled) continue;
                            slotSpan.Clear();
                            slot.Instrument.Render(slotSpan);
                            foreach (var fx in slot.Effects)
                            {
                                if (!fx.Enabled) continue;
                                if (fx is IContextualEffect cae) cae.SetContext(ctx);
                                fx.Process(slotSpan);
                            }

                            for (var i = 0; i < slotSpan.Length; i++) tempSpan[i] += slotSpan[i];
                            hasContent = true;
                        }
                    }
                    else
                    {
                        foreach (var (start, length, samples, stretch, sourceOffset, fadeIn, fadeOut, shifters) in rt.AudioClips)
                        {
                            Mixing.RenderAudioClip(tempSpan, samples, start, length, prevBeat, samplesPerBeat, _sampleRate, _channels,
                                stretch, sourceOffset, fadeIn, fadeOut, shifters);
                            hasContent = true;
                        }
                    }

                    if (rt.Effects.Length > 0)
                    {
                        foreach (var fx in rt.Effects)
                        {
                            if (!fx.Enabled) continue;
                            if (fx is IContextualEffect cae) cae.SetContext(ctx);
                            fx.Process(tempSpan);
                        }

                        hasContent = true;
                    }

                    if (sidechain.IsRequested(rt.Source.Id)) sidechain.Publish(rt.Source.Id, tempSpan, _channels);

                    if (_scope is not null && _scope.TapAfterGroupId is null && _contentTrackIds.Contains(rt.Source.Id))
                    {
                        ApplyStripGains(tempSpan, rt.Source.Volume, rt.Source.Pan, _channels, framesThisBlock);
                        writeBlock(tempSpan);
                        continue;
                    }

                    var sidechainOnly = _scope is not null && _sidechainOnlyIds.Contains(rt.Source.Id);
                    if ((silenced && !sidechainOnly) || !hasContent) continue;

                    var parent = rt.Source.ParentId is { } pid && _busByTrackId.TryGetValue(pid, out var pb) ? pb : _masterBus;
                    var target = parent is not null ? parent.Buffer.AsSpan(0, sampleCount) : blockSpan;
                    var (lg, rg) = Mixing.StripGains(rt.Source.Volume, rt.Source.Pan);
                    MixIntoBlock(target, tempSpan, lg, rg, _channels, framesThisBlock);
                }

                foreach (var rb in _buses)
                {
                    var bt = rb.Track.Source;
                    if (_scope is null && bt.IsMuted) continue;
                    if (_scope is not null && !_includedTrackIds.Contains(bt.Id)) continue;

                    var busSpan = rb.Buffer.AsSpan(0, sampleCount);
                    if (rb.Track.Effects.Length > 0)
                    {
                        foreach (var fx in rb.Track.Effects)
                        {
                            if (!fx.Enabled) continue;
                            if (fx is IContextualEffect cae) cae.SetContext(ctx);
                            fx.Process(busSpan);
                        }
                    }

                    if (sidechain.IsRequested(bt.Id)) sidechain.Publish(bt.Id, busSpan, _channels);

                    if (_scope is not null && _scope.TapAfterGroupId == bt.Id)
                    {
                        ApplyBusGains(busSpan, bt.Volume, bt.Pan, _channels, framesThisBlock);
                        writeBlock(busSpan);
                        continue;
                    }

                    if (_scope is null && bt.IsMuted) continue;

                    var target = rb.Parent is not null ? rb.Parent.Buffer.AsSpan(0, sampleCount) : blockSpan;
                    var (lg, rg) = Mixing.BusGains(bt.Volume, bt.Pan);
                    MixIntoBlock(target, busSpan, lg, rg, _channels, framesThisBlock);
                }

                if (_scope is null)
                {
                    writeBlock(blockSpan);
                }

                framesWritten += framesThisBlock;

                if (progress is not null)
                {
                    var percent = (int)(framesWritten * 100 / _totalFrames);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress.Report(framesWritten / (double)_totalFrames);
                    }
                }
            }

            progress?.Report(1.0);
        }

        private bool IsSilenced(Track track, bool soloActive)
        {
            if (_scope is not null)
            {
                if (_contentTrackIds.Contains(track.Id)) return false;
                return _sidechainOnlyIds.Contains(track.Id) || track.IsMuted;
            }

            if (track.IsMuted) return true;
            return soloActive && !(track.IsSoloed || AncestorSoloed(track));
        }

        private bool AncestorSoloed(Track t)
        {
            var p = ParentTrack(t);
            var n = 0;
            while (p is not null && n++ < 64) { if (p.IsSoloed) return true; p = ParentTrack(p); }
            return false;
        }

        private Track? ParentTrack(Track t)
        {
            if (t.Kind == TrackKind.Master) return null;
            if (t.ParentId is { } id && _trackById.TryGetValue(id, out var p)) return p;
            return _masterTrack;
        }
    }

    private static void MixIntoBlock(Span<float> target, Span<float> source, float leftGain, float rightGain,
        int channels, int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                target[i + c] += source[i + c] * Mixing.ChannelGain(c, leftGain, rightGain);
            }
        }
    }

    private static void ApplyStripGains(Span<float> buffer, double volume, double pan, int channels, int frames)
    {
        var (lg, rg) = Mixing.StripGains(volume, pan);
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
                buffer[i + c] *= Mixing.ChannelGain(c, lg, rg);
        }
    }

    private static void ApplyBusGains(Span<float> buffer, double volume, double pan, int channels, int frames)
    {
        var (lg, rg) = Mixing.BusGains(volume, pan);
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
                buffer[i + c] *= Mixing.ChannelGain(c, lg, rg);
        }
    }

    private sealed class RenderTrack
    {
        public RenderTrack(Track source) => Source = source;
        public Track Source { get; }
        public List<RenderSlot> Slots { get; } = new();
        public IAudioEffect[] Effects { get; set; } = Array.Empty<IAudioEffect>();
        public List<(double Start, double Length, AudioSampleBuffer Samples, double Stretch, double SourceOffset, double FadeInBeats, double FadeOutBeats, Dsp.PitchShifter[]? PitchShifters)> AudioClips { get; } = new();
    }

    private sealed class RenderSlot
    {
        public RenderSlot(IInstrument instrument, bool enabled, IAudioEffect[] effects, InstrumentSlot live)
        {
            Instrument = instrument;
            Enabled = enabled;
            Effects = effects;
            Live = live;
        }

        public IInstrument Instrument { get; }
        public bool Enabled { get; set; }
        public IAudioEffect[] Effects { get; }
        public InstrumentSlot Live { get; }
    }

    private sealed class RenderBus
    {
        public RenderBus(RenderTrack track) => Track = track;
        public RenderTrack Track { get; }
        public RenderBus? Parent { get; set; }
        public float[] Buffer { get; set; } = Array.Empty<float>();
        public int Depth { get; set; }
    }

    private static IMidiAwareEffect[] MidiEffectsOf(IAudioEffect[] effects)
        => effects.OfType<IMidiAwareEffect>().ToArray();

    private readonly record struct ScheduledNote(double OnBeat, double OffBeat, RenderSlot[]? Slots,
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
}
