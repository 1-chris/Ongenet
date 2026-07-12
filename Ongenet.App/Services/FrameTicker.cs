using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Ongenet.App.Services
{
    /// <summary>
    /// Drives a per-frame UI callback for a control. Subscribers on the same <see cref="TopLevel"/>
    /// share one <see cref="DispatcherTimer"/> so transport, timeline, etc. don't each run their own
    /// loop (which beat out of phase and, with a translucent window, make the whole compositor flicker).
    ///
    /// We deliberately do NOT use <see cref="TopLevel.RequestAnimationFrame"/>: on a translucent or
    /// acrylic window it schedules a full top-level present every vsync even when only a meter or
    /// playhead changed, which reads as a subtle brightness pulse across the entire window.
    /// </summary>
    public sealed class FrameTicker
    {
        private readonly Control _host;
        private readonly Action _onTick;
        private readonly int _idleIntervalMs;
        private readonly int _fastIntervalMs;
        private FrameTickerHub? _hub;
        private bool _fast;
        private bool _attached;

        internal bool IsAttached => _attached;
        internal bool WantsFast => _fast;
        internal Control Host => _host;

        public FrameTicker(Control host, Action onTick, int idleIntervalMs = 100, int fastIntervalMs = 33)
        {
            _host = host;
            _onTick = onTick;
            _idleIntervalMs = idleIntervalMs;
            _fastIntervalMs = fastIntervalMs;

            _host.AttachedToVisualTree += (_, _) => Attach();
            _host.DetachedFromVisualTree += (_, _) => Detach();
            if (TopLevel.GetTopLevel(_host) is not null) Attach();
        }

        /// <summary>Higher-rate timer while playback needs smooth overlays (true); slow poll while idle.</summary>
        public void SetFast(bool fast)
        {
            if (_fast == fast) return;
            _fast = fast;
            _hub?.Sync();
        }

        private void Attach()
        {
            _attached = true;
            EnsureHub()?.Register(this);
            _hub?.Sync();
        }

        private void Detach()
        {
            _attached = false;
            _hub?.Unregister(this);
            _hub?.Sync();
        }

        private FrameTickerHub? EnsureHub()
        {
            if (_hub is not null) return _hub;
            var top = TopLevel.GetTopLevel(_host);
            if (top is null) return null;
            _hub = FrameTickerHub.GetOrCreate(top, _idleIntervalMs, _fastIntervalMs);
            return _hub;
        }

        internal void InvokeTick()
        {
            if (!IsEffectivelyVisible(_host)) return;
            _onTick();
        }

        internal static bool IsEffectivelyVisible(Visual visual)
        {
            for (var v = visual; v is not null; v = v.GetVisualParent())
                if (!v.IsVisible) return false;
            return true;
        }
    }

    /// <summary>One timer per top-level window; fans out to every <see cref="FrameTicker"/> on it.</summary>
    internal sealed class FrameTickerHub
    {
        private static readonly Dictionary<TopLevel, FrameTickerHub> Hubs = new();

        private readonly TopLevel _top;
        private readonly int _idleIntervalMs;
        private readonly int _fastIntervalMs;
        private readonly DispatcherTimer _timer;
        private readonly HashSet<FrameTicker> _subscribers = new();

        public static FrameTickerHub GetOrCreate(TopLevel top, int idleIntervalMs, int fastIntervalMs)
        {
            if (!Hubs.TryGetValue(top, out var hub))
            {
                hub = new FrameTickerHub(top, idleIntervalMs, fastIntervalMs);
                Hubs[top] = hub;
                top.Closed += (_, _) => Hubs.Remove(top);
            }

            return hub;
        }

        private FrameTickerHub(TopLevel top, int idleIntervalMs, int fastIntervalMs)
        {
            _top = top;
            _idleIntervalMs = idleIntervalMs;
            _fastIntervalMs = fastIntervalMs;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(idleIntervalMs) };
            _timer.Tick += (_, _) => FireTicks();
        }

        public void Register(FrameTicker subscriber) => _subscribers.Add(subscriber);

        public void Unregister(FrameTicker subscriber) => _subscribers.Remove(subscriber);

        public void Sync()
        {
            if (!HasActiveSubscribers())
            {
                _timer.Stop();
                return;
            }

            var interval = WantsFast() ? _fastIntervalMs : _idleIntervalMs;
            var span = TimeSpan.FromMilliseconds(interval);
            if (_timer.Interval != span)
                _timer.Interval = span;

            if (!_timer.IsEnabled)
                _timer.Start();
        }

        private bool HasActiveSubscribers()
        {
            foreach (var s in _subscribers)
                if (s.IsAttached && FrameTicker.IsEffectivelyVisible(s.Host)) return true;
            return false;
        }

        private bool WantsFast()
        {
            foreach (var s in _subscribers)
                if (s.IsAttached && s.WantsFast && FrameTicker.IsEffectivelyVisible(s.Host)) return true;
            return false;
        }

        private void FireTicks()
        {
            foreach (var s in _subscribers)
            {
                if (s.IsAttached) s.InvokeTick();
            }
        }
    }
}
