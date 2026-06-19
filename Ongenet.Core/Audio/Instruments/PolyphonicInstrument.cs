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
public abstract class PolyphonicInstrument : IInstrument
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

    public void Prepare(AudioFormat format) => _format = format;

    public void NoteOn(int midiNote, float velocity)
    {
        lock (_lock)
        {
            var index = FindFreeVoice();
            if (index < 0) index = FindOldestVoice();
            _voices[index].Start(midiNote, velocity, _format);
            _startOrder[index] = _counter++;
        }
    }

    public void NoteOff(int midiNote)
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

    public void AllNotesOff()
    {
        lock (_lock)
        {
            foreach (var voice in _voices)
            {
                if (voice.IsActive) voice.Release();
            }
        }
    }

    public void Render(Span<float> buffer)
    {
        // Read voices without locking the audio thread against UI note events; each voice
        // checks its own active flag.
        foreach (var voice in _voices)
        {
            if (voice.IsActive) voice.Render(buffer);
        }
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
