using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A classic analysis/synthesis (filter-bank) vocoder. The track this effect sits on is the
/// <i>modulator</i> (e.g. a voice); the user picks a <i>carrier</i> track whose output
/// is read through the engine's <see cref="ISidechainBus"/>. Each of N log-spaced bands of the carrier
/// is scaled by the modulator's envelope in that band, so the carrier "speaks" with the modulator's
/// articulation. Reuses <see cref="FilterBank"/> + <see cref="EnvelopeFollower"/>.
/// </summary>
public sealed class VocoderEffect : IAudioEffect, IContextualEffect, IProjectStatefulComponent,
    ISourceTrackEffect, IVocoderAnalysisSource
{
    public const string TypeId = "vocoder";

    private const double MinBandHz = 80.0;
    private const double MaxBandHz = 8000.0;

    private static readonly int[] BandOptions = { 8, 16, 24, 32 };
    private static readonly string[] BandNames = { "8", "16", "24", "32" };

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Vocoder";
    public bool Enabled { get; set; } = true;

    public int BandsIndex { get; set; } = 1;
    public double Mix { get; set; } = 1.0;
    public double AttackMs { get; set; } = 5.0;
    public double ReleaseMs { get; set; } = 30.0;
    public double OutputDb { get; set; }
    public double FormantShift { get; set; }
    public Guid? SourceTrackId { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private EffectContext? _ctx;
    private double _lastFormantShift = double.NaN;
    private float[] _bandLevels = Array.Empty<float>();

    int IVocoderAnalysisSource.BandCount =>
        BandOptions[Math.Clamp(BandsIndex, 0, BandOptions.Length - 1)];

    ReadOnlySpan<float> IVocoderAnalysisSource.BandLevels =>
        _bandLevels.AsSpan(0, BandOptions[Math.Clamp(BandsIndex, 0, BandOptions.Length - 1)]);

    private sealed class Graph
    {
        public required FilterBank[][] Mod;
        public required FilterBank[][] Car;
        public required EnvelopeFollower[][][] Env;
        public required float[] ModBuf;
        public required float[] CarBuf;
        public required int Channels;
    }

    private Graph? _graph;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Bands", BandNames, () => BandsIndex, v => BandsIndex = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v, "0%", "", 1.0),
        new FloatParameter("Attack", 0.1, 100.0, () => AttackMs, v => AttackMs = v, "0.#", "ms", 2.0),
        new FloatParameter("Release", 2.0, 500.0, () => ReleaseMs, v => ReleaseMs = v, "0", "ms", 2.0),
        new FloatParameter("Output", -24.0, 24.0, () => OutputDb, v => OutputDb = v, "0.#", "dB", 1.0),
        new FloatParameter("Formant Shift", -12.0, 12.0, () => FormantShift, v => FormantShift = v, "0.#", "st")
    };

    public void SetContext(EffectContext context) => _ctx = context;

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;

        var opts = BandOptions.Length;
        var mod = new FilterBank[opts][];
        var car = new FilterBank[opts][];
        var env = new EnvelopeFollower[opts][][];
        var maxBands = 0;

        for (var o = 0; o < opts; o++)
        {
            var bands = BandOptions[o];
            if (bands > maxBands) maxBands = bands;
            mod[o] = new FilterBank[_channels];
            car[o] = new FilterBank[_channels];
            env[o] = new EnvelopeFollower[_channels][];

            for (var ch = 0; ch < _channels; ch++)
            {
                mod[o][ch] = new FilterBank();
                car[o][ch] = new FilterBank();
                env[o][ch] = new EnvelopeFollower[bands];
                for (var b = 0; b < bands; b++)
                {
                    env[o][ch][b] = new EnvelopeFollower();
                    env[o][ch][b].SetTimes(AttackMs, ReleaseMs, _sampleRate);
                }
            }
        }

        _graph = new Graph
        {
            Mod = mod, Car = car, Env = env,
            ModBuf = new float[maxBands], CarBuf = new float[maxBands],
            Channels = _channels
        };
        _bandLevels = new float[maxBands];
        _lastFormantShift = double.NaN;
        ApplyFormantShift();
    }

    public void Process(Span<float> buffer)
    {
        var g = _graph;
        if (g is null) return;

        if (FormantShift != _lastFormantShift) ApplyFormantShift();

        var channels = g.Channels < 1 ? 1 : g.Channels;
        var frames = buffer.Length / channels;
        var opt = Math.Clamp(BandsIndex, 0, BandOptions.Length - 1);
        var bands = BandOptions[opt];

        if (_bandLevels.Length < bands) _bandLevels = new float[bands];

        var modBanks = g.Mod[opt];
        var carBanks = g.Car[opt];
        var envBanks = g.Env[opt];

        for (var ch = 0; ch < channels; ch++)
            for (var b = 0; b < bands; b++)
                envBanks[ch][b].SetTimes(AttackMs, ReleaseMs, _sampleRate);

        var src = ReadOnlySpan<float>.Empty;
        var srcChannels = 1;
        if (_ctx is not null && SourceTrackId is { } id)
        {
            _ctx.Sidechain.Request(id);
            src = _ctx.Sidechain.Read(id, out srcChannels);
        }

        var srcFrames = srcChannels > 0 ? src.Length / srcChannels : 0;
        if (srcFrames == 0) return;

        var mix = AudioMath.Clamp(Mix, 0.0, 1.0);
        var outGain = (float)AudioMath.Db2Lin(OutputDb);

        var modSpan = g.ModBuf.AsSpan(0, bands);
        var carSpan = g.CarBuf.AsSpan(0, bands);

        for (var f = 0; f < frames; f++)
        {
            Array.Clear(_bandLevels, 0, bands);

            for (var ch = 0; ch < channels; ch++)
            {
                var i = f * channels + ch;
                var dry = buffer[i];

                var carrier = 0f;
                if (f < srcFrames)
                {
                    var sc = ch < srcChannels ? ch : srcChannels - 1;
                    carrier = src[f * srcChannels + sc];
                }

                modBanks[ch].Process(dry, modSpan);
                carBanks[ch].Process(carrier, carSpan);

                float wet = 0f;
                var env = envBanks[ch];
                for (var b = 0; b < bands; b++)
                {
                    var m = modSpan[b];
                    if (m < 0) m = -m;
                    var e = (float)env[b].Process(m);
                    _bandLevels[b] = Math.Max(_bandLevels[b], e);
                    wet += carSpan[b] * e;
                }

                wet *= outGain;
                buffer[i] = (float)(dry * (1.0 - mix) + wet * mix);
            }
        }
    }

    private void ApplyFormantShift()
    {
        var g = _graph;
        if (g is null) return;

        var ratio = Math.Pow(2.0, Math.Clamp(FormantShift, -12, 12) / 12.0);
        var minHz = Math.Clamp(MinBandHz * ratio, 20, 16000);
        var maxHz = Math.Clamp(MaxBandHz * ratio, minHz * 2, 20000);

        for (var o = 0; o < BandOptions.Length; o++)
        {
            var bands = BandOptions[o];
            for (var ch = 0; ch < g.Channels; ch++)
            {
                g.Mod[o][ch].Configure(bands, minHz, maxHz, _sampleRate);
                g.Car[o][ch].Configure(bands, minHz, maxHz, _sampleRate);
            }
        }

        _lastFormantShift = FormantShift;
    }

    public IAudioEffect Clone() => new VocoderEffect
    {
        Enabled = Enabled,
        BandsIndex = BandsIndex,
        Mix = Mix,
        AttackMs = AttackMs,
        ReleaseMs = ReleaseMs,
        OutputDb = OutputDb,
        FormantShift = FormantShift,
        SourceTrackId = SourceTrackId
    };

    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteBool(SourceTrackId.HasValue);
        writer.WriteGuid(SourceTrackId ?? Guid.Empty);
    }

    public void ReadProjectState(OngenReader reader)
    {
        var has = reader.ReadBool();
        var id = reader.ReadGuid();
        SourceTrackId = has ? id : null;
    }
}
