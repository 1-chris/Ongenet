using System;
using System.Threading;
using Ongenet.App.Services;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.Services;

/// <summary>Sends MIDI transport and 24 PPQN clock messages while the transport is playing.</summary>
public sealed class MidiClockOutputService : IDisposable
{
    private readonly IMidiOutputService _output;
    private readonly ITransportService _transport;
    private readonly IAppSettingsService _settings;
    private readonly Timer _timer;

    public MidiClockOutputService(IMidiOutputService output, ITransportService transport, IAppSettingsService settings)
    {
        _output = output;
        _transport = transport;
        _settings = settings;
        _timer = new Timer(OnTick);
        _transport.StateChanged += OnStateChanged;
        _transport.TempoChanged += OnTempoChanged;
        OnStateChanged(_transport.State);
    }

    private void OnStateChanged(TransportState state)
    {
        if (state == TransportState.Playing && _settings.Current.MidiClockEnabled)
        {
            _output.SendRaw(0xFA);
            Schedule();
        }
        else
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            if (_settings.Current.MidiClockEnabled) _output.SendRaw(0xFC);
        }
    }

    private void OnTempoChanged(Tempo _) => Schedule();

    private void Schedule()
    {
        if (_transport.State != TransportState.Playing || !_settings.Current.MidiClockEnabled) return;
        var intervalMs = Math.Max(1, (int)Math.Round(60_000.0 / (_transport.Tempo.BeatsPerMinute * 24.0)));
        _timer.Change(0, intervalMs);
    }

    private void OnTick(object? _)
    {
        if (_transport.State == TransportState.Playing && _settings.Current.MidiClockEnabled)
            _output.SendRaw(0xF8);
    }

    public void Dispose()
    {
        _transport.StateChanged -= OnStateChanged;
        _transport.TempoChanged -= OnTempoChanged;
        _timer.Dispose();
    }
}
