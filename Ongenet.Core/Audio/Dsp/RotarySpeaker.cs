using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A Leslie rotary-speaker emulation. A crossover splits the signal into a bass rotor (low band) and a
/// treble horn (high band); each is spun by its own LFO that produces (a) a Doppler pitch wobble via a
/// modulated <see cref="DelayLine"/>, and (b) an amplitude tremolo as the driver sweeps toward and away
/// from the listener. The horn and drum spin at different rates, and the left/right mics see opposite
/// phases, giving the swirling stereo image. An optional <see cref="Drive"/> adds tube-style grit before
/// the rotors. Allocation- and trig-light in <see cref="Process"/> (the LFOs use table-free sine).
/// Reusable by any instrument/effect.
/// </summary>
public sealed class RotarySpeaker
{
    private const double HornBaseHz = 6.6;   // fast (chorale→tremolo) horn spin
    private const double DrumBaseHz = 5.5;    // bass rotor runs a touch slower
    private const double HornDepthMs = 0.7;   // Doppler swing for the horn
    private const double DrumDepthMs = 2.2;   // bass rotor swings wider/slower
    private const double MaxDelayMs = 6.0;

    private readonly DelayLine _hornL = new();
    private readonly DelayLine _hornR = new();
    private readonly DelayLine _drumL = new();
    private readonly DelayLine _drumR = new();
    private readonly OnePole _splitL = new();
    private readonly OnePole _splitR = new();
    private readonly Lfo _horn = new();
    private readonly Lfo _drum = new();

    private int _sampleRate = 44100;
    private double _hornCenter = 44;   // centre delay in samples
    private double _drumCenter = 44;
    private double _hornDepth;          // delay swing in samples
    private double _drumDepth;
    private float _mix = 1f;
    private float _drive = 1f;

    /// <summary>Wet/dry blend (0 = dry, 1 = full Leslie).</summary>
    public float Mix { get => _mix; set => _mix = AudioMath.Clamp(value, 0f, 1f); }

    public void Configure(int sampleRate, double crossoverHz = 800.0)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 44100;

        var size = (int)(MaxDelayMs / 1000.0 * _sampleRate) + 8;
        if (_hornL.Size < size)
        {
            _hornL.Resize(size); _hornR.Resize(size);
            _drumL.Resize(size); _drumR.Resize(size);
        }

        var xover = AudioMath.Clamp(crossoverHz, 200.0, 3000.0);
        _splitL.SetLowpass(xover, _sampleRate);
        _splitR.SetLowpass(xover, _sampleRate);

        _hornCenter = MaxDelayMs * 0.5 / 1000.0 * _sampleRate;
        _drumCenter = _hornCenter;
        _hornDepth = HornDepthMs / 1000.0 * _sampleRate;
        _drumDepth = DrumDepthMs / 1000.0 * _sampleRate;

        _horn.Wave = LfoWave.Sine;
        _drum.Wave = LfoWave.Sine;
        SetSpeed(1.0);
    }

    /// <summary>
    /// Sets rotor speed as a 0..1 blend from brake/chorale (slow) to tremolo (fast); the horn always
    /// leads the drum, as on a real Leslie.
    /// </summary>
    public void SetSpeed(double speed01)
    {
        var t = AudioMath.Clamp(speed01, 0.0, 1.0);
        // Slow ~ 0.8 Hz, fast ~ base rate.
        var horn = AudioMath.Lerp(0.8, HornBaseHz, t);
        var drum = AudioMath.Lerp(0.7, DrumBaseHz, t);
        _horn.SetRate(horn, _sampleRate);
        _drum.SetRate(drum, _sampleRate);
    }

    /// <summary>Explicit horn/drum spin rates in Hz.</summary>
    public void SetSpeedHz(double hornHz, double drumHz)
    {
        _horn.SetRate(Math.Max(0.0, hornHz), _sampleRate);
        _drum.SetRate(Math.Max(0.0, drumHz), _sampleRate);
    }

    /// <summary>Pre-rotor overdrive in dB (0 dB = clean).</summary>
    public void SetDrive(double driveDb) => _drive = (float)AudioMath.Db2Lin(AudioMath.Clamp(driveDb, 0.0, 36.0));

    public void Reset()
    {
        _hornL.Clear(); _hornR.Clear();
        _drumL.Clear(); _drumR.Clear();
        _splitL.Reset(); _splitR.Reset();
        _horn.Reset(); _drum.Reset(0.25); // start the drum a quarter-cycle out for width
    }

    public void Process(float l, float r, out float outL, out float outR)
    {
        if (_drive > 1.0001f)
        {
            l = AudioMath.SoftClip(l * _drive);
            r = AudioMath.SoftClip(r * _drive);
        }

        // Split each channel; ProcessHP() = x − LP(x).
        var lowL = (float)_splitL.ProcessLP(l);
        var lowR = (float)_splitR.ProcessLP(r);
        var highL = l - lowL;
        var highR = r - lowR;

        // Horn LFO (left/right read the swing in anti-phase for stereo swirl).
        var hp = _horn.Value(0.0);
        var dp = _drum.Value(0.0);
        _horn.Advance();
        _drum.Advance();

        var hDelL = _hornCenter + _hornDepth * hp;
        var hDelR = _hornCenter - _hornDepth * hp;
        var dDelL = _drumCenter + _drumDepth * dp;
        var dDelR = _drumCenter - _drumDepth * dp;

        _hornL.Write(highL); _hornR.Write(highR);
        _drumL.Write(lowL);  _drumR.Write(lowR);

        var hL = _hornL.ReadFrac(hDelL);
        var hR = _hornR.ReadFrac(hDelR);
        var dL = _drumL.ReadFrac(dDelL);
        var dR = _drumR.ReadFrac(dDelR);

        // Amplitude tremolo tracking the same rotation (in anti-phase per channel).
        var hAmpL = (float)(0.7 + 0.3 * hp);
        var hAmpR = (float)(0.7 - 0.3 * hp);
        var dAmpL = (float)(0.85 + 0.15 * dp);
        var dAmpR = (float)(0.85 - 0.15 * dp);

        var wetL = hL * hAmpL + dL * dAmpL;
        var wetR = hR * hAmpR + dR * dAmpR;

        outL = l + (wetL - l) * _mix;
        outR = r + (wetR - r) * _mix;
    }
}
