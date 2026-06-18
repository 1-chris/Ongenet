using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// A little monitor that visualises the granular synth's grain cloud: each spawned grain appears as
    /// a dot (x = position in the source sample, y = stereo pan) that fades in and out over the grain's
    /// lifetime. Driven by its own ~60fps timer, draining new spawns from the instrument's
    /// <see cref="GrainMonitor"/> — animation runs on the UI clock, independent of the audio thread.
    /// </summary>
    public sealed class GrainMonitorControl : Control
    {
        public static readonly StyledProperty<GrainMonitor?> MonitorProperty =
            AvaloniaProperty.Register<GrainMonitorControl, GrainMonitor?>(nameof(Monitor));

        private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#181825")); // mantle/crust
        private static readonly IPen Axis = new Pen(new SolidColorBrush(Color.Parse("#313244")), 1);
        private static readonly Color Forward = Color.Parse("#cba6f7"); // mauve
        private static readonly Color Reverse = Color.Parse("#94e2d5"); // teal

        private readonly List<VisualGrain> _grains = new();
        private DispatcherTimer? _timer;
        private long _cursor;
        private long _lastDrainMs = -1;

        static GrainMonitorControl()
        {
            MonitorProperty.Changed.AddClassHandler<GrainMonitorControl>((c, _) => c.OnMonitorChanged());
        }

        public GrainMonitor? Monitor
        {
            get => GetValue(MonitorProperty);
            set => SetValue(MonitorProperty, value);
        }

        private void OnMonitorChanged()
        {
            _grains.Clear();
            _cursor = Monitor?.Cursor ?? 0; // start fresh; don't replay history
            _lastDrainMs = -1;
            InvalidateVisual();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _timer?.Stop();
            _timer = null;
            _grains.Clear();
        }

        private void Tick()
        {
            var monitor = Monitor;
            var now = Environment.TickCount64;
            if (_lastDrainMs < 0) _lastDrainMs = now;

            if (monitor is not null)
            {
                // Drain newly-spawned grains (bounded so a long stall can't loop forever).
                var target = monitor.Cursor;
                if (target - _cursor > 512) _cursor = target - 512;
                var newCount = target - _cursor;
                if (newCount > 0)
                {
                    // The audio thread reports a whole block's grains at once and the timer can fire
                    // irregularly, so a naive drain births a clump that fades in lockstep ("bursts").
                    // Spread the batch's birth times back across the real elapsed interval so the cloud
                    // reads as a steady supply.
                    var spanStart = _lastDrainMs;
                    var span = Math.Max(1, now - spanStart);
                    long drained = 0;
                    while (_cursor < target && monitor.TryGet(_cursor, out var blip))
                    {
                        _cursor++;
                        drained++;
                        if (blip.DurationSeconds <= 0) continue;
                        var born = spanStart + span * drained / newCount;
                        _grains.Add(new VisualGrain(blip.Position, blip.Pan, blip.DurationSeconds, blip.Reverse, born));
                        if (_grains.Count > 800) _grains.RemoveAt(0);
                    }
                }
            }

            _lastDrainMs = now;

            // Cull finished grains.
            for (var i = _grains.Count - 1; i >= 0; i--)
            {
                if ((now - _grains[i].BornMs) / 1000.0 >= _grains[i].Duration) _grains.RemoveAt(i);
            }

            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 2 || h < 2) return;

            context.FillRectangle(Background, new Rect(0, 0, w, h));
            // Centre line (pan = 0).
            context.DrawLine(Axis, new Point(0, h / 2), new Point(w, h / 2));

            var now = Environment.TickCount64;
            var midY = h / 2;
            var spread = h * 0.42;

            foreach (var g in _grains)
            {
                var progress = (now - g.BornMs) / 1000.0 / g.Duration;
                if (progress < 0 || progress >= 1) continue;
                var alpha = GrainWindow.Value(GrainWindowShape.Hann, progress); // fade in/out
                if (alpha <= 0.001) continue;

                var x = g.X * w;
                var y = midY + g.Pan * spread;
                var radius = 2.0 + 4.0 * alpha;
                var baseColor = g.Reverse ? Reverse : Forward;
                var brush = new SolidColorBrush(baseColor, alpha);
                context.DrawEllipse(brush, null, new Point(x, y), radius, radius);
            }
        }

        private readonly struct VisualGrain
        {
            public VisualGrain(float x, float pan, float duration, bool reverse, long bornMs)
            {
                X = x;
                Pan = pan;
                Duration = duration;
                Reverse = reverse;
                BornMs = bornMs;
            }

            public float X { get; }
            public float Pan { get; }
            public float Duration { get; }
            public bool Reverse { get; }
            public long BornMs { get; }
        }
    }
}
