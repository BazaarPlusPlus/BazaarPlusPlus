#nullable enable
using System.Reflection;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using HarmonyLib;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.GameInterop.AssetLoading;

// The shared native prefab/reference seam. Callers own every successfully returned root.
internal static class NativeCardPrefabLoader
{
    private static readonly MethodInfo? InstantiateAssetMethod = AccessTools.Method(
        typeof(AssetLoader),
        "InstantiateAssetAsyncByReference"
    );
    private static readonly FieldInfo? SmallItemAsset = AccessTools.Field(
        typeof(AssetLoader),
        "SmallCardUIAssetRef"
    );
    private static readonly FieldInfo? MediumItemAsset = AccessTools.Field(
        typeof(AssetLoader),
        "MediumCardUIAssetRef"
    );
    private static readonly FieldInfo? LargeItemAsset = AccessTools.Field(
        typeof(AssetLoader),
        "LargeCardUIAssetRef"
    );
    private static readonly FieldInfo? SkillAsset = AccessTools.Field(
        typeof(AssetLoader),
        "SkillUIAssetRef"
    );

    internal static async Task<GameObject?> LoadPrefabAsync(
        TCardBase template,
        ECardSize? size = null
    )
    {
        if (
            !Services.TryGet<AssetLoader>(out var loader)
            || loader == null
            || ResolveAssetField(template, size)?.GetValue(loader) is not AssetReference reference
        )
            return null;
        return await NativeGlobalAssetLoader.LoadByReferenceAsync<GameObject>(loader, reference);
    }

    internal static async Task<GameObject?> RentInactiveAsync(
        TCardBase template,
        Transform parent,
        CancellationToken token = default,
        ECardSize? size = null
    )
    {
        if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
            return null;
        var reference = ResolveAssetField(template, size)?.GetValue(loader);
        if (
            reference == null
            || !NativeAssetLoaderInvocation.TryBuildArguments(
                InstantiateAssetMethod,
                reference,
                NativeAssetScopeIntent.Current,
                out var arguments
            )
        )
            return null;
        token.ThrowIfCancellationRequested();
        GameObject? root = null;
        try
        {
            if (InstantiateAssetMethod!.Invoke(loader, arguments) is not Task<GameObject> task)
                return null;
            root = await task;
            token.ThrowIfCancellationRequested();
            if (root == null)
                return null;
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            return root;
        }
        catch
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    private static FieldInfo? ResolveAssetField(TCardBase template, ECardSize? size = null) =>
        (template.Type, size ?? template.Size) switch
        {
            (ECardType.Skill, _) => SkillAsset,
            (ECardType.Item, ECardSize.Small) => SmallItemAsset,
            (ECardType.Item, ECardSize.Medium) => MediumItemAsset,
            (ECardType.Item, ECardSize.Large) => LargeItemAsset,
            _ => null,
        };
}
