using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// An immutable, ready-to-run snapshot of a <see cref="FieldGraph"/> produced by
/// <see cref="FieldGraphCompiler"/>. It owns a preallocated buffer pool and a
/// <see cref="FieldVoiceManager"/>, and renders a block with no audio-thread allocation. The audio thread
/// only ever reads a <see cref="CompiledGraph"/> that a host swapped in via a <c>volatile</c> field, so
/// recompilation on the UI thread is safe.
///
/// Processing walks the nodes in topological order. Per-voice nodes (oscillators, envelopes, per-voice
/// filters) run once for each active voice into per-voice output buffers; global nodes (reverb, master,
/// audio out) run once and sum any per-voice inputs across the active voices — that summation is the
/// voice-collecting boundary. Feedback edges read the previous block's buffer (a one-block delay).
/// </summary>
public sealed class CompiledGraph
{
    internal readonly struct InEdge
    {
        public readonly int SrcNode;      // compiled node index
        public readonly int SrcOutBase;   // base slot in _outBuf
        public readonly bool SrcPerVoice; // whether the source has per-voice buffers
        public readonly bool Audio;       // audio-kind edge (drives voice-lifetime peak tracking)

        public InEdge(int srcNode, int srcOutBase, bool srcPerVoice, bool audio)
        {
            SrcNode = srcNode;
            SrcOutBase = srcOutBase;
            SrcPerVoice = srcPerVoice;
            Audio = audio;
        }
    }

    internal sealed class CompiledNode
    {
        public required FieldNode Node;
        public bool PerVoice;
        public required InEdge[][] InEdges;   // [inputPort][edge]
        public required int[] OutSlotBase;    // [outputPort] -> base index into _outBuf
        public required float[][] InputBuffers;  // rebound each call
        public required float[][] OutputBuffers; // rebound each call
        public required float[]?[] ModByParam;   // rebound each call, indexed by parameter
    }

    private readonly CompiledNode[] _order;
    private readonly List<IFieldNoteReceiver> _receivers = new();
    private readonly float[][] _outBuf;   // per (node,outputPort,voice) slot
    private readonly float[][] _scratch;  // shared input-summation scratch, indexed by input-port position
    private readonly float[] _zero;
    private readonly FieldRenderContext _ctx = new();
    private readonly FieldVoiceManager _voices;
    private readonly int[] _activeVoices;
    private readonly float[] _voicePeak;
    private readonly float[] _inL, _inR, _scL, _scR, _outL, _outR;

    public AudioFormat Format { get; }
    public int MaxBlock { get; }
    public bool IsInstrument { get; }
    public FieldVoiceManager Voices => _voices;

    internal CompiledGraph(AudioFormat format, int maxBlock, bool isInstrument, FieldVoiceManager voices,
        CompiledNode[] order, float[][] outBuf, int scratchPorts)
    {
        Format = format;
        MaxBlock = maxBlock;
        IsInstrument = isInstrument;
        _voices = voices;
        _order = order;
        _outBuf = outBuf;
        _zero = new float[maxBlock];
        _scratch = new float[Math.Max(1, scratchPorts)][];
        for (var i = 0; i < _scratch.Length; i++) _scratch[i] = new float[maxBlock];
        _activeVoices = new int[voices.MaxVoices];
        _voicePeak = new float[voices.MaxVoices];
        _inL = new float[maxBlock];
        _inR = new float[maxBlock];
        _scL = new float[maxBlock];
        _scR = new float[maxBlock];
        _outL = new float[maxBlock];
        _outR = new float[maxBlock];

        foreach (var cn in order)
            if (cn.Node is IFieldNoteReceiver receiver) _receivers.Add(receiver);
    }

    /// <summary>Allocates a voice for a note and resets every per-voice node's state for that voice.</summary>
    public void NoteOn(int note, float velocity)
    {
        var freq = MusicalMath.NoteToFrequency(note);
        var v = _voices.NoteOn(note, velocity, freq);
        foreach (var cn in _order)
            if (cn.PerVoice) cn.Node.ResetVoice(v);
        foreach (var r in _receivers) r.NoteOn(note, velocity);
    }

    public void NoteOff(int note)
    {
        _voices.NoteOff(note);
        foreach (var r in _receivers) r.NoteOff(note);
    }

    public void AllNotesOff()
    {
        _voices.AllNotesOff();
        foreach (var r in _receivers) r.AllNotesOff();
    }

    public void PitchBend(double semitones)
    {
        _voices.ApplyPitchBend(semitones);
        foreach (var r in _receivers) r.PitchBend(semitones);
    }

    /// <summary>
    /// Renders one interleaved block. For an instrument the result is <b>added</b> into
    /// <paramref name="buffer"/>; for an effect the incoming audio is read, processed, and the result
    /// <b>overwrites</b> the buffer.
    /// </summary>
    public void Process(Span<float> buffer, double bpm, double playheadBeats, bool playing,
        ReadOnlySpan<float> sidechain, int sidechainChannels)
    {
        var channels = Format.Channels < 1 ? 1 : Format.Channels;
        var frames = buffer.Length / channels;
        if (frames <= 0) return;
        if (frames > MaxBlock) frames = MaxBlock;

        // Deinterleave incoming audio (effect mode) and clear the output accumulators.
        if (!IsInstrument) Deinterleave(buffer, channels, frames, _inL, _inR);
        else { Array.Clear(_inL, 0, frames); Array.Clear(_inR, 0, frames); }
        Deinterleave(sidechain, sidechainChannels, frames, _scL, _scR);
        Array.Clear(_outL, 0, frames);
        Array.Clear(_outR, 0, frames);
        Array.Clear(_voicePeak, 0, _voicePeak.Length);

        _ctx.Format = Format;
        _ctx.Frames = frames;
        _ctx.Bpm = bpm;
        _ctx.PlayheadBeats = playheadBeats;
        _ctx.Playing = playing;
        _ctx.Voices = _voices.Voices;
        _ctx.HostInLeft = _inL;
        _ctx.HostInRight = _inR;
        _ctx.SidechainLeft = _scL;
        _ctx.SidechainRight = _scR;
        _ctx.HostOutLeft = _outL;
        _ctx.HostOutRight = _outR;

        var activeCount = 0;
        var vs = _voices.Voices;
        for (var i = 0; i < vs.Length; i++)
            if (vs[i].Active) _activeVoices[activeCount++] = i;

        if (IsInstrument && activeCount == 0) return;

        foreach (var cn in _order)
        {
            if (cn.PerVoice)
            {
                var active = _activeVoices.AsSpan(0, activeCount);
                for (var a = 0; a < activeCount; a++)
                    ProcessNode(cn, active[a], frames, activeCount);
            }
            else
            {
                ProcessNode(cn, 0, frames, activeCount);
            }
        }

        // Interleave the accumulated output into the host buffer.
        for (var f = 0; f < frames; f++)
        {
            var baseIdx = f * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                var s = ch == 0 ? _outL[f] : ch == 1 ? _outR[f] : (_outL[f] + _outR[f]) * 0.5f;
                if (IsInstrument) buffer[baseIdx + ch] += s;
                else buffer[baseIdx + ch] = s;
            }
        }

        if (IsInstrument) _voices.EndBlock(_voicePeak);
    }

    private void ProcessNode(CompiledNode cn, int voice, int frames, int activeCount)
    {
        var node = cn.Node;
        var inputs = node.Inputs;

        for (var ip = 0; ip < inputs.Count; ip++)
        {
            var port = inputs[ip];
            var edges = cn.InEdges[ip];
            if (edges.Length == 0)
            {
                cn.InputBuffers[ip] = _zero;
                if (port.IsModulation) cn.ModByParam[port.ModParamIndex] = null;
                continue;
            }

            var buf = _scratch[ip];
            Array.Clear(buf, 0, frames);
            foreach (var e in edges)
            {
                if (e.SrcPerVoice)
                {
                    if (cn.PerVoice)
                    {
                        Add(buf, _outBuf[e.SrcOutBase + voice], frames);
                    }
                    else
                    {
                        for (var a = 0; a < activeCount; a++)
                        {
                            var av = _activeVoices[a];
                            var src = _outBuf[e.SrcOutBase + av];
                            Add(buf, src, frames);
                            if (e.Audio) TrackPeak(av, src, frames);
                        }
                    }
                }
                else
                {
                    Add(buf, _outBuf[e.SrcOutBase], frames);
                }
            }

            cn.InputBuffers[ip] = buf;
            if (port.IsModulation) cn.ModByParam[port.ModParamIndex] = buf;
        }

        for (var op = 0; op < node.Outputs.Count; op++)
            cn.OutputBuffers[op] = cn.PerVoice ? _outBuf[cn.OutSlotBase[op] + voice] : _outBuf[cn.OutSlotBase[op]];

        _ctx.Voice = voice;
        _ctx.Bind(cn.InputBuffers, cn.OutputBuffers, cn.ModByParam);
        node.ProcessBlock(_ctx);
    }

    private void TrackPeak(int voice, float[] src, int frames)
    {
        var peak = _voicePeak[voice];
        for (var i = 0; i < frames; i++)
        {
            var a = src[i] < 0 ? -src[i] : src[i];
            if (a > peak) peak = a;
        }

        _voicePeak[voice] = peak;
    }

    private static void Add(float[] dest, float[] src, int frames)
    {
        for (var i = 0; i < frames; i++) dest[i] += src[i];
    }

    private static void Deinterleave(ReadOnlySpan<float> buffer, int channels, int frames, float[] left, float[] right)
    {
        if (channels <= 0 || buffer.Length < frames * channels)
        {
            Array.Clear(left, 0, frames);
            Array.Clear(right, 0, frames);
            return;
        }

        for (var f = 0; f < frames; f++)
        {
            var b = f * channels;
            left[f] = buffer[b];
            right[f] = channels > 1 ? buffer[b + 1] : buffer[b];
        }
    }
}
