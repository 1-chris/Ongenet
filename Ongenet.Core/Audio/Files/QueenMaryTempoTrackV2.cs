using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Beat period and beat position tracker ported from qm-dsp <c>TempoTrackV2</c>
/// (Mixxx default beat analyzer).
/// </summary>
internal sealed class QueenMaryTempoTrackV2
{
    private readonly float _sampleRate;
    private readonly int _increment;

    public QueenMaryTempoTrackV2(float sampleRate, int dfIncrement)
    {
        _sampleRate = sampleRate;
        _increment = dfIncrement;
    }

    public void CalculateBeatPeriod(IReadOnlyList<double> df, IList<int> beatPeriod,
        double inputTempo = 120.0, bool constrainTempo = false)
    {
        beatPeriod.Clear();
        if (df.Count == 0) return;

        const int wvLen = 128;
        var rayParam = (60.0 * 44100.0 / 512.0) / inputTempo;
        var wv = new double[wvLen];
        if (constrainTempo)
        {
            for (var i = 0; i < wvLen; i++)
                wv[i] = Math.Exp(-Math.Pow(i - rayParam, 2.0) / (2.0 * Math.Pow(rayParam / 4.0, 2.0)));
        }
        else
        {
            for (var i = 0; i < wvLen; i++)
                wv[i] = i / Math.Pow(rayParam, 2.0) * Math.Exp(-Math.Pow(i, 2.0) / (2.0 * Math.Pow(rayParam, 2.0)));
        }

        const int winLen = 512;
        const int hopSize = 128;
        var dfLen = df.Count;
        var rcfMat = new List<double[]>();
        var dfframe = new double[winLen];
        var rcf = new double[wvLen];

        for (var i = -winLen / 2; i < dfLen - winLen / 2; i += hopSize)
        {
            var k = 0;
            var l = winLen;
            Array.Clear(dfframe);
            if (i < 0)
            {
                k = -i;
                for (var z = 0; z < k; z++) dfframe[z] = 0.0;
            }

            if (i + l > dfLen)
            {
                l = dfLen - i;
                for (var z = l; z < winLen; z++) dfframe[z] = 0.0;
            }

            for (var z = 0; z < l - k; z++)
                dfframe[k + z] = df[i + k + z];

            GetRcf(dfframe, wv, rcf);
            rcfMat.Add((double[])rcf.Clone());
        }

        ViterbiDecode(rcfMat, wv, beatPeriod);
    }

    public void CalculateBeats(IReadOnlyList<double> df, IReadOnlyList<int> beatPeriod, IList<double> beats,
        double alpha = 0.9, double tightness = 4.0)
    {
        beats.Clear();
        if (df.Count == 0 || beatPeriod.Count == 0) return;

        var dfLen = df.Count;
        var cumscore = new double[dfLen];
        var backlink = new int[dfLen];
        var localscore = new double[dfLen];
        for (var i = 0; i < dfLen; i++)
        {
            localscore[i] = df[i];
            backlink[i] = -1;
        }

        var oldPeriod = 0;
        var txwtLen = 0;
        var txwt = new List<double>();

        for (var i = 0; i < dfLen; i++)
        {
            var periodIndex = Math.Min(i / 128, beatPeriod.Count - 1);
            var period = beatPeriod[periodIndex];
            var prangeMin = period * -2;
            if (period != oldPeriod)
            {
                oldPeriod = period;
                var prangeMax = period / -2;
                txwtLen = prangeMax - prangeMin + 1;
                txwt.Clear();
                var mu = (double)period;
                for (var j = 0; j < txwtLen; j++)
                    txwt.Add(Math.Exp(-0.5 * Math.Pow(tightness * Math.Log((Math.Round(2.0 * mu) - j) / mu), 2.0)));
            }

            var vv = 0.0;
            var xx = 0;
            for (var j = 0; j < txwtLen; j++)
            {
                var cscoreInd = i + prangeMin + j;
                if (cscoreInd < 0) continue;
                var scoreCands = txwt[j] * cumscore[cscoreInd];
                if (scoreCands > vv)
                {
                    vv = scoreCands;
                    xx = cscoreInd;
                }
            }

            cumscore[i] = alpha * vv + (1.0 - alpha) * localscore[i];
            backlink[i] = xx;
        }

        var tmpVec = new List<double>();
        var lastPeriod = beatPeriod[^1];
        var startSearch = Math.Max(0, dfLen - lastPeriod);
        for (var i = startSearch; i < dfLen; i++)
            tmpVec.Add(cumscore[i]);

        var startPoint = GetMaxIndex(tmpVec) + startSearch;
        if (startPoint >= backlink.Length)
            startPoint = backlink.Length - 1;

        var ibeats = new List<int> { startPoint };
        while (backlink[ibeats[^1]] > 0)
        {
            var b = ibeats[^1];
            if (backlink[b] == b) break;
            ibeats.Add(backlink[b]);
        }

        for (var i = ibeats.Count - 1; i >= 0; i--)
            beats.Add(ibeats[i]);
    }

    public double BeatPeriodToBpm(int beatPeriod)
    {
        if (beatPeriod <= 0 || _increment <= 0) return 0;
        var dfRate = _sampleRate / _increment;
        return 60.0 * dfRate / beatPeriod;
    }

    private static void GetRcf(double[] dfframe, double[] wv, double[] rcf)
    {
        var dfframeList = new List<double>(dfframe);
        QueenMaryMath.AdaptiveThreshold(dfframeList);
        for (var i = 0; i < dfframe.Length; i++) dfframe[i] = dfframeList[i];

        var dfframeLen = dfframe.Length;
        var rcfLen = rcf.Length;
        Array.Clear(rcf);
        var acf = new double[dfframeLen];
        for (var lag = 0; lag < dfframeLen; lag++)
        {
            var sum = 0.0;
            for (var n = 0; n < dfframeLen - lag; n++)
                sum += dfframe[n] * dfframe[n + lag];
            acf[lag] = sum / (dfframeLen - lag);
        }

        const int numElem = 4;
        for (var i = 2; i < rcfLen; i++)
        {
            for (var a = 1; a <= numElem; a++)
            {
                for (var b = 1 - a; b <= a - 1; b++)
                    rcf[i - 1] += acf[a * i + b - 1] * wv[i - 1] / (2.0 * a - 1.0);
            }
        }

        var rcfList = new List<double>(rcf);
        QueenMaryMath.AdaptiveThreshold(rcfList);
        for (var i = 0; i < rcfLen; i++) rcf[i] = rcfList[i];
        var rcfSum = 0.0;
        for (var i = 0; i < rcfLen; i++)
        {
            rcf[i] += QueenMaryMath.Epsilon;
            rcfSum += rcf[i];
        }

        for (var i = 0; i < rcfLen; i++)
            rcf[i] /= rcfSum + QueenMaryMath.Epsilon;
    }

    private static void ViterbiDecode(List<double[]> rcfMat, double[] wv, IList<int> beatPeriod)
    {
        beatPeriod.Clear();
        if (rcfMat.Count < 2) return;

        var t = rcfMat.Count;
        var q = rcfMat[0].Length;
        const double sigma = 8.0;
        var tmat = new double[q][];
        for (var i = 0; i < q; i++)
        {
            tmat[i] = new double[q];
            for (var j = 0; j < q; j++) tmat[i][j] = 0.0;
        }

        for (var i = 20; i < q - 20; i++)
        {
            for (var j = 20; j < q - 20; j++)
            {
                var mu = (double)i;
                tmat[i][j] = Math.Exp(-Math.Pow(j - mu, 2.0) / (2.0 * sigma * sigma));
            }
        }

        var delta = new double[t][];
        var psi = new int[t][];
        for (var ti = 0; ti < t; ti++)
        {
            delta[ti] = new double[q];
            psi[ti] = new int[q];
        }

        for (var j = 0; j < q; j++)
            delta[0][j] = wv[j] * rcfMat[0][j];

        var deltaSum = 0.0;
        for (var i = 0; i < q; i++) deltaSum += delta[0][i];
        for (var i = 0; i < q; i++)
            delta[0][i] /= deltaSum + QueenMaryMath.Epsilon;

        for (var ti = 1; ti < t; ti++)
        {
            var tmpVec = new double[q];
            for (var j = 0; j < q; j++)
            {
                for (var i = 0; i < q; i++)
                    tmpVec[i] = delta[ti - 1][i] * tmat[j][i];
                delta[ti][j] = GetMaxVal(tmpVec);
                psi[ti][j] = GetMaxIndex(tmpVec);
                delta[ti][j] *= rcfMat[ti][j];
            }

            deltaSum = 0.0;
            for (var i = 0; i < q; i++) deltaSum += delta[ti][i];
            for (var i = 0; i < q; i++)
                delta[ti][i] /= deltaSum + QueenMaryMath.Epsilon;
        }

        var bestPath = new int[t];
        bestPath[t - 1] = GetMaxIndex(delta[t - 1]);
        for (var ti = t - 2; ti > 0; ti--)
            bestPath[ti] = psi[ti + 1][bestPath[ti + 1]];
        bestPath[0] = psi[1][bestPath[1]];

        for (var i = 0; i < t; i++)
            beatPeriod.Add(bestPath[i]);
    }

    private static double GetMaxVal(IReadOnlyList<double> values)
    {
        var max = 0.0;
        for (var i = 0; i < values.Count; i++)
            if (values[i] > max) max = values[i];
        return max;
    }

    private static int GetMaxIndex(IReadOnlyList<double> values)
    {
        var max = double.NegativeInfinity;
        var idx = 0;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
                idx = i;
            }
        }

        return idx;
    }
}
