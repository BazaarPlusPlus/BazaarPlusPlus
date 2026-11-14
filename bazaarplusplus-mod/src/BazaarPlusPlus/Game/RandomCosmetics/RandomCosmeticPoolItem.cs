#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.RandomCosmetics;

/// <summary>
/// The Random Cosmetic Pool state of one loadout cosmetic. In Pool Mode the native selected
/// look (the glowing frame, full brightness) means "in the pool" instead of "equipped"; see
/// RandomCosmeticPoolService.ResolveSelected. It lives on the native item, so a list rebuild
/// destroys it with the item.
/// </summary>
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
