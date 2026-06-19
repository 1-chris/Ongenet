using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Ongenet.Core.Audio.Effects;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Reusable base for the filter/EQ analyser graphs: a log-frequency × dB chart that draws a
    /// background, frequency grid with x-axis band labels, and a live audio spectrum overlay fed by
    /// an <see cref="ISpectrumSource"/>. Subclasses draw their own curve/markers in
    /// <see cref="RenderOverlay"/> and use the shared <see cref="FreqToX"/>/<see cref="XToFreq"/> map.
    /// </summary>
    public abstract class SpectrumGraphControl : ThemedControl
    {
        protected const double MinFreq = 20.0;
        protected const double MaxFreq = 20000.0;
        private const double SpecTopDb = 0.0;   // spectrum axis (dBFS)
        private const double SpecBotDb = -84.0;
        private const int FftSize = 1024;

        public static readonly StyledProperty<ISpectrumSource?> SourceProperty =
            AvaloniaProperty.Register<SpectrumGraphControl, ISpectrumSource?>(nameof(Source));

        private IBrush _backBrush = Brushes.Black;
        private IPen _gridPen = new Pen(Brushes.Gray, 1);
        private IPen _gridStrongPen = new Pen(Brushes.Gray, 1);
        private IBrush _spectrumBrush = Brushes.Teal;
        private IBrush _labelBrush = Brushes.Gray;

        protected override void BuildThemeResources()
        {
            base.BuildThemeResources();
            _backBrush = new SolidColorBrush(ThemePalette.Mantle);
            _gridPen = new Pen(new SolidColorBrush(ThemePalette.Surface0), 1);
            _gridStrongPen = new Pen(new SolidColorBrush(ThemePalette.Surface1), 1);
            _spectrumBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Teal, 0x66));
            _labelBrush = new SolidColorBrush(ThemePalette.Overlay1);
        }

        // (frequency, label, strong-line) for the x-axis band indicators.
        private static readonly (double Freq, string Label, bool Strong)[] AxisMarks =
        {
            (50, "50", false), (100, "100", true), (200, "200", false), (500, "500", false),
            (1000, "1k", true), (2000, "2k", false), (5000, "5k", false), (10000, "10k", true), (20000, "20k", false)
        };

        // Spectrum analysis state (UI thread).
        private readonly DispatcherTimer _timer;
        private readonly float[] _samples = new float[FftSize];
        private readonly float[] _window = new float[FftSize];
        private readonly float[] _re = new float[FftSize];
        private readonly float[] _im = new float[FftSize];
        private readonly float[] _mag = new float[FftSize / 2 + 1]; // smoothed dB per bin
        private bool _haveSpectrum;

        protected SpectrumGraphControl()
        {
            for (var i = 0; i < FftSize; i++)
                _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1))); // Hann
            for (var i = 0; i < _mag.Length; i++) _mag[i] = (float)SpecBotDb;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => UpdateSpectrum();
        }

        public ISpectrumSource? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

        /// <summary>The reserved strip at the bottom for x-axis labels.</summary>
        protected double LabelStripHeight => 14.0;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _timer.Stop();
        }

        private void UpdateSpectrum()
        {
            var source = Source;
            if (source is null) { if (_haveSpectrum) { _haveSpectrum = false; InvalidateVisual(); } return; }

            source.CaptureLatest(_samples);
            for (var i = 0; i < FftSize; i++) { _re[i] = _samples[i] * _window[i]; _im[i] = 0; }
            Fft(_re, _im);

            var scale = 4.0 / FftSize; // single-sided amplitude with Hann gain compensation
            for (var k = 0; k < _mag.Length; k++)
            {
                var amp = Math.Sqrt(_re[k] * (double)_re[k] + _im[k] * (double)_im[k]) * scale;
                var db = 20.0 * Math.Log10(amp + 1e-9);
                if (db < SpecBotDb) db = SpecBotDb;
                _mag[k] = db > _mag[k] ? (float)db : _mag[k] + (float)((db - _mag[k]) * 0.25); // fast attack, slow release
            }

            _haveSpectrum = true;
            InvalidateVisual();
        }

        public sealed override void Render(DrawingContext context)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 8 || h < 8) return;

            context.DrawRectangle(_backBrush, null, new Rect(0, 0, w, h));

            var plotH = Math.Max(1, h - LabelStripHeight);

            if (_haveSpectrum) DrawSpectrum(context, w, plotH);
            DrawAxis(context, w, h, plotH);
            RenderOverlay(context, w, plotH);
        }

        /// <summary>Draws the subclass's curve/markers within the plot area (height excludes labels).</summary>
        protected abstract void RenderOverlay(DrawingContext context, double width, double plotHeight);

        private void DrawSpectrum(DrawingContext context, double w, double plotH)
        {
            var sr = Source?.SampleRate > 0 ? Source!.SampleRate : 44100.0;
            var bins = _mag.Length;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, plotH), true);
                for (var px = 0.0; px <= w; px += 1)
                {
                    var bin = (int)Math.Round(XToFreq(px, w) * FftSize / sr);
                    if (bin < 0) bin = 0; else if (bin >= bins) bin = bins - 1;
                    ctx.LineTo(new Point(px, SpecDbToY(_mag[bin], plotH)));
                }

                ctx.LineTo(new Point(w, plotH));
                ctx.EndFigure(true);
            }

            context.DrawGeometry(_spectrumBrush, null, geometry);
        }

        private void DrawAxis(DrawingContext context, double w, double h, double plotH)
        {
            foreach (var (freq, label, strong) in AxisMarks)
            {
                var x = FreqToX(freq, w);
                context.DrawLine(strong ? _gridStrongPen : _gridPen, new Point(x, 0), new Point(x, plotH));
                var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 9, _labelBrush);
                var tx = Math.Clamp(x - ft.Width / 2, 1, w - ft.Width - 1);
                context.DrawText(ft, new Point(tx, h - LabelStripHeight + 1));
            }
        }

        private static void Fft(float[] re, float[] im)
        {
            var n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                var bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }

            for (var len = 2; len <= n; len <<= 1)
            {
                var ang = -2.0 * Math.PI / len;
                float wRe = (float)Math.Cos(ang), wIm = (float)Math.Sin(ang);
                for (var i = 0; i < n; i += len)
                {
                    float curRe = 1, curIm = 0;
                    for (var k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        var tre = re[b] * curRe - im[b] * curIm;
                        var tim = re[b] * curIm + im[b] * curRe;
                        re[b] = re[a] - tre; im[b] = im[a] - tim;
                        re[a] += tre; im[a] += tim;
                        var ncur = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = ncur;
                    }
                }
            }
        }

        protected static double FreqToX(double freq, double width)
        {
            freq = Math.Clamp(freq, MinFreq, MaxFreq);
            return Math.Log(freq / MinFreq) / Math.Log(MaxFreq / MinFreq) * width;
        }

        protected static double XToFreq(double x, double width)
        {
            var t = width > 0 ? x / width : 0;
            return MinFreq * Math.Pow(MaxFreq / MinFreq, t);
        }

        private static double SpecDbToY(double db, double plotH)
            => Math.Clamp((SpecTopDb - db) / (SpecTopDb - SpecBotDb), 0, 1) * plotH;
    }
}
