#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Core.Events;

public sealed class InMemoryBppEventBus : IBppEventBus
{
    private readonly object _syncRoot = new();

    // Copy-on-write arrays let a publisher iterate outside the lock even if a handler
    // subscribes or unsubscribes during dispatch.
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

        foreach (var registration in snapshot)
        {
            try
            {
                ((Action<TEvent>)registration).Invoke(eventData);
            }
            catch (Exception ex)
            {
                global::BazaarPlusPlus.Infrastructure.BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Plugin,
                        "plugin.event_handler.degraded",
                        storm: ["event_id", "handler_id"]
                    ),
                    ex,
                    ("event_id", typeof(TEvent).FullName),
                    ("handler_id", registration.Method.DeclaringType?.FullName),
                    ("reason_code", global::BazaarPlusPlus.PluginLogReasonCode.HandlerException)
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
