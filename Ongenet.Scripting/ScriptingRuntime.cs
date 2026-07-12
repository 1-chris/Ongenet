using System;
using System.Collections.Generic;
using System.Threading;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Scripting;

/// <summary>Manages live script subscriptions (transport, beat grid, clip changes).</summary>
public sealed class ScriptingRuntime : IDisposable
{
    private readonly ITransportService _transport;
    private readonly List<(Action<ScriptTransportState> Handler, Action<ScriptTransportState> Wrapper)> _transportHandlers = new();
    private readonly List<(Action<ScriptClipInfo> Handler, Action<ScriptClipInfo> Wrapper)> _clipHandlers = new();
    private readonly List<BeatSubscription> _beatHandlers = new();
    private readonly object _gate = new();

    private Timer? _beatTimer;
    private SynchronizationContext? _uiContext;
    private Action<string>? _log;
    private int _handlerFailures;

    public ScriptingRuntime(ITransportService transport) => _transport = transport;

    public bool IsActive { get; private set; }

    public void Configure(Action<string>? log, SynchronizationContext? uiContext)
    {
        _log = log;
        _uiContext = uiContext;
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        _handlerFailures = 0;
        _transport.StateChanged += OnTransportStateChanged;
        OnTransportStateChanged(_transport.State);
        StartBeatTimerIfNeeded();
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        StopBeatTimer();
        _transport.StateChanged -= OnTransportStateChanged;
        _transportHandlers.Clear();
        lock (_gate)
            _beatHandlers.Clear();
        _clipHandlers.Clear();
    }

    public void Dispose() => Deactivate();

    public IDisposable OnTransportStateChanged(Action<ScriptTransportState> handler)
    {
        void Wrapper(ScriptTransportState state) => InvokeHandler(() => handler(state));
        _transportHandlers.Add((handler, Wrapper));
        Wrapper(_transport.State == TransportState.Playing
            ? ScriptTransportState.Playing
            : ScriptTransportState.Stopped);
        return new Subscription(() => RemoveTransportHandler(handler));
    }

    public IDisposable OnBeat(Action<double> handler, double gridBeats = 1.0)
    {
        if (gridBeats <= 0) gridBeats = 1.0;
        var sub = new BeatSubscription(handler, gridBeats);
        lock (_gate)
            _beatHandlers.Add(sub);
        return new Subscription(() =>
        {
            lock (_gate)
                _beatHandlers.Remove(sub);
        });
    }

    public IDisposable OnClipChanged(Action<ScriptClipInfo> handler)
    {
        void Wrapper(ScriptClipInfo info) => InvokeHandler(() => handler(info));
        _clipHandlers.Add((handler, Wrapper));
        return new Subscription(() => RemoveClipHandler(handler));
    }

    public void NotifyClipChanged(ScriptClipInfo info)
    {
        foreach (var (_, wrapper) in _clipHandlers.ToArray())
            InvokeHandler(() => wrapper(info));
    }

    private void RemoveTransportHandler(Action<ScriptTransportState> handler)
    {
        var idx = _transportHandlers.FindIndex(h => h.Handler == handler);
        if (idx >= 0) _transportHandlers.RemoveAt(idx);
    }

    private void RemoveClipHandler(Action<ScriptClipInfo> handler)
    {
        var idx = _clipHandlers.FindIndex(h => h.Handler == handler);
        if (idx >= 0) _clipHandlers.RemoveAt(idx);
    }

    private void OnTransportStateChanged(TransportState state)
    {
        var scriptState = state == TransportState.Playing
            ? ScriptTransportState.Playing
            : ScriptTransportState.Stopped;

        foreach (var (_, wrapper) in _transportHandlers.ToArray())
            InvokeHandler(() => wrapper(scriptState));

        if (state == TransportState.Playing)
            StartBeatTimerIfNeeded();
        else
            StopBeatTimer();
    }

    private void StartBeatTimerIfNeeded()
    {
        if (_transport.State != TransportState.Playing) return;
        _beatTimer ??= new Timer(_ => OnBeatTimerTick(), null, 0, 100);
    }

    private void StopBeatTimer()
    {
        _beatTimer?.Dispose();
        _beatTimer = null;
        lock (_gate)
        {
            foreach (var sub in _beatHandlers)
                sub.LastCell = double.NaN;
        }
    }

    private void OnBeatTimerTick()
    {
        if (_transport.State != TransportState.Playing)
        {
            StopBeatTimer();
            return;
        }

        var beat = _transport.PlayheadBeats;
        BeatSubscription[] handlers;
        lock (_gate)
            handlers = _beatHandlers.ToArray();

        foreach (var sub in handlers)
        {
            var grid = sub.GridBeats;
            var currentCell = Math.Floor(beat / grid);
            if (double.IsNaN(sub.LastCell) || currentCell > sub.LastCell)
            {
                sub.LastCell = currentCell;
                var snapped = currentCell * grid;
                InvokeHandler(() => sub.Handler(snapped));
            }
        }
    }

    private void InvokeHandler(Action action)
    {
        if (_uiContext is not null)
        {
            _uiContext.Post(_ => SafeInvoke(action), null);
            return;
        }

        SafeInvoke(action);
    }

    private void SafeInvoke(Action action)
    {
        try
        {
            action();
            _handlerFailures = 0;
        }
        catch (Exception ex)
        {
            _handlerFailures++;
            _log?.Invoke($"Script handler error: {ex.Message}");
            if (_handlerFailures >= 3)
            {
                _log?.Invoke("Live script stopped after repeated handler errors.");
                Deactivate();
            }
        }
    }

    private sealed class BeatSubscription
    {
        public BeatSubscription(Action<double> handler, double gridBeats)
        {
            Handler = handler;
            GridBeats = gridBeats;
        }

        public Action<double> Handler { get; }
        public double GridBeats { get; }
        public double LastCell { get; set; } = double.NaN;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;
        private int _disposed;

        public Subscription(Action dispose) => _dispose = dispose;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _dispose();
        }
    }
}
