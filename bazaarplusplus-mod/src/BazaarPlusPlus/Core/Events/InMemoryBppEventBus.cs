#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace BazaarPlusPlus.Core.Events;

internal sealed class InMemoryBppEventBus : IBppEventBus
{
    private readonly object _syncRoot = new();

    // Copy-on-write: each event type maps to an immutable handler array that Subscribe/Unsubscribe
    // replace wholesale. Publish can therefore grab the current array under a brief lock and iterate
    // it without allocating a per-call snapshot, while a handler that subscribes/unsubscribes during
    // dispatch still cannot mutate the array the in-flight Publish is walking.
    private readonly Dictionary<Type, Delegate[]> _handlers = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : class
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        lock (_syncRoot)
        {
            if (_handlers.TryGetValue(typeof(TEvent), out var existing))
            {
                var updated = new Delegate[existing.Length + 1];
                Array.Copy(existing, updated, existing.Length);
                updated[existing.Length] = handler;
                _handlers[typeof(TEvent)] = updated;
            }
            else
            {
                _handlers[typeof(TEvent)] = new Delegate[] { handler };
            }
        }

        return new Subscription(() => Unsubscribe(handler));
    }

    public void Publish<TEvent>(TEvent eventData)
        where TEvent : class
    {
        if (eventData == null)
            throw new ArgumentNullException(nameof(eventData));

        Delegate[]? snapshot;
        lock (_syncRoot)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out snapshot) || snapshot.Length == 0)
                return;
        }

        // snapshot is immutable (copy-on-write), so iterating it outside the lock is safe.
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
                global::BazaarPlusPlus.Infrastructure.BppLog.Error(
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
            if (!_handlers.TryGetValue(typeof(TEvent), out var existing))
                return;

            var index = Array.IndexOf(existing, (Delegate)handler);
            if (index < 0)
                return;

            if (existing.Length == 1)
            {
                _handlers.Remove(typeof(TEvent));
                return;
            }

            var updated = new Delegate[existing.Length - 1];
            Array.Copy(existing, 0, updated, 0, index);
            Array.Copy(existing, index + 1, updated, index, existing.Length - index - 1);
            _handlers[typeof(TEvent)] = updated;
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;
        private int _disposed;

        public Subscription(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            _dispose();
        }
    }
}
