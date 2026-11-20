#nullable enable
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Core.Events;

public sealed class CombatSimObserved
{
    public CombatSimObserved(CombatSimSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public CombatSimSnapshot Snapshot { get; }
}
