using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>
/// Piecewise-linear beat ↔ source-seconds mapping built from a clip's warp markers.
/// Beat positions are clip-local (0 .. <see cref="Clip.LengthBeats"/>).
/// </summary>
public sealed class WarpMap
{
    private readonly (double Beat, double Source)[] _markers;

    public double LengthBeats { get; }
    public double SourceStart { get; }
    public double SourceEnd { get; }

    private WarpMap((double Beat, double Source)[] markers, double lengthBeats, double sourceStart, double sourceEnd)
    {
        _markers = markers;
        LengthBeats = lengthBeats;
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
    }

    /// <summary>True when the clip has explicit warp markers (beyond implicit endpoints).</summary>
    public bool HasExplicitMarkers => _markers.Length > 2;

    public static WarpMap FromClip(Clip clip, double sourceEndSeconds)
    {
        var lengthBeats = clip.LengthBeats;
        var sourceStart = clip.SourceOffsetSeconds;
        var list = new List<(double Beat, double Source)>
        {
            (0.0, sourceStart),
            (lengthBeats, sourceEndSeconds)
        };

        foreach (var wm in clip.WarpMarkers)
        {
            if (wm.BeatPosition < 0 || wm.BeatPosition > lengthBeats) continue;
            list.Add((wm.BeatPosition, wm.SourceSeconds));
        }

        list.Sort((a, b) => a.Beat.CompareTo(b.Beat));

        // Collapse duplicate beat positions (keep last source).
        var deduped = new List<(double Beat, double Source)>();
        foreach (var m in list)
        {
            if (deduped.Count > 0 && Math.Abs(deduped[^1].Beat - m.Beat) < 1e-9)
                deduped[^1] = m;
            else
                deduped.Add(m);
        }

        return new WarpMap(deduped.ToArray(), lengthBeats, sourceStart, sourceEndSeconds);
    }

    /// <summary>Clip-local beat → source seconds.</summary>
    public double BeatToSource(double beat)
    {
        if (_markers.Length == 0) return SourceStart;
        if (beat <= _markers[0].Beat) return _markers[0].Source;
        for (var i = 1; i < _markers.Length; i++)
        {
            var (b1, s1) = _markers[i];
            var (b0, s0) = _markers[i - 1];
            if (beat <= b1)
            {
                var db = b1 - b0;
                if (db <= 1e-12) return s1;
                var t = (beat - b0) / db;
                return s0 + t * (s1 - s0);
            }
        }

        return _markers[^1].Source;
    }

    /// <summary>Source seconds → clip-local beat.</summary>
    public double SourceToBeat(double sourceSeconds)
    {
        if (_markers.Length == 0) return 0;
        if (sourceSeconds <= _markers[0].Source) return _markers[0].Beat;
        for (var i = 1; i < _markers.Length; i++)
        {
            var (b1, s1) = _markers[i];
            var (b0, s0) = _markers[i - 1];
            if (sourceSeconds <= s1)
            {
                var ds = s1 - s0;
                if (ds <= 1e-12) return b1;
                var t = (sourceSeconds - s0) / ds;
                return b0 + t * (b1 - b0);
            }
        }

        return _markers[^1].Beat;
    }

    /// <summary>Local playback speed: source seconds advanced per beat at <paramref name="beat"/>.</summary>
    public double SegmentRatio(double beat)
    {
        if (_markers.Length < 2) return 1.0;
        for (var i = 1; i < _markers.Length; i++)
        {
            var (b1, s1) = _markers[i];
            var (b0, s0) = _markers[i - 1];
            if (beat <= b1 || i == _markers.Length - 1)
            {
                var db = b1 - b0;
                if (db <= 1e-12) return 1.0;
                return (s1 - s0) / db;
            }
        }

        return 1.0;
    }

    public int SegmentIndexAt(double beat)
    {
        for (var i = 1; i < _markers.Length; i++)
            if (beat <= _markers[i].Beat) return i - 1;
        return Math.Max(0, _markers.Length - 2);
    }

    public (double Beat0, double Beat1, double Source0, double Source1) Segment(int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, _markers.Length - 2));
        var (b0, s0) = _markers[index];
        var (b1, s1) = _markers[index + 1];
        return (b0, b1, s0, s1);
    }
}
