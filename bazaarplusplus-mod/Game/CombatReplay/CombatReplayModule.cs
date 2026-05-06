#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayModule : IBppFeature
{
    private readonly IBppEventBus _eventBus;
    private CombatReplayRuntime? _runtime;
    private IDisposable? _messageSubscription;

    public CombatReplayModule(IBppEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public void AttachRuntime(CombatReplayRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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
        _runtime?.ObserveMessage(observed.Message);
    }
}
