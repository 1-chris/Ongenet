using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

public class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, List<object>> _subscriptions = new();

    public void Publish<TEvent>(TEvent @event)
    {
        var type = typeof(TEvent);
        if (_subscriptions.TryGetValue(type, out var handlers))
        {
            lock (handlers)
            {
                foreach (var handler in handlers.Cast<Action<TEvent>>())
                {
                    handler(@event);
                }
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> action)
    {
        var type = typeof(TEvent);
        var handlers = _subscriptions.GetOrAdd(type, _ => new List<object>());
        
        lock (handlers)
        {
            handlers.Add(action);
        }

        return new Subscription(handlers, action);
    }

    private class Subscription : IDisposable
    {
        private readonly List<object> _handlers;
        private readonly object _handler;

        public Subscription(List<object> handlers, object handler)
        {
            _handlers = handlers;
            _handler = handler;
        }

        public void Dispose()
        {
            lock (_handlers)
            {
                _handlers.Remove(_handler);
            }
        }
    }
}
