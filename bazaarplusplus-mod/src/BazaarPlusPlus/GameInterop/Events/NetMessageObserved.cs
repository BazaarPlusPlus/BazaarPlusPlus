#nullable enable
using BazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.GameInterop.Events;

internal sealed class NetMessageObserved
{
    public INetMessage Message { get; set; } = null!;
}
