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

        var semis = (triggerNote - rt.PitchKeycenter) * rt.KeytrackSemisPerKey
                    + rt.TransposeSemis + rt.TuneCents / 100.0 + extraSemis;
        var sampleRate = _sample.SampleRate <= 0 ? format.SampleRate : _sample.SampleRate;
        _rate = Math.Pow(2.0, semis / 12.0) * sampleRate / format.SampleRate;

        _offset = rt.Offset;
        _end = rt.End;
        _loopStart = rt.LoopStart;
        _loopEnd = rt.LoopEnd;
        _loopMode = rt.LoopMode;
        _reverse = rt.Reverse; // streamed samples are always forward (reverse forces resident)
        _position = _reverse ? Math.Max(_offset, _end - 1) : _offset;

        if (_streamed) Stream.Request(_sample, (long)_position);

        var norm = velocity / 127.0;
        var vt = rt.AmpVeltrack / 100.0;
        var velGain = (1.0 - vt) + vt * norm * norm;
        _gain = (float)(rt.Gain * velGain);

        AudioMath.PanGains(rt.Pan, out _panL, out _panR);

        _env.SetSampleRate(_sampleRate);
        rt.AmpEg.ApplyTo(_env);
        _env.Gate();

        SetupModulation(rt, triggerNote, velocity);

        IsActive = _gain > 0f;
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

        if (rt.HasFilEg) { _filEg.SetSampleRate(_sampleRate); rt.FilEg.ApplyTo(_filEg); _filEg.Gate(); }
        if (rt.HasPitchEg) { _pitchEg.SetSampleRate(_sampleRate); rt.PitchEg.ApplyTo(_pitchEg); _pitchEg.Gate(); }
        if (rt.HasFilLfo) { _filLfo.SetRate(rt.FilLfoFreq, _sampleRate); _filLfo.Reset(); _filLfoDelay = (long)(rt.FilLfoDelay * _sampleRate); }
        if (rt.HasAmpLfo) { _ampLfo.SetRate(rt.AmpLfoFreq, _sampleRate); _ampLfo.Reset(); _ampLfoDelay = (long)(rt.AmpLfoDelay * _sampleRate); }
        if (rt.HasPitchLfo) { _pitchLfo.SetRate(rt.PitchLfoFreq, _sampleRate); _pitchLfo.Reset(); _pitchLfoDelay = (long)(rt.PitchLfoDelay * _sampleRate); }
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
        _looping = _loopMode is SamplerLoopMode.LoopContinuous || (_loopMode == SamplerLoopMode.LoopSustain && !_released);

        // Pitch bend is read per buffer so held notes bend in real time (applies on both paths).
        var bendMul = 1.0;
        if (_mod is { } mod && mod.Bend != 0.0)
        {
            var bendCents = mod.Bend >= 0 ? mod.Bend * rt.BendUpCents : mod.Bend * rt.BendDownCents;
            bendMul = Math.Pow(2.0, bendCents / 1200.0);
        }
        var baseRate = _rate * bendMul;

        bool active;
        if (!rt.ModActive)
        {
            // Fast path: no filtering / EQ / LFO / pitch-EG / CC — just resample + amp envelope.
            active = RenderRange(buffer, 0, frames, channels, baseRate, 1f, useFilter: false, useEq: false);
        }
        else
        {
            active = true;
            var frame = 0;
            while (frame < frames)
            {
                var n = Math.Min(ControlBlock, frames - frame);

                // --- Control-rate modulation update ---
                var pitchCents = 0.0;
                if (rt.HasPitchEg) pitchCents += rt.PitchEgDepth * _pitchEg.Level;
                if (rt.HasPitchLfo && _age >= _pitchLfoDelay) pitchCents += rt.PitchLfoDepth * _pitchLfo.Value(0);
                var rate = pitchCents != 0.0 ? baseRate * Math.Pow(2.0, pitchCents / 1200.0) : baseRate;

                var ampMul = 1f;
                if (rt.HasAmpLfo && _age >= _ampLfoDelay)
                    ampMul = (float)AudioMath.Db2Lin(rt.AmpLfoDepthDb * _ampLfo.Value(0));

                if (_useFilter)
                {
                    var cents = 0.0;
                    if (rt.HasFilEg) cents += rt.FilEgDepth * _filEg.Level;
                    if (rt.HasFilLfo && _age >= _filLfoDelay) cents += rt.FilLfoDepth * _filLfo.Value(0);
                    if (_mod is { } m && rt.CutoffCc.Count > 0)
                    {
                        foreach (var cc in rt.CutoffCc) cents += cc.Depth * m.Cc[cc.Cc] / 127.0;
                    }
                    var hz = cents != 0.0
                        ? Math.Clamp(_filterBaseHz * Math.Pow(2.0, cents / 1200.0), 20.0, _nyquist)
                        : _filterBaseHz;
                    _filterCoeffs = BiquadCoefficients.Compute(_filterMode, hz, _filterQ, _sampleRate);
                }

                active = RenderRange(buffer, frame, n, channels, rate, ampMul, _useFilter, _eqBandCount > 0);

                // Advance modulators across the block.
                if (rt.HasFilEg) for (var k = 0; k < n; k++) _filEg.Process();
                if (rt.HasPitchEg) for (var k = 0; k < n; k++) _pitchEg.Process();
                if (rt.HasFilLfo) for (var k = 0; k < n; k++) _filLfo.Advance();
                if (rt.HasAmpLfo) for (var k = 0; k < n; k++) _ampLfo.Advance();
                if (rt.HasPitchLfo) for (var k = 0; k < n; k++) _pitchLfo.Advance();
                _age += n;
                frame += n;
                if (!active) break;
            }
        }

        if (!active)
        {
            IsActive = false;
            if (_streamed) Stream.Release(); // hand the file back to the streaming engine to close
            return;
        }

        // Let the streaming producer free everything behind the read position (keep a couple for Hermite).
        if (_streamed) Stream.SetConsumed((long)_position - 2);
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
                if (span > 0) { while (_position >= _loopEnd) _position -= span; }
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
