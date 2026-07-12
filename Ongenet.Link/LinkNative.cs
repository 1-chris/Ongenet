#if ONGENET_LINK_NATIVE
using System;
using System.Runtime.InteropServices;

namespace Ongenet.Link;

/// <summary>P/Invoke surface over libabl-link (Ableton Link C wrapper).</summary>
internal static class LinkNative
{
    private const string Lib = "abl-link";

    [StructLayout(LayoutKind.Sequential)]
    internal struct AblLink
    {
        public IntPtr Impl;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AblLinkSessionState
    {
        public IntPtr Impl;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void TempoCallback(double tempo, IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PeersCallback(ulong numPeers, IntPtr context);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern AblLink abl_link_create(double bpm);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_destroy(AblLink link);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool abl_link_is_enabled(AblLink link);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_enable(AblLink link, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong abl_link_num_peers(AblLink link);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_set_num_peers_callback(AblLink link, PeersCallback callback, IntPtr context);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_set_tempo_callback(AblLink link, TempoCallback callback, IntPtr context);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long abl_link_clock_micros(AblLink link);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern AblLinkSessionState abl_link_create_session_state();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_destroy_session_state(AblLinkSessionState sessionState);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_capture_app_session_state(AblLink link, AblLinkSessionState sessionState);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_commit_app_session_state(AblLink link, AblLinkSessionState sessionState);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double abl_link_tempo(AblLinkSessionState sessionState);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_set_tempo(AblLinkSessionState sessionState, double bpm, long atTime);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double abl_link_phase_at_time(AblLinkSessionState sessionState, long time, double quantum);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double abl_link_beat_at_time(AblLinkSessionState sessionState, long time);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_set_beat_at_time(AblLinkSessionState sessionState, double beat, long time);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_request_beat_at_start_playing_time(AblLinkSessionState sessionState, double beat);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void abl_link_set_is_playing(AblLinkSessionState sessionState, [MarshalAs(UnmanagedType.I1)] bool isPlaying, long atTime);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool abl_link_is_playing(AblLinkSessionState sessionState);

    internal static bool TryLoad()
    {
        try { return NativeLibrary.TryLoad(Lib, out _); }
        catch { return false; }
    }
}

/// <summary>Live Ableton Link session backed by libabl-link.</summary>
public sealed class NativeLinkSession : ILinkSession
{
    private readonly LinkNative.AblLink _link;
    private readonly LinkNative.TempoCallback _tempoCallback;
    private readonly LinkNative.PeersCallback _peersCallback;
    private readonly GCHandle _selfHandle;
    private double _tempo;
    private int _peerCount;
    private double _phase;
    private double _sessionBeat;
    private double _quantum = 4;
    private bool _enabled;
    private bool _disposed;

    public NativeLinkSession(double initialTempo)
    {
        _tempo = initialTempo;
        _selfHandle = GCHandle.Alloc(this);
        var ctx = GCHandle.ToIntPtr(_selfHandle);

        _link = LinkNative.abl_link_create(initialTempo);

        _tempoCallback = OnTempoChanged;
        _peersCallback = OnPeersChanged;
        LinkNative.abl_link_set_tempo_callback(_link, _tempoCallback, ctx);
        LinkNative.abl_link_set_num_peers_callback(_link, _peersCallback, ctx);
    }

    public bool IsAvailable => true;

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            LinkNative.abl_link_enable(_link, value);
            _enabled = value;
            RefreshPhase();
            SyncChanged?.Invoke();
        }
    }

    public double Tempo
    {
        get => _tempo;
        set
        {
            if (value <= 0 || Math.Abs(_tempo - value) < 1e-9) return;
            var state = LinkNative.abl_link_create_session_state();
            try
            {
                LinkNative.abl_link_capture_app_session_state(_link, state);
                LinkNative.abl_link_set_tempo(state, value, LinkNative.abl_link_clock_micros(_link));
                LinkNative.abl_link_commit_app_session_state(_link, state);
                _tempo = value;
                SyncChanged?.Invoke();
            }
            finally
            {
                LinkNative.abl_link_destroy_session_state(state);
            }
        }
    }

    public int PeerCount => _peerCount;

    public double Phase => _phase;

    public double SessionBeat => _sessionBeat;

    public double Quantum
    {
        get => _quantum;
        set
        {
            if (value <= 0) return;
            _quantum = value;
            RefreshPhase();
        }
    }

    public event Action? SyncChanged;

    public void RefreshSessionState() => RefreshPhase();

    public void StartAtBeat(double beatAtStart)
    {
        if (!IsEnabled) return;
        var state = LinkNative.abl_link_create_session_state();
        try
        {
            LinkNative.abl_link_capture_app_session_state(_link, state);
            var at = LinkNative.abl_link_clock_micros(_link);
            LinkNative.abl_link_request_beat_at_start_playing_time(state, beatAtStart);
            LinkNative.abl_link_set_is_playing(state, true, at);
            LinkNative.abl_link_commit_app_session_state(_link, state);
            RefreshPhase();
        }
        finally
        {
            LinkNative.abl_link_destroy_session_state(state);
        }
    }

    public void Start() => StartAtBeat(0);

    public void Stop()
    {
        if (!IsEnabled) return;
        var state = LinkNative.abl_link_create_session_state();
        try
        {
            LinkNative.abl_link_capture_app_session_state(_link, state);
            LinkNative.abl_link_set_is_playing(state, false, LinkNative.abl_link_clock_micros(_link));
            LinkNative.abl_link_commit_app_session_state(_link, state);
        }
        finally
        {
            LinkNative.abl_link_destroy_session_state(state);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LinkNative.abl_link_destroy(_link);
        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    private void RefreshPhase()
    {
        var state = LinkNative.abl_link_create_session_state();
        try
        {
            LinkNative.abl_link_capture_app_session_state(_link, state);
            var at = LinkNative.abl_link_clock_micros(_link);
            _phase = LinkNative.abl_link_phase_at_time(state, at, _quantum);
            _sessionBeat = LinkNative.abl_link_beat_at_time(state, at);
            _tempo = LinkNative.abl_link_tempo(state);
            _peerCount = (int)LinkNative.abl_link_num_peers(_link);
        }
        finally
        {
            LinkNative.abl_link_destroy_session_state(state);
        }
    }

    private void OnTempoChanged(double tempo, IntPtr context)
    {
        if (GCHandle.FromIntPtr(context).Target is not NativeLinkSession self) return;
        self._tempo = tempo;
        self.RefreshPhase();
        self.SyncChanged?.Invoke();
    }

    private void OnPeersChanged(ulong numPeers, IntPtr context)
    {
        if (GCHandle.FromIntPtr(context).Target is not NativeLinkSession self) return;
        self._peerCount = (int)numPeers;
        self.SyncChanged?.Invoke();
    }
}
#endif
