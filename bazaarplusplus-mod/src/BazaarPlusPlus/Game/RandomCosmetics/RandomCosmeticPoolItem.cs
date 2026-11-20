#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.RandomCosmetics;

// Pool Mode repurposes native selection to mean pool membership. The state dies with the native list item.
internal sealed class RandomCosmeticPoolItem : MonoBehaviour
{
    internal bool PoolMode { get; private set; }
    internal bool IsMember { get; private set; }

    internal void Set(bool poolMode, bool member)
    {
        PoolMode = poolMode;
        IsMember = poolMode && member;
    }
}
