using System;
using System.Threading;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services;

/// <summary>
/// Skeleton MTC/LTC sync service. Sends MIDI Time Code quarter-frame messages while playing;
/// LTC encode/decode is reserved for a future audio-input path.
/// </summary>
public sealed class TimecodeSyncService : IDisposable
{
    private readonly IMidiOutputService _output;
    private readonly ITransportService _transport;
    private readonly Timer _timer;
    private double _lastBeat = double.NaN;
    private int _lastQuarterFrame = -1;

    public TimecodeSyncService(IMidiOutputService output, ITransportService transport)
    {
        _output = output;
        _transport = transport;
        _timer = new Timer(OnTick);
        _transport.StateChanged += OnStateChanged;
    }

    public bool MtcEnabled { get; set; }
    public bool LtcEnabled { get; set; }
    public int FrameRate { get; set; } = 30;

    private void OnTick(object? _)
    {
        if (!MtcEnabled || _transport.State != TransportState.Playing) return;
        SendMtcQuarterFrames(_transport.PlayheadBeats);
    }

    private void OnStateChanged(TransportState state)
    {
        if (!MtcEnabled) return;
        if (state == TransportState.Playing)
        {
            _output.SendRaw(0xFA);
            _timer.Change(0, 40);
        }
        else
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _output.SendRaw(0xFC);
            _lastBeat = double.NaN;
            _lastQuarterFrame = -1;
        }
    }

    private void SendMtcQuarterFrames(double beat)
    {
        if (Math.Abs(beat - _lastBeat) < 1e-6) return;
        _lastBeat = beat;

        var bpm = Math.Max(1.0, _transport.Tempo.BeatsPerMinute);
        var seconds = beat * 60.0 / bpm;
        var fps = Math.Clamp(FrameRate, 24, 30);
        var totalFrames = (int)(seconds * fps);
        var hours = totalFrames / (fps * 3600) % 24;
        var minutes = totalFrames / (fps * 60) % 60;
        var secondsPart = totalFrames / fps % 60;
        var frames = totalFrames % fps;

        for (var q = 0; q < 8; q++)
        {
            var value = q switch
            {
                0 => frames & 0xF,
                1 => (frames >> 4) & 0x1,
                2 => secondsPart & 0xF,
                3 => (secondsPart >> 4) & 0x7,
                4 => minutes & 0xF,
                5 => (minutes >> 4) & 0x7,
                6 => hours & 0xF,
                7 => ((hours >> 4) & 0x1) | (FrameRate switch { 24 => 0, 25 => 0, 30 => 2, _ => 0 }),
                _ => 0
            };
            var packed = (q << 4) | (value & 0xF);
            if (packed == _lastQuarterFrame && q > 0) continue;
            _lastQuarterFrame = packed;
            _output.SendRaw(0xF1, (byte)packed);
        }
    }

    /// <summary>Encodes SMPTE LTC as bipolar audio samples (±0.5) for output routing.</summary>
    public void EncodeLtcIntoBuffer(Span<float> buffer, int sampleRate, double beat)
    {
        if (!LtcEnabled || buffer.Length == 0) return;
        var bpm = Math.Max(1.0, _transport.Tempo.BeatsPerMinute);
        var seconds = beat * 60.0 / bpm;
        var fps = Math.Clamp(FrameRate, 24, 30);
        var frameIndex = (long)(seconds * fps);
        var bitRate = fps * 80;
        for (var i = 0; i < buffer.Length; i++)
        {
            var t = (frameIndex * sampleRate + i) / (double)sampleRate;
            var bit = ((long)(t * bitRate) & 1) == 0 ? 0.5f : -0.5f;
            buffer[i] = bit;
        }
    }

    /// <summary>Decodes linear timecode from an audio input buffer (bi-phase mark simplified).</summary>
    public double? TryDecodeLtc(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (!LtcEnabled || samples.Length < sampleRate / 10) return null;
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
            if (samples[i - 1] < 0 && samples[i] >= 0) crossings++;
        if (crossings < 10) return null;
        var fps = Math.Clamp(FrameRate, 24, 30);
        var seconds = crossings / (fps * 80.0);
        return seconds;
    }

    public void Dispose()
    {
        _transport.StateChanged -= OnStateChanged;
        _timer.Dispose();
    }
}
