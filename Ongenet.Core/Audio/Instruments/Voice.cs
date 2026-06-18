using System;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// One sounding note within a <see cref="PolyphonicInstrument"/>. Concrete instruments supply
/// a voice type that defines their DSP. Voices are pooled and reused, so they must not
/// allocate while rendering.
/// </summary>
public abstract class Voice
{
    /// <summary>Whether this voice is currently producing sound.</summary>
    public bool IsActive { get; protected set; }

    /// <summary>The MIDI note this voice is playing (valid while active).</summary>
    public int Note { get; protected set; }

    /// <summary>The engine format. Set on <see cref="Start"/>.</summary>
    protected AudioFormat Format { get; private set; }

    /// <summary>Begins playing a note. Overrides should call base first.</summary>
    public virtual void Start(int midiNote, float velocity, AudioFormat format)
    {
        Note = midiNote;
        Format = format;
        IsActive = true;
    }

    /// <summary>Begins the release phase. The voice keeps rendering until its tail finishes.</summary>
    public abstract void Release();

    /// <summary>Adds this voice's output into <paramref name="buffer"/> (interleaved).</summary>
    public abstract void Render(Span<float> buffer);
}
