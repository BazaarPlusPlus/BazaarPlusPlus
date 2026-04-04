#nullable enable
using BazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.Core.Events;

internal sealed class CombatSimObserved
{
    public NetMessageCombatSim Message { get; set; } = null!;
}
