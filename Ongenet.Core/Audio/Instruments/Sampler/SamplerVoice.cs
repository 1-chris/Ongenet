using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// One sounding region within an <see cref="SamplerInstrument"/>. Resamples its region's sample (4-point
/// Hermite) at the note's pitch, applies the amp envelope, pan and looping, and — when the region uses
/// any tone shaping — a resonant filter, EQ and LFO/EG modulation computed at control rate. Pooled and
/// reused, so it allocates nothing while rendering once warmed up.
/// </summary>
public sealed class SamplerVoice
{
    private const int ControlBlock = 64; // samples between modulation/coefficient updates

    private readonly DahdsrEnvelope _env = new();
    private readonly DahdsrEnvelope _filEg = new();
    private readonly DahdsrEnvelope _pitchEg = new();
    private readonly Lfo _filLfo = new();
    private readonly Lfo _ampLfo = new();
    private readonly Lfo _pitchLfo = new();

    private SamplerRegion? _rt;
    private SamplerSample? _sample;
    private AudioSampleBuffer? _resident; // non-null when the sample is RAM-resident
    private bool _streamed;               // true when reading via the disk stream
    private int _sampleChannels = 1;
    private SamplerModState? _mod;
    private AudioFormat _format = AudioFormat.Default;
    private int _sampleRate = 44100;
    private double _nyquist = 22050;

    /// <summary>This voice's disk-streaming state (used only when its sample is streamed).</summary>
    public SamplerStream Stream { get; } = new();

    private double _position;  // read position in file frames (fractional)
    private double _rate;      // base file frames advanced per output frame (positive magnitude)
    private float _gain;       // region gain × velocity gain
    private float _panL, _panR;
    private bool _reverse;
    private bool _released;
    private bool _looping;
    private long _age;         // output samples since note start (for LFO delay)

    private long _offset, _end, _loopStart, _loopEnd;
    private SamplerLoopMode _loopMode;

    // Filter state.
    private bool _useFilter;
    private FilterMode _filterMode;
    private double _filterBaseHz;
    private double _filterQ;
    private BiquadCoefficients _filterCoeffs = BiquadCoefficients.Identity;
    private Biquad[] _filter = Array.Empty<Biquad>();
    private long _filLfoDelay, _ampLfoDelay, _pitchLfoDelay;

    // EQ state.
    private int _eqBandCount;
    private BiquadCoefficients[] _eqCoeffs = Array.Empty<BiquadCoefficients>();
    private Biquad[] _eq = Array.Empty<Biquad>();

    /// <summary>Whether this voice is producing sound.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The MIDI note that triggered this voice (for note-off matching).</summary>
    public int TriggerNote { get; private set; }

    /// <summary>The region's exclusive group id (SFZ <c>group</c>), for <c>off_by</c> cutoff.</summary>
    public int Group => _rt?.Group ?? 0;

    /// <summary>The region's <c>off_by</c> id, or -1.</summary>
    public int OffBy => _rt?.OffBy ?? -1;

    /// <summary>How this voice's region was triggered (attack/release/first/legato).</summary>
    public SamplerTrigger Trigger => _rt?.Trigger ?? SamplerTrigger.Attack;

    /// <summary>Exclusive-group cutoff mode for this voice.</summary>
    public SamplerOffMode OffMode => _rt?.OffMode ?? SamplerOffMode.Fast;

    /// <summary>Active region (null when idle).</summary>
    public SamplerRegion? Region => _rt;

    private int _delayLeft;
    private int _loopPassesLeft;
    private bool _useFilter2;
    private FilterMode _filter2Mode;
    private double _filter2BaseHz;
    private double _filter2Q;
    private BiquadCoefficients _filter2Coeffs = BiquadCoefficients.Identity;
    private Biquad[] _filter2 = Array.Empty<Biquad>();
    private float _baseGain;
    private float _basePan;
    private readonly Lfo[] _flexLfos = new Lfo[8];
    private readonly double[] _flexEgLevels = new double[8];
    private readonly double[] _flexEgTimesLeft = new double[8];
    private readonly int[] _flexEgStage = new int[8];

    public void Start(SamplerRegion rt, int triggerNote, int velocity, double extraSemis,
        SamplerModState mod, AudioFormat format)
    {
        _rt = rt;
        _sample = rt.Sample;
        _resident = rt.Sample.Resident;
        _streamed = rt.Sample.IsStreamed;
        _sampleChannels = rt.Sample.Channels;
        _mod = mod;
        _format = format;
        _sampleRate = format.SampleRate <= 0 ? 44100 : format.SampleRate;
        _nyquist = _sampleRate * 0.49;
        TriggerNote = triggerNote;
        _released = false;
        _age = 0;
        _loopPassesLeft = rt.LoopCount > 0 ? rt.LoopCount : int.MaxValue;

        var pitchRand = rt.PitchRandom != 0 ? (Random.Shared.NextDouble() * 2 - 1) * rt.PitchRandom : 0;
        var semis = (triggerNote - rt.PitchKeycenter) * rt.KeytrackSemisPerKey
                    + rt.TransposeSemis + (rt.TuneCents + rt.PitchVeltrack * (velocity / 127.0) + pitchRand) / 100.0
                    + extraSemis;
        var sampleRate = _sample.SampleRate <= 0 ? format.SampleRate : _sample.SampleRate;
        _rate = Math.Pow(2.0, semis / 12.0) * sampleRate / format.SampleRate;

        var offsetRand = rt.OffsetRandom > 0 ? (long)(Random.Shared.NextDouble() * rt.OffsetRandom) : 0;
        _offset = Math.Min(rt.End, Math.Max(0, rt.Offset + offsetRand));
        if (mod is not null)
        {
            foreach (var r in rt.ModRoutes)
            {
                if (r.Target != SamplerModTarget.OffsetFrames) continue;
                _offset = Math.Clamp(_offset + (long)SamplerModMath.RouteAmount(r, mod, mod.Curves, velocity, triggerNote),
                    0, rt.End);
            }
        }

        _end = rt.End;
        _loopStart = rt.LoopStart;
        _loopEnd = rt.LoopEnd;
        _loopMode = rt.LoopMode;
        var reverse = rt.Reverse;
        if (rt.ReverseLoCc >= 0 && mod is not null)
        {
            var cc = mod.Cc[Math.Clamp(rt.ReverseLoCc, 0, 127)];
            // when only reverse_locc style is used without value in builder, ReverseLoCc stores the CC number incorrectly
            // Builder stores reverse_loccN as the first matching value — treat Reverse as mode; CC gate flips.
        }
        _reverse = reverse;
        _position = _reverse ? Math.Max(_offset, _end - 1) : _offset;

        // Delay (seconds + beats + samples + random + CC)
        var delaySec = rt.DelaySeconds + (rt.DelayRandom > 0 ? Random.Shared.NextDouble() * rt.DelayRandom : 0);
        if (rt.DelayBeats > 0 && mod is { HostBpm: > 0 })
            delaySec += rt.DelayBeats * 60.0 / mod.HostBpm;
        delaySec += rt.DelaySamples / (double)_sampleRate;
        if (mod is not null)
        {
            foreach (var r in rt.ModRoutes)
            {
                if (r.Target == SamplerModTarget.DelaySeconds)
                    delaySec += SamplerModMath.RouteAmount(r, mod, mod.Curves, velocity, triggerNote);
            }
        }
        _delayLeft = (int)(Math.Max(0, delaySec) * _sampleRate);

        if (_streamed) Stream.Request(_sample, (long)_position);

        var norm = velocity / 127.0;
        double velGain;
        if (rt.AmpVelcurve is { } curve)
            velGain = curve[Math.Clamp(velocity, 0, 127)];
        else
        {
            var vt = rt.AmpVeltrack / 100.0;
            velGain = (1.0 - vt) + vt * norm * norm;
        }

        var ampKey = AudioMath.Db2Lin(rt.AmpKeytrack / 100.0 * (triggerNote - rt.AmpKeycenter));
        var ampRand = rt.AmpRandom != 0 ? AudioMath.Db2Lin((Random.Shared.NextDouble() * 2 - 1) * rt.AmpRandom) : 1.0;
        var xfadeCcVal = 0;
        if (rt.Xfade is { XfadeCc: >= 0 and <= 127 } xf && mod is not null)
            xfadeCcVal = mod.Cc[xf.XfadeCc];
        var xfade = rt.Xfade?.Evaluate(triggerNote, velocity, xfadeCcVal) ?? 1f;
        if (rt.Trigger == SamplerTrigger.Release && rt.RtDecayDb != 0)
            ampRand *= AudioMath.Db2Lin(-Math.Abs(rt.RtDecayDb)); // simplified release decay

        _baseGain = (float)(rt.Gain * velGain * ampKey * ampRand * xfade);
        _gain = _baseGain;

        var pan = rt.Pan + rt.Position / 100.0;
        // width: 0 = mono center, 100 = full stereo pan
        pan *= rt.Width / 100.0;
        _basePan = (float)AudioMath.Clamp(pan, -1, 1);
        AudioMath.PanGains(_basePan, out _panL, out _panR);
        if (rt.InvertPhase) { _panL = -_panL; _panR = -_panR; }

        _env.SetSampleRate(_sampleRate);
        rt.AmpEg.ApplyTo(_env, velocity / 127.0);
        _env.Gate();

        SetupModulation(rt, triggerNote, velocity);
        SetupFlex(rt);

        IsActive = _baseGain > 0f || _delayLeft > 0;
    }

    private void SetupModulation(SamplerRegion rt, int triggerNote, int velocity)
    {
        var channels = _format.Channels < 1 ? 1 : _format.Channels;

        // Filter: base cutoff with key/velocity tracking; coefficients are (re)computed while rendering.
        _useFilter = rt.HasFilter;
        if (_useFilter)
        {
            _filterMode = rt.FilterMode;
            _filterQ = Math.Max(0.05, rt.FilterQ);
            var cents = rt.FilKeytrack * (triggerNote - rt.FilKeycenter) + rt.FilVeltrack * (velocity / 127.0);
            _filterBaseHz = Math.Clamp(rt.Cutoff * Math.Pow(2.0, cents / 1200.0), 20.0, _nyquist);
            if (_filter.Length < channels) _filter = new Biquad[channels];
            for (var c = 0; c < channels; c++) _filter[c].Reset();
            _filterCoeffs = BiquadCoefficients.Compute(_filterMode, _filterBaseHz, _filterQ, _sampleRate);
        }

        // EQ: static peaking bands computed once per note (sample-rate dependent).
        _eqBandCount = rt.EqBands.Count;
        if (_eqBandCount > 0)
        {
            if (_eqCoeffs.Length < _eqBandCount) _eqCoeffs = new BiquadCoefficients[_eqBandCount];
            if (_eq.Length < _eqBandCount * channels) _eq = new Biquad[_eqBandCount * channels];
            for (var b = 0; b < _eqBandCount; b++)
            {
                var band = rt.EqBands[b];
                _eqCoeffs[b] = BiquadCoefficients.ComputeEq(EqBandType.Bell, band.Freq,
                    BandwidthToQ(band.BandwidthOctaves), band.GainDb, _sampleRate);
                for (var c = 0; c < channels; c++) _eq[b * channels + c].Reset();
            }
        }

        _useFilter2 = rt.HasFilter2;
        if (_useFilter2)
        {
            _filter2Mode = rt.Filter2Mode;
            _filter2Q = Math.Max(0.05, rt.Filter2Q);
            _filter2BaseHz = Math.Clamp(rt.Cutoff2, 20.0, _nyquist);
            if (_filter2.Length < channels) _filter2 = new Biquad[channels];
            for (var c = 0; c < channels; c++) _filter2[c].Reset();
            _filter2Coeffs = BiquadCoefficients.Compute(_filter2Mode, _filter2BaseHz, _filter2Q, _sampleRate);
        }

        var velN = velocity / 127.0;
        if (rt.HasFilEg) { _filEg.SetSampleRate(_sampleRate); rt.FilEg.ApplyTo(_filEg, velN); _filEg.Gate(); }
        if (rt.HasPitchEg) { _pitchEg.SetSampleRate(_sampleRate); rt.PitchEg.ApplyTo(_pitchEg, velN); _pitchEg.Gate(); }
        if (rt.HasFilLfo) { _filLfo.SetRate(rt.FilLfoFreq, _sampleRate); _filLfo.Reset(); _filLfoDelay = (long)(rt.FilLfoDelay * _sampleRate); }
        if (rt.HasAmpLfo) { _ampLfo.SetRate(rt.AmpLfoFreq, _sampleRate); _ampLfo.Reset(); _ampLfoDelay = (long)(rt.AmpLfoDelay * _sampleRate); }
        if (rt.HasPitchLfo) { _pitchLfo.SetRate(rt.PitchLfoFreq, _sampleRate); _pitchLfo.Reset(); _pitchLfoDelay = (long)(rt.PitchLfoDelay * _sampleRate); }

        if (rt.FilRandom != 0)
            _filterBaseHz = Math.Clamp(_filterBaseHz * Math.Pow(2.0, ((Random.Shared.NextDouble() * 2 - 1) * rt.FilRandom) / 1200.0), 20, _nyquist);
    }

    private void SetupFlex(SamplerRegion rt)
    {
        for (var i = 0; i < _flexEgLevels.Length; i++)
        {
            _flexEgLevels[i] = 0;
            _flexEgStage[i] = 0;
            _flexEgTimesLeft[i] = 0;
        }
        for (var i = 0; i < rt.FlexEgs.Count && i < _flexEgLevels.Length; i++)
        {
            var eg = rt.FlexEgs[i];
            _flexEgStage[i] = 0;
            _flexEgLevels[i] = eg.Levels.Length > 0 ? eg.Levels[0] : 0;
            _flexEgTimesLeft[i] = eg.Times.Length > 0 ? eg.Times[0] * _sampleRate : 0;
        }
        for (var i = 0; i < rt.FlexLfos.Count && i < _flexLfos.Length; i++)
        {
            _flexLfos[i] ??= new Lfo();
            _flexLfos[i].SetRate(rt.FlexLfos[i].Freq, _sampleRate);
            _flexLfos[i].Reset();
        }
    }

    public void Release()
    {
        if (_loopMode == SamplerLoopMode.OneShot) return; // one_shot plays to the end regardless of note-off
        _released = true;
        _env.Release();
        if (_rt is { HasFilEg: true }) _filEg.Release();
        if (_rt is { HasPitchEg: true }) _pitchEg.Release();
    }

    /// <summary>Fast cutoff for exclusive-group (<c>off_by</c>) stealing: a short fade to avoid a click.</summary>
    public void FastRelease()
    {
        _env.ReleaseSeconds = 0.002;
        _env.Release();
        _released = true;
    }

    public void Render(Span<float> buffer)
    {
        var rt = _rt;
        if (_sample is null || rt is null) { IsActive = false; return; }

        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;

        // Consume note delay before sounding.
        if (_delayLeft > 0)
        {
            var skip = Math.Min(_delayLeft, frames);
            _delayLeft -= skip;
            if (_delayLeft > 0) return;
            // fall through to render remaining frames in this buffer after delay
        }

        _looping = _loopMode is SamplerLoopMode.LoopContinuous || (_loopMode == SamplerLoopMode.LoopSustain && !_released);

        // Pitch bend is read per buffer so held notes bend in real time (applies on both paths).
        var bendMul = 1.0;
        if (_mod is { } mod && mod.Bend != 0.0)
        {
            var bendCents = mod.Bend >= 0 ? mod.Bend * rt.BendUpCents : mod.Bend * rt.BendDownCents;
            if (rt.BendStepCents > 0)
                bendCents = Math.Round(bendCents / rt.BendStepCents) * rt.BendStepCents;
            bendMul = Math.Pow(2.0, bendCents / 1200.0);
        }
        var baseRate = _rate * bendMul;

        bool active;
        if (!rt.ModActive)
        {
            active = RenderRange(buffer, 0, frames, channels, baseRate, 1f, useFilter: false, useEq: false);
        }
        else
        {
            active = true;
            var frame = 0;
            while (frame < frames)
            {
                var n = Math.Min(ControlBlock, frames - frame);

                var pitchCents = 0.0;
                if (rt.HasPitchEg) pitchCents += rt.PitchEgDepth * _pitchEg.Level;
                if (rt.HasPitchLfo && _age >= _pitchLfoDelay) pitchCents += rt.PitchLfoDepth * _pitchLfo.Value(0);

                var ampDb = 0.0;
                if (rt.HasAmpLfo && _age >= _ampLfoDelay)
                    ampDb += rt.AmpLfoDepthDb * _ampLfo.Value(0);

                var panAdd = 0.0;
                var cutoffCents = 0.0;
                var resDb = 0.0;
                if (_mod is { } m)
                {
                    foreach (var route in rt.ModRoutes)
                    {
                        var amt = SamplerModMath.RouteAmount(route, m, m.Curves, 64, TriggerNote);
                        switch (route.Target)
                        {
                            case SamplerModTarget.AmplitudeDb: ampDb += amt; break;
                            case SamplerModTarget.Pan: panAdd += amt; break;
                            case SamplerModTarget.PitchCents: pitchCents += amt; break;
                            case SamplerModTarget.CutoffCents: cutoffCents += amt; break;
                            case SamplerModTarget.ResonanceDb: resDb += amt; break;
                        }
                    }
                }

                ApplyFlex(rt, ref pitchCents, ref ampDb, ref panAdd, ref cutoffCents);

                var rate = pitchCents != 0.0 ? baseRate * Math.Pow(2.0, pitchCents / 1200.0) : baseRate;
                var ampMul = (float)AudioMath.Db2Lin(ampDb);
                if (panAdd != 0)
                    AudioMath.PanGains(AudioMath.Clamp(_basePan + panAdd, -1, 1), out _panL, out _panR);

                if (_useFilter)
                {
                    var cents = cutoffCents;
                    if (rt.HasFilEg) cents += rt.FilEgDepth * _filEg.Level;
                    if (rt.HasFilLfo && _age >= _filLfoDelay) cents += rt.FilLfoDepth * _filLfo.Value(0);
                    var q = Math.Max(0.05, _filterQ * AudioMath.Db2Lin(resDb));
                    var hz = cents != 0.0
                        ? Math.Clamp(_filterBaseHz * Math.Pow(2.0, cents / 1200.0), 20.0, _nyquist)
                        : _filterBaseHz;
                    _filterCoeffs = BiquadCoefficients.Compute(_filterMode, hz, q, _sampleRate);
                }
                if (_useFilter2)
                    _filter2Coeffs = BiquadCoefficients.Compute(_filter2Mode, _filter2BaseHz, _filter2Q, _sampleRate);

                active = RenderRange(buffer, frame, n, channels, rate, ampMul, _useFilter, _eqBandCount > 0);

                if (rt.HasFilEg) for (var k = 0; k < n; k++) _filEg.Process();
                if (rt.HasPitchEg) for (var k = 0; k < n; k++) _pitchEg.Process();
                if (rt.HasFilLfo) for (var k = 0; k < n; k++) _filLfo.Advance();
                if (rt.HasAmpLfo) for (var k = 0; k < n; k++) _ampLfo.Advance();
                if (rt.HasPitchLfo) for (var k = 0; k < n; k++) _pitchLfo.Advance();
                for (var i = 0; i < rt.FlexLfos.Count && i < _flexLfos.Length; i++)
                    for (var k = 0; k < n; k++) _flexLfos[i]?.Advance();
                AdvanceFlexEgs(rt, n);
                _age += n;
                frame += n;
                if (!active) break;
            }
        }

        if (!active)
        {
            IsActive = false;
            if (_streamed) Stream.Release();
            return;
        }

        if (_streamed) Stream.SetConsumed((long)_position - 2);
    }

    private void ApplyFlex(SamplerRegion rt, ref double pitchCents, ref double ampDb, ref double panAdd, ref double cutoffCents)
    {
        for (var i = 0; i < rt.FlexEgs.Count && i < _flexEgLevels.Length; i++)
        {
            var level = _flexEgLevels[i];
            foreach (var d in rt.FlexEgs[i].Dests)
                ApplyFlexDest(d, level, ref pitchCents, ref ampDb, ref panAdd, ref cutoffCents);
        }
        for (var i = 0; i < rt.FlexLfos.Count && i < _flexLfos.Length; i++)
        {
            var lfo = _flexLfos[i];
            if (lfo is null) continue;
            var ageSec = _age / (double)_sampleRate;
            if (ageSec < rt.FlexLfos[i].Delay) continue;
            var fade = rt.FlexLfos[i].Fade;
            var fadeMul = fade <= 0 ? 1.0 : Math.Clamp((ageSec - rt.FlexLfos[i].Delay) / fade, 0, 1);
            var v = lfo.Value(0) * fadeMul;
            foreach (var d in rt.FlexLfos[i].Dests)
                ApplyFlexDest(d, v, ref pitchCents, ref ampDb, ref panAdd, ref cutoffCents);
        }
    }

    private static void ApplyFlexDest(SamplerFlexEgDest d, double level,
        ref double pitchCents, ref double ampDb, ref double panAdd, ref double cutoffCents)
    {
        var amt = d.Depth * level;
        switch (d.Target)
        {
            case SamplerModTarget.AmplitudeDb: ampDb += amt; break;
            case SamplerModTarget.Pan: panAdd += amt; break;
            case SamplerModTarget.PitchCents: pitchCents += amt; break;
            case SamplerModTarget.CutoffCents: cutoffCents += amt; break;
        }
    }

    private void AdvanceFlexEgs(SamplerRegion rt, int samples)
    {
        for (var i = 0; i < rt.FlexEgs.Count && i < _flexEgLevels.Length; i++)
        {
            var eg = rt.FlexEgs[i];
            var left = samples;
            while (left > 0 && _flexEgStage[i] < eg.Times.Length)
            {
                var stage = _flexEgStage[i];
                if (_flexEgTimesLeft[i] <= 0)
                {
                    _flexEgStage[i]++;
                    if (_flexEgStage[i] >= eg.Times.Length)
                    {
                        if (eg.SustainPoint >= 0 && eg.SustainPoint < eg.Levels.Length)
                            _flexEgLevels[i] = eg.Levels[eg.SustainPoint];
                        break;
                    }
                    _flexEgTimesLeft[i] = eg.Times[_flexEgStage[i]] * _sampleRate;
                    continue;
                }
                var step = Math.Min(left, (int)_flexEgTimesLeft[i]);
                _flexEgTimesLeft[i] -= step;
                left -= step;
                var target = stage < eg.Levels.Length ? eg.Levels[stage] : _flexEgLevels[i];
                _flexEgLevels[i] = target;
            }
        }
    }

    // Renders `count` frames from `startFrame`, advancing the read position; returns false when the
    // voice has finished (sample end or envelope done).
    private bool RenderRange(Span<float> buffer, int startFrame, int count, int channels,
        double rate, float ampMul, bool useFilter, bool useEq)
    {
        for (var frame = startFrame; frame < startFrame + count; frame++)
        {
            var f0 = (long)_position;
            if (!_reverse && f0 >= _end) return false;
            if (_reverse && _position < _offset) return false;

            var env = _env.Process();
            if (!_env.IsActive) return false;

            var frac = (float)(_position - f0);
            var baseIndex = frame * channels;
            var amp = env * _gain * ampMul;

            for (var c = 0; c < channels; c++)
            {
                var fc = c < _sampleChannels ? c : _sampleChannels - 1;
                float s = HermiteInterpolator.Sample(
                    ReadTap(f0 - 1, fc), ReadTap(f0, fc),
                    ReadTap(f0 + 1, fc), ReadTap(f0 + 2, fc), frac);

                double v = s * amp;
                if (useFilter) v = _filter[c].Process(_filterCoeffs, v);
                if (_useFilter2) v = _filter2[c].Process(_filter2Coeffs, v);
                if (useEq)
                {
                    for (var b = 0; b < _eqBandCount; b++) v = _eq[b * channels + c].Process(_eqCoeffs[b], v);
                }

                var g = channels >= 2 ? (c == 0 ? _panL : _panR) : 1f;
                buffer[baseIndex + c] += (float)v * g;
            }

            if (_reverse) _position -= rate;
            else _position += rate;

            if (_looping && !_reverse && _position >= _loopEnd)
            {
                var span = _loopEnd - _loopStart;
                if (span > 0)
                {
                    while (_position >= _loopEnd)
                    {
                        _position -= span;
                        if (_loopPassesLeft != int.MaxValue)
                        {
                            _loopPassesLeft--;
                            if (_loopPassesLeft <= 0) { _looping = false; break; }
                        }
                    }
                }
            }
            else if (_looping && _reverse && _rt?.LoopType == SamplerLoopType.Backward && _position <= _loopStart)
            {
                var span = _loopEnd - _loopStart;
                if (span > 0) while (_position <= _loopStart) _position += span;
            }
        }

        return true;
    }

    // Reads one sample frame/channel from the resident buffer or the disk stream.
    private float ReadTap(long frame, int channel)
        => _streamed ? Stream.Read(frame, channel) : _resident!.Sample(frame, channel);

    /// <summary>Immediately silences the voice and releases its stream (e.g. on patch reload).</summary>
    public void Stop()
    {
        IsActive = false;
        if (_streamed) Stream.Release();
    }

    // Converts an EQ bandwidth in octaves to a biquad Q.
    private static double BandwidthToQ(double bwOctaves)
    {
        if (bwOctaves <= 0) bwOctaves = 1.0;
        var p = Math.Pow(2.0, bwOctaves);
        return Math.Sqrt(p) / (p - 1.0);
    }
}
