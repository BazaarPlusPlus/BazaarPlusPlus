#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayModule : IBppFeature
{
    private readonly IBppEventBus _eventBus;
    private readonly Func<CombatReplayRuntime?> _runtimeAccessor;
    private IDisposable? _messageSubscription;

    public CombatReplayModule(IBppEventBus eventBus, Func<CombatReplayRuntime?> runtimeAccessor)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _runtimeAccessor =
            runtimeAccessor ?? throw new ArgumentNullException(nameof(runtimeAccessor));
    }

    public void Start()
    {
        _messageSubscription = _eventBus.Subscribe<NetMessageObserved>(OnNetMessageObserved);
    }

    public void Stop()
    {
        _messageSubscription?.Dispose();
        _messageSubscription = null;
    }

    private void OnNetMessageObserved(NetMessageObserved observed)
    {
        _runtimeAccessor()?.ObserveMessage(observed.Message);
    }
}
