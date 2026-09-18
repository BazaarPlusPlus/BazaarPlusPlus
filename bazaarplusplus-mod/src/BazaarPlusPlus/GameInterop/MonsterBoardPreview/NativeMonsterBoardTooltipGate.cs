#nullable enable
using System.Runtime.CompilerServices;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

// Weak identities survive native pooling without retaining old card data or owners.
internal sealed class NativeMonsterBoardTooltipGate
{
    private readonly ConditionalWeakTable<object, Lease> _leases = new();

    internal Lease Register(object data)
    {
        var lease = new Lease();
        _leases.Remove(data);
        _leases.Add(data, lease);
        return lease;
    }

    internal bool Allows(object data) => !_leases.TryGetValue(data, out var lease) || lease.Active;

    internal sealed class Lease
    {
        internal bool Active { get; private set; } = true;

        internal void Retire() => Active = false;
    }
}
