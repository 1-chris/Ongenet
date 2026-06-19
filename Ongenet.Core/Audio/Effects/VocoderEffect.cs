using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A classic analysis/synthesis (filter-bank) vocoder. The track this effect sits on is the
/// <i>modulator</i> (e.g. a voice); the user picks a <i>carrier</i> track (e.g. a synth) whose output
/// is read through the engine's <see cref="ISidechainBus"/>. Each of N log-spaced bands of the carrier
/// is scaled by the modulator's envelope in that band, so the carrier "speaks" with the modulator's
/// articulation. Reuses <see cref="FilterBank"/> + <see cref="EnvelopeFollower"/>.
/// </summary>
public sealed class VocoderEffect : IAudioEffect, IContextualEffect, IProjectStatefulComponent, ISourceTrackEffect
{
    public const string TypeId = "vocoder";

    private const double MinBandHz = 80.0;
    private const double MaxBandHz = 8000.0;

    private static readonly int[] BandOptions = { 8, 16, 24, 32 };
    private static readonly string[] BandNames = { "8", "16", "24", "32" };

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Vocoder";
    public bool Enabled { get; set; } = true;

    /// <summary>Index into <see cref="BandOptions"/> (number of analysis bands); default 16.</summary>
    public int BandsIndex { get; set; } = 1;

    /// <summary>Dry/wet blend (0 = dry modulator, 1 = full vocoded).</summary>
    public double Mix { get; set; } = 1.0;

    /// <summary>Band-envelope attack (ms) — lower tracks the modulator's transients more sharply.</summary>
    public double AttackMs { get; set; } = 5.0;

    /// <summary>Band-envelope release (ms).</summary>
    public double ReleaseMs { get; set; } = 30.0;

    /// <summary>Make-up gain applied to the vocoded signal (dB).</summary>
    public double OutputDb { get; set; } = 0.0;

    /// <summary>Carrier track/group whose output is shaped by the modulator; null = bypass (dry).</summary>
    public Guid? SourceTrackId { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private EffectContext? _ctx;

    // The whole DSP graph (filter banks + envelopes + scratch buffers, pre-built for every band-count
    // option). It is built entirely on the prepare thread and published with a single atomic field
    // assignment, so the audio thread's Process always sees a complete graph — never a half-filled
    // array. Process performs NO allocation, so it can't race a rebuild.
    private sealed class Graph
    {
        public required FilterBank[][] Mod;          // [bandOption][channel]
        public required FilterBank[][] Car;          // [bandOption][channel]
        public required EnvelopeFollower[][][] Env;  // [bandOption][channel][band]
        public required float[] ModBuf;              // scratch, sized to the max band count
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
        new FloatParameter("Output", -24.0, 24.0, () => OutputDb, v => OutputDb = v, "0.#", "dB", 1.0)
    };

    public void SetContext(EffectContext context) => _ctx = context;

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;

        // Pre-build the graph for every band-count option so a live Bands change is just an index
        // switch in Process (no audio-thread allocation). Build into locals, publish atomically last.
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
                mod[o][ch].Configure(bands, MinBandHz, MaxBandHz, _sampleRate);
                car[o][ch] = new FilterBank();
                car[o][ch].Configure(bands, MinBandHz, MaxBandHz, _sampleRate);

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
    }

    public void Process(Span<float> buffer)
    {
        var g = _graph;            // single atomic read: a complete graph or null
        if (g is null) return;

        var channels = g.Channels < 1 ? 1 : g.Channels;
        var frames = buffer.Length / channels;
        var opt = Math.Clamp(BandsIndex, 0, BandOptions.Length - 1);
        var bands = BandOptions[opt];

        var modBanks = g.Mod[opt];
        var carBanks = g.Car[opt];
        var envBanks = g.Env[opt];

        // Refresh envelope ballistics (cheap, no allocation; lets Attack/Release respond live).
        for (var ch = 0; ch < channels; ch++)
            for (var b = 0; b < bands; b++)
                envBanks[ch][b].SetTimes(AttackMs, ReleaseMs, _sampleRate);

        // Fetch the carrier track's output from the sidechain bus.
        var src = ReadOnlySpan<float>.Empty;
        var srcChannels = 1;
        if (_ctx is not null && SourceTrackId is { } id)
        {
            _ctx.Sidechain.Request(id);
            src = _ctx.Sidechain.Read(id, out srcChannels);
        }

        var srcFrames = srcChannels > 0 ? src.Length / srcChannels : 0;
        if (srcFrames == 0) return; // no carrier → leave the dry modulator untouched

        var mix = AudioMath.Clamp(Mix, 0.0, 1.0);
        var outGain = (float)AudioMath.Db2Lin(OutputDb);

        var modSpan = g.ModBuf.AsSpan(0, bands);
        var carSpan = g.CarBuf.AsSpan(0, bands);

        for (var f = 0; f < frames; f++)
        {
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
                    wet += carSpan[b] * e;
                }

                wet *= outGain;
                buffer[i] = (float)(dry * (1.0 - mix) + wet * mix);
            }
        }
    }

    public IAudioEffect Clone() => new VocoderEffect
    {
        Enabled = Enabled,
        BandsIndex = BandsIndex,
        Mix = Mix,
        AttackMs = AttackMs,
        ReleaseMs = ReleaseMs,
        OutputDb = OutputDb,
        SourceTrackId = SourceTrackId
    };

    // The carrier-track reference isn't a generic Parameter, so persist it as custom state.
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
