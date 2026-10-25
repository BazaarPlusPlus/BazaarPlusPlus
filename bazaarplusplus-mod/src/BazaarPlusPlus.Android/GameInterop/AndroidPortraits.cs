using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2Cpp;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppTheBazaar;
using Il2CppTheBazaar.AppFramework;
using Il2CppTheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Native default hero and encounter art, held in the game's Global asset scope. Call under the
/// Android main-thread context; the shared cache coalesces requests from rebuilt UI trees.
/// </summary>
internal static class AndroidPortraits
{
    private static readonly AsyncLoadCache<CardHero, Sprite> Heroes = new(LoadHero);
    private static readonly AsyncLoadCache<Guid, Sprite> Encounters = new(LoadEncounter);

    public static Task<Sprite?> Hero(CardHero hero) => Heroes.GetOrLoadAsync(hero);

    public static Task<Sprite?> Encounter(Guid templateId) => Encounters.GetOrLoadAsync(templateId);

    private static async Task<AsyncLoadResult<Sprite>> LoadHero(CardHero hero)
    {
        try
        {
            if (
                hero == CardHero.Common
                || !AndroidCardFacets.Hero.TryToSource(hero, out var native)
            )
                return new(null, true);
            if (
                !Services.TryGet<CollectionManager>(out var collection)
                || collection == null
                || !Services.TryGet<AssetLoader>(out var loader)
                || loader == null
            )
                return new(null, false);
            var skin = collection.GetDefaultHeroSkin(native);
            if (skin == null)
                return new(null, true);
            var sprite = await Il2CppTasks.AwaitAsync(
                loader.LoadAssetAsyncByReference<Sprite>(
                    skin.portraitTextureReference,
                    new Il2CppSystem.Nullable<AssetScope>(AssetScope.Global)
                ),
                "hero portrait"
            );
            if (sprite == null)
            {
                var texture = await Il2CppTasks.AwaitAsync(
                    loader.LoadAssetAsyncByReference<Texture2D>(
                        skin.storePortraitTextureReference,
                        new Il2CppSystem.Nullable<AssetScope>(AssetScope.Global)
                    ),
                    "hero store portrait"
                );
                if (texture != null)
                    sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f,
                        0u,
                        SpriteMeshType.FullRect
                    );
            }
            return new(sprite, true);
        }
        catch (Exception exception)
        {
            Failed(hero.ToString(), exception);
            return new(null, true);
        }
    }

    private static async Task<AsyncLoadResult<Sprite>> LoadEncounter(Guid templateId)
    {
        try
        {
            if (templateId == Guid.Empty)
                return new(null, true);
            if (
                !Il2CppTheBazaar.Data.IsManagerCreated()
                || !Services.TryGet<AssetLoader>(out var loader)
                || loader == null
            )
                return new(null, false);
            var template = Il2CppTheBazaar
                .Data.GetStatic()
                ?.GetCardById(Il2CppSystem.Guid.Parse(templateId.ToString()))
                ?.TryCast<TCardBase>();
            if (
                template == null
                || string.IsNullOrWhiteSpace(template.ArtKey)
                || template.ArtKey == "Invalid"
            )
                return new(null, true);
            var data = await Il2CppTasks.AwaitAsync(
                loader.LoadAssetAsyncByAddress<EncounterAssetDataSO>(
                    template.ArtKey,
                    new Il2CppSystem.Nullable<AssetScope>(AssetScope.Global)
                ),
                "encounter portrait"
            );
            return new(data?.portraitTextureReference, true);
        }
        catch (Exception exception)
        {
            Failed(templateId.ToString(), exception);
            return new(null, true);
        }
    }

    private static void Failed(string key, Exception exception) =>
        BppLog.WarnEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.portrait.load_failed"),
            ("key", key),
            ("exception", exception.Message)
        );
}
