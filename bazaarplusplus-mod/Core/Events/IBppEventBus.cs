using System;

namespace BazaarPlusPlus.Core.Events;

internal interface IBppEventBus
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : class;

    void Publish<TEvent>(TEvent eventData)
        where TEvent : class;
}
