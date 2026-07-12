using System;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Captures the audio input device when any track requests software monitoring and mixes the
/// latest block into the master output. Recording takes exclusive access to the input device.
/// </summary>
public sealed class InputMonitorService : IInputMonitorService, IDisposable
{
    private readonly IProjectService _project;
    private readonly IAudioInput _audioInput;
    private readonly IEventAggregator _events;

    private readonly object _lock = new();
    private float[] _ring = Array.Empty<float>();
    private int _ringChannels = 1;
    private int _ringFrames;
    private bool _exclusive;
    private bool _capturing;

    public InputMonitorService(IProjectService project, IAudioInput audioInput, IEventAggregator events)
    {
        _project = project;
        _audioInput = audioInput;
        _events = events;
        _project.ProjectChanged += Refresh;
        _events.Subscribe<TrackChangedEvent>(_ => Refresh());
        Refresh();
    }

    public bool IsActive { get; private set; }

    public void Refresh()
    {
        IsActive = _project.Current.Tracks.Any(NeedsMonitor);
        if (_exclusive)
            return;

        if (IsActive && !_capturing)
            TryStartCapture();
        else if (!IsActive && _capturing && !_audioInput.IsCapturing)
            _capturing = false;
        else if (!IsActive && _capturing)
        {
            try { _audioInput.Stop(); } catch { /* device may already be closed */ }
            _capturing = false;
        }
    }

    public void SetRecordingExclusive(bool exclusive)
    {
        _exclusive = exclusive;
        if (exclusive && _capturing)
        {
            try { _audioInput.Stop(); } catch { /* ignore */ }
            _capturing = false;
        }
        else if (!exclusive)
            Refresh();
    }

    public void PushCapture(ReadOnlySpan<float> input, int channels)
    {
        if (!IsActive) return;
        StoreBlock(input, channels);
    }

    public void Mix(Span<float> buffer, int channels, int frames)
    {
        if (!IsActive) return;

        float[] snapshot;
        int snapChannels;
        int snapFrames;
        lock (_lock)
        {
            if (_ringFrames <= 0) return;
            snapshot = _ring;
            snapChannels = _ringChannels;
            snapFrames = _ringFrames;
        }

        var monitorGain = 0.85f;
        foreach (var track in _project.Current.Tracks)
        {
            if (!NeedsMonitor(track)) continue;
            monitorGain = Math.Max(monitorGain, (float)Math.Clamp(track.Volume, 0, 1));
        }

        var count = Math.Min(frames, snapFrames);
        var outCh = Math.Max(1, channels);
        var inCh = Math.Max(1, snapChannels);
        for (var f = 0; f < count; f++)
        {
            var src = f * inCh;
            var dst = f * outCh;
            var l = snapshot[src] * monitorGain;
            var r = inCh >= 2 ? snapshot[src + 1] * monitorGain : l;
            buffer[dst] += l;
            if (outCh >= 2) buffer[dst + 1] += r;
        }
    }

    private void TryStartCapture()
    {
        try
        {
            _audioInput.Start(OnCapture);
            _capturing = true;
        }
        catch
        {
            _capturing = false;
        }
    }

    private void OnCapture(ReadOnlySpan<float> input, int channels)
    {
        StoreBlock(input, channels);
    }

    private void StoreBlock(ReadOnlySpan<float> input, int channels)
    {
        var ch = Math.Max(1, channels);
        var frames = input.Length / ch;
        if (frames <= 0) return;

        lock (_lock)
        {
            var needed = frames * ch;
            if (_ring.Length < needed) _ring = new float[needed];
            input.CopyTo(_ring);
            _ringChannels = ch;
            _ringFrames = frames;
        }
    }

    private static bool NeedsMonitor(Track track)
    {
        if (track.Kind != TrackKind.Audio || track.IsMuted) return false;
        return track.InputMonitoring switch
        {
            InputMonitoringMode.On => true,
            InputMonitoringMode.Auto => track.IsArmed,
            _ => false
        };
    }

    public void Dispose()
    {
        if (_capturing)
        {
            try { _audioInput.Stop(); } catch { /* ignore */ }
        }
    }
}
