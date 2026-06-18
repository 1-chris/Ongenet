using System;

namespace Ongenet.Core.Services.Interfaces;

public interface IEventAggregator
{
    void Publish<TEvent>(TEvent @event);
    IDisposable Subscribe<TEvent>(Action<TEvent> action);
}
