using System;

namespace Ongenet.Link;

/// <summary>Ableton Link transport sync (GPL — isolated in Ongenet.Link assembly).</summary>
public interface ILinkSession : IDisposable
{
    /// <summary>True when the native libabl-link library was loaded successfully.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether this instance participates in a Link session on the network.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Session tempo in BPM (two-way synced with the host transport when enabled).</summary>
    double Tempo { get; set; }

    /// <summary>Number of other Link peers currently connected.</summary>
    int PeerCount { get; }

    /// <summary>Shared session phase within the current quantum (0..quantum).</summary>
    double Phase { get; }

    /// <summary>Shared session beat timeline at the last refresh (maps to host playhead when synced).</summary>
    double SessionBeat { get; }

    /// <summary>Beat quantum used for phase calculations (typically beats per bar).</summary>
    double Quantum { get; set; }

    /// <summary>Refreshes phase and session beat from the Link clock (call from UI poll).</summary>
    void RefreshSessionState();

    /// <summary>Starts Link playback, mapping host beat <paramref name="beatAtStart"/> to the session timeline.</summary>
    void StartAtBeat(double beatAtStart);

    void Start();
    void Stop();

    /// <summary>Raised when tempo, phase, peer count, or enabled state changes.</summary>
    event Action? SyncChanged;
}

/// <summary>No-op stub when Link native library is unavailable.</summary>
public sealed class NullLinkSession : ILinkSession
{
    public bool IsAvailable => false;
    public bool IsEnabled { get; set; }
    public double Tempo { get; set; } = 120;
    public int PeerCount => 0;
    public double Phase => 0;
    public double SessionBeat => 0;
    public double Quantum { get; set; } = 4;
    public void RefreshSessionState() { }
    public void StartAtBeat(double beatAtStart) { }
    public void Start() { }
    public void Stop() { }
    public event Action? SyncChanged;
    public void Dispose() { }
}

/// <summary>Creates the best available <see cref="ILinkSession"/> for this build/runtime.</summary>
public static class LinkSessionFactory
{
    public static ILinkSession Create(double initialTempo = 120)
    {
#if ONGENET_LINK_NATIVE
        if (LinkNative.TryLoad())
            return new NativeLinkSession(initialTempo);
#endif
        return new NullLinkSession { Tempo = initialTempo };
    }
}
