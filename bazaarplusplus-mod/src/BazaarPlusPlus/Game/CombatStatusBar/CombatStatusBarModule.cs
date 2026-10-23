#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatStatusBarModule : IBppFeature
{
    private readonly IBppEventBus _eventBus;

    // Null, not empty: the snapshot maps a null message id to "", which must still count as new.
    private string? _lastMessageId;
    private IDisposable? _combatSimSubscription;
    private IDisposable? _combatFrameSubscription;

    public CombatStatusBarModule(IBppEventBus eventBus)
    {
        _eventBus = eventBus;
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
        var snapshot = observed.Snapshot;
        if (_lastMessageId == snapshot.MessageId)
            return;

        _lastMessageId = snapshot.MessageId;
        CombatStatusBar.State.SetCombatFrameTotal(snapshot.FrameCount);
    }

    private static void OnCombatFrameAdvanced(CombatFrameAdvanced _)
    {
        CombatStatusBar.State.AdvanceCombatFrame();
    }
}
