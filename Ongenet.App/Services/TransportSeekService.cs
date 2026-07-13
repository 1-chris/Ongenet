using System;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Services;

public sealed class TransportSeekService : ITransportSeekService
{
    private readonly ITransportService _transport;
    private readonly IProjectService _project;

    public TransportSeekService(ITransportService transport, IProjectService project)
    {
        _transport = transport;
        _project = project;
    }

    public void SeekToBeat(double beat, bool snapToBar = false)
    {
        if (snapToBar)
        {
            var bar = Math.Max(1, _project.Current.TimeSignature.Numerator);
            beat = Math.Max(0, Math.Round(beat / bar) * bar);
        }
        else
        {
            beat = Math.Max(0, beat);
        }

        _transport.StartBeat = beat;
        _transport.NotifyPlayhead(beat);
    }
}
