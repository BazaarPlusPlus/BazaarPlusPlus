#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.GameInterop.AssetLoading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal readonly record struct NativeCardPreviewInstantiateOutcome(
    Component? Card,
    NativeCardPreviewFailure? Failure
);

internal sealed class NativeCardPreviewAssetLoader
{
    internal async Task<NativeCardPreviewInstantiateOutcome> InstantiateInactiveCardAsync(
        TCardBase template,
        Transform parent,
        CancellationToken token = default
    )
    {
        if (template == null || parent == null)
            return default;
        GameObject? root = null;
        try
        {
            root = await NativeCardPrefabLoader.RentInactiveAsync(template, parent, token);
            var type = NativeCardPreviewReflection.CardPreviewBaseType;
            var card = root != null && type != null ? root.GetComponent(type) : null;
            if (card != null)
                return new(card, null);
            if (root != null)
                Object.Destroy(root);
            return new(
                null,
                new(
                    NativeCardPreviewOperation.Instantiate,
                    NativeCardPreviewFailureReason.PreviewComponentUnavailable,
                    template.Id
                )
            );
        }
        catch (OperationCanceledException)
        {
            if (root != null)
                Object.Destroy(root);
            throw;
        }
        catch (Exception error)
        {
            if (root != null)
                Object.Destroy(root);
            return new(
                null,
                new(
                    NativeCardPreviewOperation.Instantiate,
                    NativeCardPreviewFailureReason.InstantiateException,
                    template.Id,
                    error
                )
            );
        }
    }
}
