#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatStatusBarModule : IBppFeature
{
    private readonly IBppEventBus _eventBus;
    private readonly IRunContext _runContext;
    private IDisposable? _combatSimSubscription;
    private IDisposable? _combatFrameSubscription;

    public CombatStatusBarModule(IBppEventBus eventBus, IRunContext runContext)
    {
        _eventBus = eventBus;
        _runContext = runContext;
    }

    public void Start()
    {
        _combatSimSubscription = _eventBus.Subscribe<CombatSimObserved>(OnCombatSimObserved);
        _combatFrameSubscription = _eventBus.Subscribe<CombatFrameAdvanced>(OnCombatFrameAdvanced);
    }

    public void Stop()
    {
        _combatFrameSubscription?.Dispose();
        _combatFrameSubscription = null;
        _combatSimSubscription?.Dispose();
        _combatSimSubscription = null;
    }

    private void OnCombatSimObserved(CombatSimObserved observed)
    {
        var message = observed.Message;
        if (_runContext.LastMessageId == message.MessageId)
            return;

        _runContext.LastMessageId = message.MessageId;
        CombatStatusBar.SetCombatFrameTotal(message.Data?.Frames?.Count ?? 0);
        var winner = message.Data?.Winner;
        _runContext.LastVictoryCondition =
            winner == ECombatantId.Player ? EVictoryCondition.Win : EVictoryCondition.Lose;
    }

    private static void OnCombatFrameAdvanced(CombatFrameAdvanced _)
    {
        CombatStatusBar.AdvanceCombatFrame();
    }
}
