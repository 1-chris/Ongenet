using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Reusable base for polyphonic instruments. Manages a fixed pool of <see cref="Voice"/>
/// objects (allocated once), routes note events to them with simple oldest-voice stealing,
/// and sums the active voices in <see cref="Render"/>. Concrete instruments only implement
/// <see cref="CreateVoice"/> and their parameters.
/// </summary>
public abstract class PolyphonicInstrument : IInstrument, IInstrumentVoiceState
{
    private readonly Voice[] _voices;
    private readonly object _lock = new();
    private AudioFormat _format = AudioFormat.Default;
    private uint _counter; // monotonically increasing, for voice-stealing order
    private readonly uint[] _startOrder;

    protected PolyphonicInstrument(int polyphony = 16)
    {
        _voices = new Voice[polyphony];
        _startOrder = new uint[polyphony];
        for (var i = 0; i < polyphony; i++)
        {
            _voices[i] = CreateVoice();
        }
    }

    /// <summary>Creates one voice for the pool. Called once per voice at construction.</summary>
    protected abstract Voice CreateVoice();

    public abstract string Name { get; }

    // Routed through a protected hook so concrete instruments can keep their `const string TypeId`
    // (a const and a same-named override can't coexist, so the interface member is explicit here).
    string IInstrument.TypeId => GetTypeId();
    protected abstract string GetTypeId();

    /// <summary>Editable parameters. Concrete instruments override; default is none.</summary>
    public virtual IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();

    public abstract IInstrument Clone();

    /// <summary>The engine format, available to subclasses that own extra DSP (e.g. a post-mix effect chain).</summary>
    protected AudioFormat Format => _format;

    public virtual void Prepare(AudioFormat format) => _format = format;

    bool IInstrumentVoiceState.HasActiveVoices => AnyVoiceActive;

    public virtual void NoteOn(int midiNote, float velocity)
    {
        lock (_lock)
        {
            var index = FindFreeVoice();
            if (index < 0) index = FindOldestVoice();
            _voices[index].Start(midiNote, velocity, _format);
            _startOrder[index] = _counter++;
        }
    }

    public virtual void NoteOff(int midiNote)
    {
        lock (_lock)
        {
            foreach (var voice in _voices)
            {
                if (voice.IsActive && voice.Note == midiNote)
                {
                    voice.Release();
                }
            }
        }
    }

    public virtual void AllNotesOff()
    {
        lock (_lock)
        {
            foreach (var voice in _voices)
            {
                if (voice.IsActive) voice.Release();
            }
        }
    }

    /// <summary>
    /// Renders the instrument into <paramref name="buffer"/> (additive). The default just sums the
    /// active voices; subclasses can override to render the voices into a scratch buffer via
    /// <see cref="RenderVoices"/>, run their own post-mix DSP, then add the result.
    /// </summary>
    public virtual void Render(Span<float> buffer) => RenderVoices(buffer);

    /// <summary>Sums every active voice's output into <paramref name="buffer"/> (additive).</summary>
    protected void RenderVoices(Span<float> buffer)
    {
        if (!AnyVoiceActive) return;
        // Read voices without locking the audio thread against UI note events; each voice
        // checks its own active flag.
        foreach (var voice in _voices)
        {
            if (voice.IsActive) voice.Render(buffer);
        }
    }

    /// <summary>True when any voice in the pool is currently sounding.</summary>
    protected bool AnyVoiceActive
    {
        get
        {
            foreach (var voice in _voices)
                if (voice.IsActive) return true;
            return false;
        }
    }

    /// <summary>Scans an interleaved buffer for any sample above <paramref name="threshold"/>.</summary>
    protected static bool HasSignal(ReadOnlySpan<float> buffer, float threshold = 1e-6f)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var a = buffer[i];
            if (a < 0) a = -a;
            if (a > threshold) return true;
        }

        return false;
    }

    /// <summary>Picks a free voice index, or the oldest active voice if the pool is full.</summary>
    protected int PickVoiceIndex()
    {
        var index = FindFreeVoice();
        return index >= 0 ? index : FindOldestVoice();
    }

    /// <summary>Starts a voice at a specific pool index (for legato/sequencer paths).</summary>
    protected void StartVoiceAt(int index, int midiNote, float velocity)
    {
        lock (_lock)
        {
            index = Math.Clamp(index, 0, _voices.Length - 1);
            _voices[index].Start(midiNote, velocity, _format);
            _startOrder[index] = _counter++;
        }
    }

    /// <summary>Returns the voice at <paramref name="index"/>.</summary>
    protected Voice VoiceAt(int index) => _voices[Math.Clamp(index, 0, _voices.Length - 1)];

    /// <summary>Returns the first active voice, or null.</summary>
    protected Voice? FirstActiveVoice()
    {
        foreach (var voice in _voices)
            if (voice.IsActive) return voice;
        return null;
    }

    private int FindFreeVoice()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            if (!_voices[i].IsActive) return i;
        }

        return -1;
    }

    private int FindOldestVoice()
    {
        var oldest = 0;
        for (var i = 1; i < _voices.Length; i++)
        {
            if (_startOrder[i] < _startOrder[oldest]) oldest = i;
        }

        return oldest;
    }
}
