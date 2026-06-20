using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.Services;

/// <summary>
/// Default <see cref="ITransportMapService"/>. Learns and applies transport-control mappings. Matching
/// runs on the MIDI thread; the triggered transport/record call (and learn completion) is marshalled to
/// the UI thread because it touches transport/project state and raises UI events.
/// </summary>
public sealed class TransportMapService : ITransportMapService
{
    private readonly ITransportService _transport;
    private readonly IRecordingService _recording;
    private readonly IUiThreadDispatcher? _ui;

    private readonly List<TransportMapping> _mappings = new();
    private volatile TransportMapping[] _snapshot = Array.Empty<TransportMapping>();
    private volatile bool _learning;
    private TransportAction _learnAction;

    public TransportMapService(ITransportService transport, IRecordingService recording,
        IUiThreadDispatcher? ui = null)
    {
        _transport = transport;
        _recording = recording;
        _ui = ui;
    }

    public IReadOnlyList<TransportMapping> Mappings => _mappings;

    public TransportAction? LearnAction => _learning ? _learnAction : null;

    public event Action? MappingsChanged;
    public event Action? LearnStateChanged;

    public void BeginLearn(TransportAction action)
    {
        _learnAction = action;
        _learning = true;
        LearnStateChanged?.Invoke();
    }

    public void CancelLearn()
    {
        if (!_learning) return;
        _learning = false;
        LearnStateChanged?.Invoke();
    }

    public void ClearMapping(TransportAction action)
    {
        _mappings.RemoveAll(m => m.Action == action);
        Publish();
    }

    public TransportMapping? MappingFor(TransportAction action)
        => _mappings.FirstOrDefault(m => m.Action == action);

    public bool HandleMessage(MidiMessage message)
    {
        // Only a Note On or a CC "button press" (value >= 64) is a trigger.
        bool isNote;
        int number;
        switch (message.Kind)
        {
            case MidiMessageKind.NoteOn:
                isNote = true;
                number = message.Note;
                break;
            case MidiMessageKind.ControlChange when message.Value >= 64:
                isNote = false;
                number = message.Controller;
                break;
            default:
                return false;
        }

        if (_learning)
        {
            _learning = false;
            var action = _learnAction;
            var channel = message.Channel;
            Post(() =>
            {
                Bind(action, isNote, channel, number);
                LearnStateChanged?.Invoke();
            });
            return true;
        }

        foreach (var m in _snapshot)
        {
            if (m.IsNote != isNote || m.Number != number) continue;
            if (m.Channel >= 0 && m.Channel != message.Channel) continue;
            Post(() => Invoke(m.Action));
            return true;
        }

        return false;
    }

    public void SetMappings(IEnumerable<TransportMapping> mappings)
    {
        _mappings.Clear();
        _mappings.AddRange(mappings);
        Publish();
    }

    // UI thread.
    private void Bind(TransportAction action, bool isNote, int channel, int number)
    {
        _mappings.RemoveAll(m => m.Action == action);
        _mappings.Add(new TransportMapping { Action = action, IsNote = isNote, Channel = -1, Number = number });
        Publish();
    }

    private void Invoke(TransportAction action)
    {
        switch (action)
        {
            case TransportAction.PlayPause:
                if (_transport.State == TransportState.Playing) _transport.Stop();
                else _transport.Play();
                break;
            case TransportAction.Stop:
                _transport.Stop();
                break;
            case TransportAction.Record:
                if (_recording.IsRecording) _recording.StopRecording();
                else _recording.StartRecording();
                break;
        }
    }

    private void Publish()
    {
        _snapshot = _mappings.ToArray();
        MappingsChanged?.Invoke();
    }

    private void Post(Action action)
    {
        if (_ui is null) action();
        else _ui.Post(action);
    }
}
