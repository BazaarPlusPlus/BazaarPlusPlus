#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class WarmupCache
{
    internal static readonly object Lock = new();
    internal static readonly HashSet<string> PreloadedCardKeys = new(StringComparer.Ordinal);
    internal static readonly HashSet<string> PreloadedOverrideKeys = new(StringComparer.Ordinal);
    internal static readonly HashSet<string> PrewarmedVfxKeys = new(StringComparer.Ordinal);
    internal static bool SharedAssetsPreloaded;

    internal static bool TryReserveSharedAssetsPreload()
    {
        lock (Lock)
        {
            if (SharedAssetsPreloaded)
                return false;

            SharedAssetsPreloaded = true;
            return true;
        }
    }

    internal static void ReleaseSharedAssetsPreload()
    {
        lock (Lock)
        {
            SharedAssetsPreloaded = false;
        }
    }

    internal static bool TryReserveCacheKey(HashSet<string> cache, string key)
    {
        lock (Lock)
        {
            return cache.Add(key);
        }
    }

    internal static void ReleaseCacheKey(HashSet<string> cache, string key)
    {
        lock (Lock)
        {
            cache.Remove(key);
        }
    }
}
