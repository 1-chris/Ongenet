using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Ongenet.App.Services;
using Ongenet.App.Theming;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.Controls;

/// <summary>
/// Combined spectrum, waveform, and peak meter panel (Wave Candy-style analyser).
/// </summary>
public sealed class WaveCandyPanelControl : ThemedControl
{
    public static readonly StyledProperty<ISpectrumSource?> SpectrumSourceProperty =
        AvaloniaProperty.Register<WaveCandyPanelControl, ISpectrumSource?>(nameof(SpectrumSource));

    public static readonly StyledProperty<IWaveformSource?> WaveformSourceProperty =
        AvaloniaProperty.Register<WaveCandyPanelControl, IWaveformSource?>(nameof(WaveformSource));

    public static readonly StyledProperty<IAudioAnalyzerSource?> AnalyzerSourceProperty =
        AvaloniaProperty.Register<WaveCandyPanelControl, IAudioAnalyzerSource?>(nameof(AnalyzerSource));

    private const int FftSize = 1024;
    private const int WaveSize = 512;

    private readonly DispatcherTimer _timer;
    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _wave = new float[WaveSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _mag = new float[FftSize / 2 + 1];

    private IBrush _backBrush = Brushes.Black;
    private IBrush _spectrumBrush = Brushes.Teal;
    private IPen _wavePen = new Pen(Brushes.Lime, 1);
    private IBrush _meterBrush = Brushes.SkyBlue;
    private IBrush _labelBrush = Brushes.Gray;

    private float _peakL, _peakR, _rms;
    private bool _haveSpectrum, _haveWave;

    public WaveCandyPanelControl()
    {
        for (var i = 0; i < FftSize; i++)
            _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        for (var i = 0; i < _mag.Length; i++) _mag[i] = -84f;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(UiPerfProfile.AnalyserIntervalMs) };
        _timer.Tick += (_, _) => { UpdateAnalysis(); InvalidateVisual(); };
    }

    public ISpectrumSource? SpectrumSource
    {
        get => GetValue(SpectrumSourceProperty);
        set => SetValue(SpectrumSourceProperty, value);
    }

    public IWaveformSource? WaveformSource
    {
        get => GetValue(WaveformSourceProperty);
        set => SetValue(WaveformSourceProperty, value);
    }

    public IAudioAnalyzerSource? AnalyzerSource
    {
        get => GetValue(AnalyzerSourceProperty);
        set => SetValue(AnalyzerSourceProperty, value);
    }

    protected override void BuildThemeResources()
    {
        base.BuildThemeResources();
        _backBrush = new SolidColorBrush(ThemePalette.Mantle);
        _spectrumBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Teal, 0x88));
        _wavePen = new Pen(new SolidColorBrush(ThemePalette.Green), 1.2);
        _meterBrush = new SolidColorBrush(ThemePalette.Sapphire);
        _labelBrush = new SolidColorBrush(ThemePalette.Overlay1);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1) return;

        var meterH = 28.0;
        var waveH = Math.Max(40, h * 0.22);
        var specH = h - meterH - waveH - 8;

        context.DrawRectangle(_backBrush, null, new Rect(0, 0, w, h));

        DrawMeters(context, w, meterH);
        DrawWaveform(context, w, waveH, meterH + 4);
        DrawSpectrum(context, w, specH, meterH + waveH + 8);
    }

    private void DrawMeters(DrawingContext ctx, double w, double h)
    {
        var barW = (w - 24) / 2;
        DrawBar(ctx, 8, 4, barW, h - 8, _peakL, "L");
        DrawBar(ctx, 16 + barW, 4, barW, h - 8, _peakR, "R");
        var rmsText = $"RMS {_rms:0.00}";
        ctx.DrawText(new FormattedText(rmsText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 10, _labelBrush), new Point(w - 72, 6));
    }

    private void DrawBar(DrawingContext ctx, double x, double y, double bw, double bh, float level, string label)
    {
        ctx.DrawRectangle(null, new Pen(_labelBrush, 1), new Rect(x, y, bw, bh));
        var fillH = bh * Math.Clamp(level, 0f, 1f);
        ctx.DrawRectangle(_meterBrush, null, new Rect(x + 1, y + bh - fillH, bw - 2, fillH));
        ctx.DrawText(new FormattedText(label, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 9, _labelBrush), new Point(x + 2, y + 1));
    }

    private void DrawWaveform(DrawingContext ctx, double w, double h, double top)
    {
        ctx.DrawRectangle(null, new Pen(_labelBrush, 0.5), new Rect(0, top, w, h));
        if (!_haveWave) return;
        var mid = top + h * 0.5;
        var step = w / (WaveSize - 1);
        for (var i = 1; i < WaveSize; i++)
        {
            var y0 = mid - _wave[i - 1] * h * 0.45;
            var y1 = mid - _wave[i] * h * 0.45;
            ctx.DrawLine(_wavePen, new Point((i - 1) * step, y0), new Point(i * step, y1));
        }
    }

    private void DrawSpectrum(DrawingContext ctx, double w, double h, double top)
    {
        if (!_haveSpectrum) return;
        var bins = _mag.Length;
        var barW = w / bins;
        for (var i = 0; i < bins; i++)
        {
            var db = _mag[i];
            var norm = (db + 84f) / 84f;
            if (norm <= 0) continue;
            var bh = norm * h;
            ctx.DrawRectangle(_spectrumBrush, null, new Rect(i * barW, top + h - bh, Math.Max(1, barW), bh));
        }
    }

    private void UpdateAnalysis()
    {
        var analyzer = AnalyzerSource;
        if (analyzer is not null)
        {
            _peakL = analyzer.PeakLeft;
            _peakR = analyzer.PeakRight;
            _rms = analyzer.Rms;
        }

        var wave = WaveformSource ?? SpectrumSource as IWaveformSource;
        if (wave is not null)
        {
            var n = wave.CaptureLatest(_wave);
            _haveWave = n > 0;
        }

        var spec = SpectrumSource;
        if (spec is null) return;
        var got = spec.CaptureLatest(_samples);
        if (got <= 0) return;
        for (var i = 0; i < FftSize; i++)
        {
            _re[i] = i < got ? _samples[i] * _window[i] : 0f;
            _im[i] = 0f;
        }
        FftInPlace(_re, _im);
        var scale = 4.0f / FftSize;
        for (var i = 0; i < _mag.Length; i++)
        {
            var m = (float)Math.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) * scale;
            var db = 20f * (float)Math.Log10(Math.Max(m, 1e-9f));
            _mag[i] = _mag[i] * 0.65f + db * 0.35f;
        }
        _haveSpectrum = true;
    }

    private static void FftInPlace(float[] re, float[] im)
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
}
