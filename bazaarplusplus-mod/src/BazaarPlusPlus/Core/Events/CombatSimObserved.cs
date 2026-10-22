#nullable enable
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Core.Events;

/// <summary>Published when the game is about to play back a combat simulation.</summary>
internal sealed class CombatSimObserved
{
    public CombatSimObserved(CombatSimSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public CombatSimSnapshot Snapshot { get; }
}
