using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>Destination of a continuous modulation route.</summary>
public enum SamplerModTarget
{
    AmplitudeDb,
    Pan,
    PitchCents,
    CutoffCents,
    ResonanceDb,
    FilterQ,
    AmpLfoDepthDb,
    PitchLfoDepthCents,
    FilLfoDepthCents,
    OffsetFrames,
    DelaySeconds,
}

/// <summary>Source of a modulation value (normalized 0..1 unless noted).</summary>
public enum SamplerModSource
{
    Cc,
    ChannelAftertouch,
    PolyAftertouch,
    PitchBend,     // 0..1 mapped from [-1,1] for absolute-style; engine uses Bend separately for pitch
    Velocity,      // note-on velocity (snapshot)
    Key,           // MIDI key / 127
}

/// <summary>How a crossfade interpolates.</summary>
public enum SamplerXfadeCurve
{
    Gain,
    Power,
}

/// <summary>Exclusive-group note-off behaviour (<c>off_mode</c>).</summary>
public enum SamplerOffMode
{
    Fast,
    Normal,
}

/// <summary>A continuous CC/aftertouch/etc. → parameter mapping.</summary>
public readonly record struct SamplerModRoute(
    SamplerModTarget Target,
    SamplerModSource Source,
    int SourceIndex,       // CC number, or unused
    double Depth,          // units of Target at source=1
    int CurveId = -1,      // <curve> index, -1 = linear
    double SmoothSeconds = 0,
    double Step = 0);

/// <summary>CC range gate: region plays only when CC is within [Lo, Hi].</summary>
public readonly record struct SamplerCcGate(int Cc, int Lo, int Hi);

/// <summary>Key/vel/CC crossfade ramps applied to region amplitude.</summary>
public sealed class SamplerXfade
{
    public int XfinLoKey { get; init; } = -1;
    public int XfinHiKey { get; init; } = -1;
    public int XfoutLoKey { get; init; } = -1;
    public int XfoutHiKey { get; init; } = -1;
    public int XfinLoVel { get; init; } = -1;
    public int XfinHiVel { get; init; } = -1;
    public int XfoutLoVel { get; init; } = -1;
    public int XfoutHiVel { get; init; } = -1;
    public int XfinLoCc { get; init; } = -1;
    public int XfinHiCc { get; init; } = -1;
    public int XfoutLoCc { get; init; } = -1;
    public int XfoutHiCc { get; init; } = -1;
    public int XfadeCc { get; init; } = -1; // which CC for cc xfades
    public SamplerXfadeCurve KeyCurve { get; init; }
    public SamplerXfadeCurve VelCurve { get; init; }
    public SamplerXfadeCurve CcCurve { get; init; }

    public bool IsActive =>
        XfinLoKey >= 0 || XfoutLoKey >= 0 || XfinLoVel >= 0 || XfoutLoVel >= 0
        || (XfadeCc >= 0 && (XfinLoCc >= 0 || XfoutLoCc >= 0));

    public float Evaluate(int key, int vel, int ccValue)
    {
        var g = 1.0;
        if (XfinLoKey >= 0 && XfinHiKey >= XfinLoKey)
            g *= RampIn(key, XfinLoKey, XfinHiKey, KeyCurve);
        if (XfoutLoKey >= 0 && XfoutHiKey >= XfoutLoKey)
            g *= RampOut(key, XfoutLoKey, XfoutHiKey, KeyCurve);
        if (XfinLoVel >= 0 && XfinHiVel >= XfinLoVel)
            g *= RampIn(vel, XfinLoVel, XfinHiVel, VelCurve);
        if (XfoutLoVel >= 0 && XfoutHiVel >= XfoutLoVel)
            g *= RampOut(vel, XfoutLoVel, XfoutHiVel, VelCurve);
        if (XfadeCc >= 0)
        {
            if (XfinLoCc >= 0 && XfinHiCc >= XfinLoCc)
                g *= RampIn(ccValue, XfinLoCc, XfinHiCc, CcCurve);
            if (XfoutLoCc >= 0 && XfoutHiCc >= XfoutLoCc)
                g *= RampOut(ccValue, XfoutLoCc, XfoutHiCc, CcCurve);
        }
        return (float)Math.Clamp(g, 0.0, 1.0);
    }

    private static double RampIn(int x, int lo, int hi, SamplerXfadeCurve curve)
    {
        if (x <= lo) return 0;
        if (x >= hi) return 1;
        var t = (x - lo) / (double)(hi - lo);
        return curve == SamplerXfadeCurve.Power ? Math.Sqrt(t) : t;
    }

    private static double RampOut(int x, int lo, int hi, SamplerXfadeCurve curve)
    {
        if (x <= lo) return 1;
        if (x >= hi) return 0;
        var t = (x - lo) / (double)(hi - lo);
        var g = 1.0 - t;
        return curve == SamplerXfadeCurve.Power ? Math.Sqrt(g) : g;
    }
}

/// <summary>Named curve table from SFZ <c>&lt;curve&gt;</c> (values indexed 0..127).</summary>
public sealed class SamplerCurve
{
    public int Id { get; init; }
    public float[] Values { get; init; } = CreateLinear();

    public float Evaluate(double normalized01)
    {
        var x = Math.Clamp(normalized01, 0.0, 1.0) * 127.0;
        var i0 = (int)x;
        var i1 = Math.Min(127, i0 + 1);
        var frac = (float)(x - i0);
        return Values[i0] * (1f - frac) + Values[i1] * frac;
    }

    public static float[] CreateLinear()
    {
        var v = new float[128];
        for (var i = 0; i < 128; i++) v[i] = i / 127f;
        return v;
    }
}

/// <summary>Lookup of instrument curves by id.</summary>
public sealed class SamplerCurveBank
{
    public static SamplerCurveBank Empty { get; } = new();

    private readonly Dictionary<int, SamplerCurve> _curves = new();

    public void Set(SamplerCurve curve) => _curves[curve.Id] = curve;

    public SamplerCurve? Get(int id) => _curves.TryGetValue(id, out var c) ? c : null;

    public float Map(int curveId, double normalized01)
    {
        if (curveId < 0) return (float)normalized01;
        var c = Get(curveId);
        return c?.Evaluate(normalized01) ?? (float)normalized01;
    }
}

/// <summary>Shared helpers for applying <see cref="SamplerModRoute"/> lists.</summary>
public static class SamplerModMath
{
    public static double SourceValue(SamplerModSource source, int sourceIndex, SamplerModState mod, int velocity = 64, int key = 60)
    {
        return source switch
        {
            SamplerModSource.Cc => sourceIndex is >= 0 and <= 127 ? mod.Cc[sourceIndex] / 127.0 : 0,
            SamplerModSource.ChannelAftertouch => mod.ChannelAftertouch / 127.0,
            SamplerModSource.PolyAftertouch => mod.PolyAftertouch(key) / 127.0,
            SamplerModSource.PitchBend => (mod.Bend + 1.0) * 0.5,
            SamplerModSource.Velocity => velocity / 127.0,
            SamplerModSource.Key => key / 127.0,
            _ => 0
        };
    }

    public static double RouteAmount(in SamplerModRoute route, SamplerModState mod, SamplerCurveBank? curves,
        int velocity = 64, int key = 60)
    {
        var n = SourceValue(route.Source, route.SourceIndex, mod, velocity, key);
        if (route.Step > 0)
            n = Math.Floor(n / route.Step + 1e-9) * route.Step;
        if (route.CurveId >= 0 && curves is not null)
            n = curves.Map(route.CurveId, n);
        return n * route.Depth;
    }
}
