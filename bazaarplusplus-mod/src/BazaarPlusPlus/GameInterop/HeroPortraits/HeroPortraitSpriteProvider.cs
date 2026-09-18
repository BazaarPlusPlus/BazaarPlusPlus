#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.AssetLoading;
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
        hero != EHero.Common && Enum.IsDefined(typeof(EHero), hero);

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

            if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
            {
                return new AsyncLoadResult<HeroPortraitLoadOutcome>(
                    HeroPortraitLoadOutcome.Degraded(
                        HeroPortraitFailureReason.AssetLoaderUnavailable
                    ),
                    shouldCache: false
                );
            }

            var result = await NativeGlobalAssetLoader.LoadByReferenceAsync<Sprite>(
                assetLoader,
                skin.portraitTextureReference
            );
            if (result == null)
            {
                var fallbackTexture = await NativeGlobalAssetLoader.LoadByReferenceAsync<Texture2D>(
                    assetLoader,
                    skin.storePortraitTextureReference
                );
                result = CreateSprite(fallbackTexture, skin.name);
            }

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

    private static Sprite? CreateSprite(Texture2D? texture, string skinName)
    {
        if (texture == null)
            return null;

        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            extrude: 0u,
            SpriteMeshType.FullRect
        );
        sprite.name = $"BPP_{skinName}_CollectionPortrait";
        return sprite;
    }
}

internal enum HeroPortraitFailureReason
{
    None,
    CollectionManagerUnavailable,
    DefaultSkinUnavailable,
    AssetLoaderUnavailable,
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
