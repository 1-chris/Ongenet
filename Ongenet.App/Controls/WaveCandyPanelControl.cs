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

    public static readonly StyledProperty<float> TargetTruePeakDbTpProperty =
        AvaloniaProperty.Register<WaveCandyPanelControl, float>(nameof(TargetTruePeakDbTp), -1.0f);

    private const int FftSize = 1024;
    private const int WaveSize = 512;
    private const int ScopeSize = 256;

    private readonly DispatcherTimer _timer;
    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _wave = new float[WaveSize];
    private readonly float[] _scopeL = new float[ScopeSize];
    private readonly float[] _scopeR = new float[ScopeSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _mag = new float[FftSize / 2 + 1];

    private IBrush _backBrush = Brushes.Black;
    private IBrush _spectrumBrush = Brushes.Teal;
    private IPen _wavePen = new Pen(Brushes.Lime, 1);
    private IBrush _meterBrush = Brushes.SkyBlue;
    private IBrush _corrBrush = Brushes.Teal;
    private IBrush _labelBrush = Brushes.Gray;

    private float _peakL, _peakR, _rms, _correlation, _phaseDegrees;
    private float _stLufs = float.NegativeInfinity, _iLufs = float.NegativeInfinity, _maxDbTp = -120f;
    private bool _haveAnalyzer, _haveSpectrum, _haveWave;
    private int _scopeCount;
    private IBrush _warnBrush = Brushes.OrangeRed;
    private IPen _ceilingPen = new Pen(Brushes.Orange, 1);

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

    public float TargetTruePeakDbTp
    {
        get => GetValue(TargetTruePeakDbTpProperty);
        set => SetValue(TargetTruePeakDbTpProperty, value);
    }

    protected override void BuildThemeResources()
    {
        base.BuildThemeResources();
        _backBrush = new SolidColorBrush(ThemePalette.Mantle);
        _spectrumBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Teal, 0x88));
        _wavePen = new Pen(new SolidColorBrush(ThemePalette.Green), 1.2);
        _meterBrush = new SolidColorBrush(ThemePalette.Sapphire);
        _corrBrush = new SolidColorBrush(ThemePalette.Mauve);
        _labelBrush = new SolidColorBrush(ThemePalette.Overlay1);
        _warnBrush = new SolidColorBrush(ThemePalette.Red);
        _ceilingPen = new Pen(new SolidColorBrush(ThemePalette.Peach), 1);
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

        var showWave = WaveformSource is not null || SpectrumSource is IWaveformSource;
        var showSpec = SpectrumSource is not null;
        var showMeters = AnalyzerSource is not null || showWave || showSpec;

        double meterH = 0;
        if (showMeters)
        {
            if (!showWave && !showSpec)
                meterH = h; // analyser-only (Tool): meters fill the panel
            else
                meterH = _haveAnalyzer ? 58.0 : 28.0;
        }

        var waveH = showWave ? Math.Max(40, h * 0.22) : 0;
        var specH = showSpec ? Math.Max(20, h - meterH - waveH - 8) : 0;

        context.DrawRectangle(_backBrush, null, new Rect(0, 0, w, h));

        if (meterH > 0)
            DrawMeters(context, w, meterH);
        if (showWave)
            DrawWaveform(context, w, waveH, meterH + 4);
        if (showSpec)
            DrawSpectrum(context, w, specH, meterH + waveH + 8);
    }

    private void DrawMeters(DrawingContext ctx, double w, double h)
    {
        var peakH = _haveAnalyzer ? 22.0 : h;
        var scopeSize = _haveAnalyzer ? Math.Min(52.0, h - 4) : 0.0;
        var meterAreaW = w - scopeSize - (_haveAnalyzer ? 12 : 0);
        var barW = (meterAreaW - 24) / 2;
        var targetTp = TargetTruePeakDbTp;
        var warn = _maxDbTp > targetTp + 0.05f;
        var meterFill = warn ? _warnBrush : _meterBrush;
        DrawBar(ctx, 8, 4, barW, peakH - 8, _peakL, "L", meterFill);
        DrawBar(ctx, 16 + barW, 4, barW, peakH - 8, _peakR, "R", meterFill);

        // Delivery true-peak ceiling markers on peak bars
        var ceilNorm = MeterScale.NormalizeDb(targetTp);
        var ceilY = 4 + (peakH - 8) * (1 - ceilNorm);
        ctx.DrawLine(_ceilingPen, new Point(8, ceilY), new Point(8 + barW, ceilY));
        ctx.DrawLine(_ceilingPen, new Point(16 + barW, ceilY), new Point(16 + barW + barW, ceilY));

        static string Fmt(float v) => float.IsNegativeInfinity(v) ? "−∞" : v.ToString("0.0");
        var lufsText = $"ST {Fmt(_stLufs)}  I {Fmt(_iLufs)}  {_maxDbTp:0.0} dBTP";
        ctx.DrawText(new FormattedText(lufsText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 10, warn ? _warnBrush : _labelBrush),
            new Point(Math.Max(8, meterAreaW - 210), 6));

        if (_haveAnalyzer)
            DrawGoniometer(ctx, w - scopeSize - 4, 2, scopeSize, h - 4);

        if (!_haveAnalyzer) return;

        var corrY = peakH + 2;
        var corrH = 14.0;
        var label = new FormattedText($"Corr {_correlation:0.00}  Φ {_phaseDegrees:0}°  RMS {_rms:0.00}",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 9, _labelBrush);
        ctx.DrawText(label, new Point(8, corrY));

        var barX = 8 + label.Width + 10;
        var barW2 = Math.Max(40, meterAreaW - barX - 8);
        var barRect = new Rect(barX, corrY + 2, barW2, corrH - 4);
        ctx.DrawRectangle(null, new Pen(_labelBrush, 1), barRect);
        var mid = barRect.X + barRect.Width * 0.5;
        ctx.DrawLine(new Pen(_labelBrush, 1), new Point(mid, barRect.Y), new Point(mid, barRect.Bottom));
        var c = Math.Clamp(_correlation, -1f, 1f);
        if (Math.Abs(c) > 0.001f)
        {
            var fillW = Math.Abs(c) * barRect.Width * 0.5;
            var fillX = c >= 0 ? mid : mid - fillW;
            ctx.DrawRectangle(_corrBrush, null, new Rect(fillX, barRect.Y + 1, fillW, barRect.Height - 2));
        }
    }

    private void DrawGoniometer(DrawingContext ctx, double x, double y, double size, double h)
    {
        var sq = Math.Min(size, h);
        var rect = new Rect(x, y + (h - sq) * 0.5, sq, sq);
        ctx.DrawRectangle(null, new Pen(_labelBrush, 1), rect);
        ctx.DrawText(new FormattedText("Scope", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 8, _labelBrush),
            new Point(rect.X + 2, rect.Y + 1));

        var cx = rect.X + rect.Width * 0.5;
        var cy = rect.Y + rect.Height * 0.5;
        var half = rect.Width * 0.42;
        var diagPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Overlay1, 0x66)), 0.8);
        ctx.DrawLine(diagPen, new Point(cx - half, cy - half), new Point(cx + half, cy + half));
        ctx.DrawLine(diagPen, new Point(cx - half, cy + half), new Point(cx + half, cy - half));
        ctx.DrawLine(new Pen(_labelBrush, 0.6), new Point(cx - half, cy), new Point(cx + half, cy));
        ctx.DrawLine(new Pen(_labelBrush, 0.6), new Point(cx, cy - half), new Point(cx, cy + half));
        ctx.DrawText(new FormattedText("L", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 8, _labelBrush),
            new Point(cx + half - 8, cy + 2));
        ctx.DrawText(new FormattedText("R", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 8, _labelBrush),
            new Point(cx + 2, cy - half + 1));

        if (_scopeCount < 2) return;
        var scopePen = new Pen(_corrBrush, 1);
        var start = Math.Max(0, _scopeCount - 128);
        for (var i = start + 1; i < _scopeCount; i++)
        {
            var x0 = cx + _scopeL[i - 1] * half;
            var y0 = cy - _scopeR[i - 1] * half;
            var x1 = cx + _scopeL[i] * half;
            var y1 = cy - _scopeR[i] * half;
            ctx.DrawLine(scopePen, new Point(x0, y0), new Point(x1, y1));
        }
    }

    private void DrawBar(DrawingContext ctx, double x, double y, double bw, double bh, float level, string label, IBrush fill)
    {
        ctx.DrawRectangle(null, new Pen(_labelBrush, 1), new Rect(x, y, bw, bh));
        var fillH = bh * MeterScale.Normalize(level);
        ctx.DrawRectangle(fill, null, new Rect(x + 1, y + bh - fillH, bw - 2, fillH));
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
        // Frequency / dB axis hints
        ctx.DrawText(new FormattedText("0 dB", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 8, _labelBrush), new Point(4, top + 2));
        ctx.DrawText(new FormattedText("−84", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 8, _labelBrush), new Point(4, top + h - 12));
        ctx.DrawText(new FormattedText("20 Hz", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 8, _labelBrush), new Point(28, top + h - 12));
        ctx.DrawText(new FormattedText("20 kHz", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 8, _labelBrush), new Point(Math.Max(40, w - 40), top + h - 12));
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
        _haveAnalyzer = analyzer is not null;
        if (analyzer is not null)
        {
            _peakL = analyzer.PeakLeft;
            _peakR = analyzer.PeakRight;
            _rms = analyzer.Rms;
            _correlation = analyzer.Correlation;
            _phaseDegrees = analyzer.PhaseDegrees;
            _stLufs = analyzer.ShortTermLufs;
            _iLufs = analyzer.IntegratedLufs;
            _maxDbTp = analyzer.MaxTruePeakDbTp;
        }

        var wave = WaveformSource ?? SpectrumSource as IWaveformSource;
        if (wave is not null)
        {
            var n = wave.CaptureLatest(_wave);
            _haveWave = n > 0;
        }

        var stereo = SpectrumSource as IStereoScopeSource ?? WaveformSource as IStereoScopeSource;
        if (stereo is not null)
            _scopeCount = stereo.CaptureLatestStereo(_scopeL, _scopeR);
        else
            _scopeCount = 0;

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
