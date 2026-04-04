#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Core.Events;

internal sealed class InMemoryBppEventBus : IBppEventBus
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : class
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        lock (_syncRoot)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var registrations))
            {
                registrations = new List<Delegate>();
                _handlers.Add(typeof(TEvent), registrations);
            }

            registrations.Add(handler);
        }

        return new Subscription(() => Unsubscribe(handler));
    }

    public void Publish<TEvent>(TEvent eventData)
        where TEvent : class
    {
        if (eventData == null)
            throw new ArgumentNullException(nameof(eventData));

        List<Delegate>? snapshot;
        lock (_syncRoot)
        {
            if (
                !_handlers.TryGetValue(typeof(TEvent), out var registrations)
                || registrations.Count == 0
            )
                return;

            snapshot = new List<Delegate>(registrations);
        }

        foreach (var registration in snapshot)
        {
            try
            {
                ((Action<TEvent>)registration).Invoke(eventData);
            }
            catch (Exception ex)
            {
                var method = registration.Method;
                var handlerName =
                    method.DeclaringType?.FullName != null
                        ? $"{method.DeclaringType.FullName}.{method.Name}"
                        : method.Name;
                global::BazaarPlusPlus.BppLog.Error(
                    "EventBus",
                    $"Handler failed for event {typeof(TEvent).FullName}: {handlerName}",
                    ex
                );
            }
        }
    }

    private void Unsubscribe<TEvent>(Action<TEvent> handler)
        where TEvent : class
    {
        lock (_syncRoot)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var registrations))
                return;

            registrations.Remove(handler);
            if (registrations.Count == 0)
                _handlers.Remove(typeof(TEvent));
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;

        public Subscription(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _dispose();
            _disposed = true;
        }
    }
}
