using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>Karplus-Strong plucked-string oscillator driven by pitch and gate.</summary>
public sealed class KarplusNode : FieldNode
{
    public const string Type = "osc.karplus";
    public override string TypeId => Type;
    public override string DisplayName => "Karplus";
    public override string Category => FieldNodeCategories.Oscillators;

    public double Damping { get; set; } = 0.5;
    public double PickPosition { get; set; } = 0.5;
    public double Brightness { get; set; } = 0.5;
    public double Excitation { get; set; } = 1.0;
    public double Level { get; set; } = 0.8;

    private KarplusStrongDsp[] _ks = Array.Empty<KarplusStrongDsp>();
    private float[] _prevGate = Array.Empty<float>();
    private double[] _lastFreq = Array.Empty<double>();

    public KarplusNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddInput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Damping", 0, 1, () => Damping, v => Damping = v, "0.00"));
        AddParam(new FloatParameter("Pick", 0, 1, () => PickPosition, v => PickPosition = v, "0.00"));
        AddParam(new FloatParameter("Bright", 0, 1, () => Brightness, v => Brightness = v, "0.00"));
        AddParam(new FloatParameter("Excite", 0, 2, () => Excitation, v => Excitation = v, "0.00"));
        AddParam(new FloatParameter("Level", 0, 1, () => Level, v => Level = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _ks = new KarplusStrongDsp[VoiceCount];
        _prevGate = new float[VoiceCount];
        _lastFreq = new double[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _ks[i] = new KarplusStrongDsp();
            _ks[i].Prepare(format.SampleRate);
            _lastFreq[i] = double.NaN;
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _ks.Length) return;
        _prevGate[voice] = 0;
        _lastFreq[voice] = double.NaN;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var ks = _ks[v];
        var pitch = ctx.Input(0);
        var gate = ctx.Input(1);
        var outBuf = ctx.Output(0);
        var g0 = gate.Length > 0 ? gate[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f)
            ks.Pluck((float)ModValue(ctx, 3, Excitation, 0));
        _prevGate[v] = g0;

        for (var i = 0; i < ctx.Frames; i++)
        {
            var freq = pitch[i] > 1e-6 ? pitch[i] : 440.0;
            if (freq != _lastFreq[v])
            {
                ks.Damping = ModValue(ctx, 0, Damping, i);
                ks.PickPosition = ModValue(ctx, 1, PickPosition, i);
                ks.Brightness = ModValue(ctx, 2, Brightness, i);
                ks.SetFrequency(freq);
                _lastFreq[v] = freq;
            }

            outBuf[i] = ks.Process() * (float)ModValue(ctx, 4, Level, i);
        }
    }
}

/// <summary>Additive partial-bank oscillator; optional spectrum asset from <see cref="SpectralImportNode"/>.</summary>
public sealed class PartialBankNode : FieldNode, IFieldAssetConsumer
{
    public const string Type = "osc.partials";
    public override string TypeId => Type;
    public override string DisplayName => "Partial Bank";
    public override string Category => FieldNodeCategories.Oscillators;

    public int PartialCount { get; set; } = 16;
    public double Level { get; set; } = 0.7;

    private AdditivePartialEngine[] _engine = Array.Empty<AdditivePartialEngine>();
    private SpectralMagnitudeBank? _spectrum;
    private int _lastRevision = -1;

    public PartialBankNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddInput("spectrum", "Spectrum", FieldSignalKind.Asset);
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Partials", 1, AdditivePartialEngine.MaxPartials,
            () => PartialCount, v => PartialCount = (int)Math.Round(v), "0"), modulatable: false);
        AddParam(new FloatParameter("Level", 0, 1, () => Level, v => Level = v, "0.00"));
        Build();
    }

    public void SetAsset(string portId, object? asset)
    {
        if (portId != "spectrum") return;
        _spectrum = asset as SpectralMagnitudeBank;
        _lastRevision = -1;
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _engine = new AdditivePartialEngine[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _engine[i] = new AdditivePartialEngine();
            _engine[i].SetSampleRate(format.SampleRate);
            SeedDefaultPartials(_engine[i]);
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _engine.Length) _engine[voice].ResetPhases();
    }

    private void SeedDefaultPartials(AdditivePartialEngine engine)
    {
        var count = Math.Clamp(PartialCount, 1, AdditivePartialEngine.MaxPartials);
        engine.PartialCount = count;
        for (var h = 0; h < count; h++)
            engine.SetPartial(h, h + 1, 1.0 / (h + 1));
    }

    private void SyncSpectrum(AdditivePartialEngine engine)
    {
        if (_spectrum is null || _spectrum.Revision == _lastRevision) return;
        _lastRevision = _spectrum.Revision;
        engine.PartialCount = Math.Clamp(PartialCount, 1, AdditivePartialEngine.MaxPartials);
        engine.ImportSpectrum(_spectrum.Magnitudes, Math.Min(_spectrum.BinCount, _spectrum.Magnitudes.Length));
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var engine = _engine[ctx.Voice];
        var pitch = ctx.Input(0);
        var outBuf = ctx.Output(0);
        SyncSpectrum(engine);
        engine.PartialCount = Math.Clamp(PartialCount, 1, AdditivePartialEngine.MaxPartials);

        for (var i = 0; i < ctx.Frames; i++)
        {
            var freq = pitch[i] > 1e-6 ? pitch[i] : 440.0;
            engine.SetFundamental(freq);
            outBuf[i] = engine.Process() * (float)ModValue(ctx, 1, Level, i);
        }
    }
}

/// <summary>FFT magnitude analyzer that feeds a connected <see cref="PartialBankNode"/>.</summary>
public sealed class SpectralImportNode : FieldNode, IFieldAssetProvider
{
    public const string Type = "spectral.import";
    public override string TypeId => Type;
    public override string DisplayName => "Spectral Import";
    public override string Category => FieldNodeCategories.Spectral;
    public override bool ForceGlobal => true;

    public double Sensitivity { get; set; } = 0.02;

    private const int FftSize = 2048;
    private readonly SpectralMagnitudeBank _bank = new();
    private readonly float[] _scratch = new float[FftSize];
    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly float[] _mags = new float[FftSize / 2];
    private float[] _prevGate = Array.Empty<float>();

    public SpectralImportNode()
    {
        AddInput("in", "In");
        AddInput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("spectrum", "Spectrum", FieldSignalKind.Asset);
        AddParam(new FloatParameter("Sense", 0.001, 1, () => Sensitivity, v => Sensitivity = v, "0.000"));
        Build();
    }

    public object? GetAsset(string portId) => portId == "spectrum" ? _bank : null;

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _prevGate = new float[1];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var gate = ctx.Input(1);
        var g0 = gate.Length > 0 ? gate[0] : 1f;
        var trigger = _prevGate[0] <= 0.5f && g0 > 0.5f;
        _prevGate[0] = g0;

        var frames = Math.Min(ctx.Frames, FftSize);
        Array.Clear(_scratch, 0, _scratch.Length);
        for (var i = 0; i < frames; i++) _scratch[i] = input[i];

        var rms = 0.0;
        for (var i = 0; i < frames; i++) rms += _scratch[i] * _scratch[i];
        rms = Math.Sqrt(rms / Math.Max(1, frames));

        if (!trigger && rms < Sensitivity) return;

        for (var i = 0; i < FftSize; i++)
        {
            _re[i] = i < frames ? _scratch[i] : 0;
            _im[i] = 0;
        }

        Fft.Forward(_re, _im);
        for (var k = 0; k < _mags.Length; k++)
            _mags[k] = (float)Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);

        _bank.SetMagnitudes(_mags, _mags.Length);
    }
}
