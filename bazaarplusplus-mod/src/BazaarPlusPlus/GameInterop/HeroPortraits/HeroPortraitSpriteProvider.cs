#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal static class HeroPortraitSpriteProvider
{
    private const string LogComponent = "HeroPortrait";

    private static readonly Dictionary<EHero, Sprite?> CachedSprites = new();
    private static readonly Dictionary<EHero, Task<Sprite?>> InFlightLoads = new();

    internal static bool IsRenderableHero(EHero hero) =>
        hero != EHero.Common && !string.Equals(hero.ToString(), "Hero8", StringComparison.Ordinal);

    internal static bool TryGetCached(EHero hero, out Sprite? sprite)
    {
        sprite = null;
        return IsRenderableHero(hero) && CachedSprites.TryGetValue(hero, out sprite);
    }

    internal static Task<Sprite?> LoadDefaultPortraitAsync(EHero hero)
    {
        if (!IsRenderableHero(hero))
            return Task.FromResult<Sprite?>(null);

        if (CachedSprites.TryGetValue(hero, out var cached))
            return Task.FromResult(cached);

        if (InFlightLoads.TryGetValue(hero, out var inFlight))
            return inFlight;

        var task = LoadAndMaybeCacheAsync(hero);
        InFlightLoads[hero] = task;
        return task;
    }

    private static async Task<Sprite?> LoadAndMaybeCacheAsync(EHero hero)
    {
        Sprite? result = null;
        var shouldCacheResult = false;

        try
        {
            Services.TryGet<CollectionManager>(out var collectionManager);
            if (collectionManager == null)
            {
                BppLog.Warn(
                    LogComponent,
                    $"CollectionManager unavailable for hero={hero}; using text fallback."
                );
                return null;
            }

            SkinAssetDataSO? skin = collectionManager.GetDefaultHeroSkin(hero);
            shouldCacheResult = true;
            if (skin == null)
            {
                BppLog.Warn(
                    LogComponent,
                    $"No default hero skin for hero={hero}; using text fallback."
                );
                return null;
            }

            result = await skin.LoadPortraitSpriteAsync();
            if (result == null)
                BppLog.Debug(
                    LogComponent,
                    $"No static portrait sprite for hero={hero}; using text fallback."
                );
            return result;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                LogComponent,
                $"Failed to load hero portrait for hero={hero}: {ex.Message}"
            );
            return null;
        }
        finally
        {
            InFlightLoads.Remove(hero);
            if (shouldCacheResult)
                CachedSprites[hero] = result;
        }
    }
}
