using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Ongenet.App.Services
{
    /// <summary>
    /// Drives a per-frame UI callback for a control. While in "fast" mode (playback), it uses
    /// <see cref="TopLevel.RequestAnimationFrame"/> — a vsync-aligned, display-rate callback — so the
    /// playhead/meters animate smoothly without the jitter of a <see cref="DispatcherTimer"/>. While
    /// idle (stopped), it falls back to a low-rate timer so the renderer isn't pinned at full refresh
    /// for nothing (RequestAnimationFrame forces a frame each time, so we must not self-reschedule it
    /// when there's nothing moving). Only one source ticks at a time — no double-firing.
    /// </summary>
    public sealed class FrameTicker
    {
        private readonly Control _host;
        private readonly Action _onTick;
        private readonly DispatcherTimer _idleTimer;
        private bool _fast;
        private bool _attached;
        private bool _rafScheduled;

        public FrameTicker(Control host, Action onTick, int idleIntervalMs = 33)
        {
            _host = host;
            _onTick = onTick;
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(idleIntervalMs) };
            _idleTimer.Tick += (_, _) => _onTick();

            _host.AttachedToVisualTree += (_, _) => { _attached = true; Sync(); };
            _host.DetachedFromVisualTree += (_, _) => { _attached = false; Sync(); };
            if (TopLevel.GetTopLevel(_host) is not null) { _attached = true; Sync(); }
        }

        /// <summary>Switch between the vsync render-frame cadence (true) and the idle timer (false).</summary>
        public void SetFast(bool fast)
        {
            if (_fast == fast) return;
            _fast = fast;
            Sync();
        }

        private void Sync()
        {
            if (_attached && _fast)
            {
                _idleTimer.Stop();
                ScheduleRaf();
            }
            else
            {
                if (_attached && !_idleTimer.IsEnabled) _idleTimer.Start();
                else if (!_attached) _idleTimer.Stop();
            }
        }

        private void ScheduleRaf()
        {
            if (_rafScheduled) return;
            var top = TopLevel.GetTopLevel(_host);
            if (top is null) { _idleTimer.Start(); return; } // no top level yet → poll until one exists

            _rafScheduled = true;
            top.RequestAnimationFrame(_ =>
            {
                _rafScheduled = false;
                if (!_attached || !_fast) return;
                _onTick();
                ScheduleRaf(); // self-reschedule: keeps the loop running each display frame while fast
            });
        }
    }
}
