#nullable enable
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.EncounterPortraits;

internal static class EncounterPortraitSpriteProvider
{
    private static readonly AsyncLoadCache<Guid, EncounterPortraitLoadOutcome> Portraits = new(
        LoadPortraitCoreAsync
    );

    internal static bool TryGetCached(
        Guid sourceTemplateId,
        out EncounterPortraitLoadOutcome? outcome
    )
    {
        outcome = null;
        return sourceTemplateId != Guid.Empty
            && Portraits.TryGetCached(sourceTemplateId, out outcome);
    }

    internal static Task<EncounterPortraitLoadOutcome?> LoadPortraitAsync(Guid sourceTemplateId)
    {
        if (sourceTemplateId == Guid.Empty)
            return Task.FromResult<EncounterPortraitLoadOutcome?>(null);

        return Portraits.GetOrLoadAsync(sourceTemplateId);
    }

    private static async Task<AsyncLoadResult<EncounterPortraitLoadOutcome>> LoadPortraitCoreAsync(
        Guid sourceTemplateId
    )
    {
        var shouldCache = false;
        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var template = BppStaticDataAccess.GetCardTemplate(staticData, sourceTemplateId);
            if (
                template == null
                || string.IsNullOrWhiteSpace(template.ArtKey)
                || template.ArtKey == "Invalid"
            )
            {
                shouldCache = staticData != null;
                return new AsyncLoadResult<EncounterPortraitLoadOutcome>(
                    EncounterPortraitLoadOutcome.Degraded(
                        EncounterPortraitFailureReason.ArtKeyUnavailable,
                        template?.ArtKey
                    ),
                    shouldCache
                );
            }

            if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
            {
                return new AsyncLoadResult<EncounterPortraitLoadOutcome>(
                    EncounterPortraitLoadOutcome.Degraded(
                        EncounterPortraitFailureReason.AssetLoaderUnavailable,
                        template.ArtKey
                    ),
                    shouldCache
                );
            }

            shouldCache = true;
            var encounterData = await assetLoader.LoadAssetAsyncByAddress<EncounterAssetDataSO>(
                template.ArtKey
            );
            if (encounterData == null)
            {
                return new AsyncLoadResult<EncounterPortraitLoadOutcome>(
                    EncounterPortraitLoadOutcome.Degraded(
                        EncounterPortraitFailureReason.EncounterAssetUnavailable,
                        template.ArtKey
                    ),
                    shouldCache
                );
            }

            var result = await encounterData.LoadPortraitSpriteAsync();
            return new AsyncLoadResult<EncounterPortraitLoadOutcome>(
                result == null
                    ? EncounterPortraitLoadOutcome.Degraded(
                        EncounterPortraitFailureReason.PortraitUnavailable,
                        template.ArtKey
                    )
                    : EncounterPortraitLoadOutcome.Ready(result, template.ArtKey),
                shouldCache
            );
        }
        catch (Exception ex)
        {
            return new AsyncLoadResult<EncounterPortraitLoadOutcome>(
                EncounterPortraitLoadOutcome.Degraded(
                    EncounterPortraitFailureReason.LoadException,
                    artKey: null,
                    ex
                ),
                shouldCache
            );
        }
    }
}

internal enum EncounterPortraitFailureReason
{
    None,
    ArtKeyUnavailable,
    AssetLoaderUnavailable,
    EncounterAssetUnavailable,
    PortraitUnavailable,
    LoadException,
}

internal sealed class EncounterPortraitLoadOutcome
{
    private EncounterPortraitLoadOutcome(
        Sprite? sprite,
        EncounterPortraitFailureReason reason,
        string? artKey,
        Exception? exception
    )
    {
        Sprite = sprite;
        Reason = reason;
        ArtKey = artKey;
        Exception = exception;
    }

    internal Sprite? Sprite { get; }
    internal EncounterPortraitFailureReason Reason { get; }
    internal string? ArtKey { get; }
    internal Exception? Exception { get; }
    internal bool IsDegraded => Reason != EncounterPortraitFailureReason.None;

    internal static EncounterPortraitLoadOutcome Ready(Sprite sprite, string artKey) =>
        new(sprite, EncounterPortraitFailureReason.None, artKey, null);

    internal static EncounterPortraitLoadOutcome Degraded(
        EncounterPortraitFailureReason reason,
        string? artKey,
        Exception? exception = null
    ) => new(null, reason, artKey, exception);
}
