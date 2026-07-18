#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal static class HeroPortraitSpriteProvider
{
    private static readonly AsyncLoadCache<EHero, HeroPortraitLoadOutcome> Portraits = new(
        LoadPortraitCoreAsync
    );

    internal static bool IsRenderableHero(EHero hero) =>
        hero != EHero.Common && !string.Equals(hero.ToString(), "Hero8", StringComparison.Ordinal);

    internal static bool TryGetCached(EHero hero, out HeroPortraitLoadOutcome? outcome)
    {
        outcome = null;
        return IsRenderableHero(hero) && Portraits.TryGetCached(hero, out outcome);
    }

    internal static Task<HeroPortraitLoadOutcome?> LoadDefaultPortraitAsync(EHero hero)
    {
        if (!IsRenderableHero(hero))
            return Task.FromResult<HeroPortraitLoadOutcome?>(null);

        return Portraits.GetOrLoadAsync(hero);
    }

    private static async Task<AsyncLoadResult<HeroPortraitLoadOutcome>> LoadPortraitCoreAsync(
        EHero hero
    )
    {
        var shouldCache = false;

        try
        {
            Services.TryGet<CollectionManager>(out var collectionManager);
            if (collectionManager == null)
            {
                return new AsyncLoadResult<HeroPortraitLoadOutcome>(
                    HeroPortraitLoadOutcome.Degraded(
                        HeroPortraitFailureReason.CollectionManagerUnavailable
                    ),
                    shouldCache
                );
            }

            SkinAssetDataSO? skin = collectionManager.GetDefaultHeroSkin(hero);
            shouldCache = true;
            if (skin == null)
            {
                return new AsyncLoadResult<HeroPortraitLoadOutcome>(
                    HeroPortraitLoadOutcome.Degraded(
                        HeroPortraitFailureReason.DefaultSkinUnavailable
                    ),
                    shouldCache
                );
            }

            var result = await skin.LoadPortraitSpriteAsync();
            return new AsyncLoadResult<HeroPortraitLoadOutcome>(
                result == null
                    ? HeroPortraitLoadOutcome.Degraded(
                        HeroPortraitFailureReason.PortraitUnavailable
                    )
                    : HeroPortraitLoadOutcome.Ready(result),
                shouldCache
            );
        }
        catch (Exception ex)
        {
            return new AsyncLoadResult<HeroPortraitLoadOutcome>(
                HeroPortraitLoadOutcome.Degraded(HeroPortraitFailureReason.LoadException, ex),
                shouldCache
            );
        }
    }
}

internal enum HeroPortraitFailureReason
{
    None,
    CollectionManagerUnavailable,
    DefaultSkinUnavailable,
    PortraitUnavailable,
    LoadException,
}

internal sealed class HeroPortraitLoadOutcome
{
    private HeroPortraitLoadOutcome(
        Sprite? sprite,
        HeroPortraitFailureReason reason,
        Exception? exception
    )
    {
        Sprite = sprite;
        Reason = reason;
        Exception = exception;
    }

    internal Sprite? Sprite { get; }
    internal HeroPortraitFailureReason Reason { get; }
    internal Exception? Exception { get; }
    internal bool IsDegraded => Reason != HeroPortraitFailureReason.None;

    internal static HeroPortraitLoadOutcome Ready(Sprite sprite) =>
        new(sprite, HeroPortraitFailureReason.None, null);

    internal static HeroPortraitLoadOutcome Degraded(
        HeroPortraitFailureReason reason,
        Exception? exception = null
    ) => new(null, reason, exception);
}
