#nullable enable
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

// CollectionManager releases an opponent's skin handle before GenerateEncounterData awaits.
// Copy managed authoring values before suspension; the ScriptableObject may be unloaded later.
internal sealed record NativeHeroPortraitSkin(
    string Name,
    AssetReferenceGameObject? CharacterPrefab,
    string IdleAnimation,
    bool NativeAnimated,
    string PortraitKey
)
{
    internal static NativeHeroPortraitSkin Capture(SkinAssetDataSO skin) =>
        new(
            // Unity's destroyed-object equality does not imply a null managed instance.
            skin != null
                ? skin.name
                : skin!.portraitTextureReference?.AssetGUID ?? "unloaded_skin",
            skin.skinEditSkinPrefab,
            string.IsNullOrEmpty(skin.animatedPortraitIdleAnimationName)
                ? "idle"
                : skin.animatedPortraitIdleAnimationName,
            skin.animatedPortraitPrefabReference != null
                && skin.animatedPortraitPrefabReference.RuntimeKeyIsValid(),
            skin.portraitTextureReference?.RuntimeKey?.ToString() ?? ""
        );
}
