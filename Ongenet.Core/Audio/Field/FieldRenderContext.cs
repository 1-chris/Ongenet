using System;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// The per-block, per-node processing context handed to <see cref="FieldNode.ProcessBlock"/>. The
/// runtime rebinds the port buffers (via <see cref="Bind"/>) immediately before invoking each node, so
/// a node reads its inlets through <see cref="Input"/> / <see cref="ModInput"/> and writes its outlets
/// through <see cref="Output"/>. Everything here is preallocated — no audio-thread allocation.
/// </summary>
public sealed class FieldRenderContext
{
    public AudioFormat Format { get; internal set; } = AudioFormat.Default;

    /// <summary>Number of sample frames in this block (mono buffer length).</summary>
    public int Frames { get; internal set; }

    /// <summary>The voice slot currently being processed (0 for global nodes and effects).</summary>
    public int Voice { get; internal set; }

    public double Bpm { get; internal set; } = 120.0;
    public double PlayheadBeats { get; internal set; }
    public bool Playing { get; internal set; }

    /// <summary>Per-voice note state. Indexed by <see cref="Voice"/>.</summary>
    public FieldVoiceState[] Voices { get; internal set; } = Array.Empty<FieldVoiceState>();

    /// <summary>Global pitch-bend in semitones (already folded into <see cref="FieldVoiceState.Frequency"/>).</summary>
    public double PitchBendSemitones { get; internal set; }

    /// <summary>Deinterleaved incoming audio (effect mode / sidechain), left channel. Length <see cref="Frames"/>.</summary>
    public float[] HostInLeft { get; internal set; } = Array.Empty<float>();

    /// <summary>Deinterleaved incoming audio, right channel (equals left for mono input).</summary>
    public float[] HostInRight { get; internal set; } = Array.Empty<float>();

    /// <summary>Deinterleaved sidechain audio, left channel (zero-filled when no source is connected).</summary>
    public float[] SidechainLeft { get; internal set; } = Array.Empty<float>();

    /// <summary>Deinterleaved sidechain audio, right channel.</summary>
    public float[] SidechainRight { get; internal set; } = Array.Empty<float>();

    /// <summary>Output accumulator written by the Audio Out node, left channel; interleaved to the host after the block.</summary>
    public float[] HostOutLeft { get; internal set; } = Array.Empty<float>();

    /// <summary>Output accumulator written by the Audio Out node, right channel.</summary>
    public float[] HostOutRight { get; internal set; } = Array.Empty<float>();

    // Bound per node execution by the runtime.
    private float[][] _inputs = Array.Empty<float[]>();
    private float[][] _outputs = Array.Empty<float[]>();
    private float[]?[] _modByParam = Array.Empty<float[]?>();

    internal void Bind(float[][] inputs, float[][] outputs, float[]?[] modByParam)
    {
        _inputs = inputs;
        _outputs = outputs;
        _modByParam = modByParam;
    }

    /// <summary>The summed signal on input port <paramref name="port"/> (length <see cref="Frames"/>). Never null.</summary>
    public float[] Input(int port) => _inputs[port];

    /// <summary>The write buffer for output port <paramref name="port"/> (length <see cref="Frames"/>).</summary>
    public float[] Output(int port) => _outputs[port];

    /// <summary>
    /// The modulation signal connected to parameter <paramref name="paramIndex"/>, or null when nothing
    /// is patched into that parameter's modulation inlet.
    /// </summary>
    public float[]? ModInput(int paramIndex) => _modByParam[paramIndex];
}
