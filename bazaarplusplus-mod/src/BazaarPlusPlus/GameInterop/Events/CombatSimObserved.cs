#nullable enable
using BazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.GameInterop.Events;

internal sealed class CombatSimObserved
{
    public NetMessageCombatSim Message { get; set; } = null!;
}
