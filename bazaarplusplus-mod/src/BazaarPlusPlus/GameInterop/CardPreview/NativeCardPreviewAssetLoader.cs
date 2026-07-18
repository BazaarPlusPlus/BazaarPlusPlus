#nullable enable
using System.Reflection;
using System.Runtime.ExceptionServices;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using HarmonyLib;
using TheBazaar.AppFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal readonly record struct NativeCardPreviewInstantiateOutcome(
    Component? Card,
    NativeCardPreviewFailure? Failure
);

internal sealed class NativeCardPreviewAssetLoader
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

    internal async Task<NativeCardPreviewInstantiateOutcome> InstantiateInactiveCardAsync(
        TCardBase template,
        Transform parent,
        CancellationToken token = default
    )
    {
        if (template == null || parent == null)
            return default;

        if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
        {
            return Failed(template.Id, NativeCardPreviewFailureReason.AssetLoaderUnavailable);
        }

        var assetField = ResolveAssetField(template);
        if (InstantiateAssetMethod == null || assetField == null)
        {
            return Failed(template.Id, NativeCardPreviewFailureReason.ReflectionUnavailable);
        }

        GameObject? root = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var assetReference = assetField.GetValue(assetLoader);
            if (assetReference == null)
                return Failed(template.Id, NativeCardPreviewFailureReason.PreviewTypeUnavailable);

            var raw = InstantiateAssetMethod.Invoke(assetLoader, new[] { assetReference });
            if (raw is not Task<GameObject> task)
                return Failed(template.Id, NativeCardPreviewFailureReason.ReflectionUnavailable);

            root = await task;
            token.ThrowIfCancellationRequested();
            if (root == null)
            {
                return Failed(
                    template.Id,
                    NativeCardPreviewFailureReason.PreviewComponentUnavailable
                );
            }

            root.SetActive(false);
            root.transform.SetParent(parent, worldPositionStays: false);
            var cardPreviewBaseType = NativeCardPreviewReflection.CardPreviewBaseType;
            if (cardPreviewBaseType == null)
            {
                Object.Destroy(root);
                return Failed(template.Id, NativeCardPreviewFailureReason.PreviewTypeUnavailable);
            }

            var card = root.GetComponent(cardPreviewBaseType);
            if (card == null)
            {
                Object.Destroy(root);
                return Failed(
                    template.Id,
                    NativeCardPreviewFailureReason.PreviewComponentUnavailable
                );
            }

            return new NativeCardPreviewInstantiateOutcome(card, null);
        }
        catch (OperationCanceledException)
        {
            if (root != null)
                Object.Destroy(root);
            throw;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException)
        {
            if (root != null)
                Object.Destroy(root);
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            throw;
        }
        catch (Exception ex)
        {
            if (root != null)
                Object.Destroy(root);
            return new NativeCardPreviewInstantiateOutcome(
                null,
                new NativeCardPreviewFailure(
                    NativeCardPreviewOperation.Instantiate,
                    NativeCardPreviewFailureReason.InstantiateException,
                    template.Id,
                    ex
                )
            );
        }
    }

    private static FieldInfo? ResolveAssetField(TCardBase template) =>
        template.Type switch
        {
            ECardType.Skill => SkillAsset,
            ECardType.Item when template.Size == ECardSize.Small => SmallItemAsset,
            ECardType.Item when template.Size == ECardSize.Medium => MediumItemAsset,
            ECardType.Item when template.Size == ECardSize.Large => LargeItemAsset,
            _ => null,
        };

    private static NativeCardPreviewInstantiateOutcome Failed(
        Guid templateId,
        NativeCardPreviewFailureReason reason
    ) =>
        new(
            null,
            new NativeCardPreviewFailure(NativeCardPreviewOperation.Instantiate, reason, templateId)
        );
}
