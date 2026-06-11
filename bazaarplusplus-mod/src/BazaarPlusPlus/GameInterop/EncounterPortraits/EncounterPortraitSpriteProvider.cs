#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.EncounterPortraits;

internal static class EncounterPortraitSpriteProvider
{
    private const string LogComponent = "EncounterPortrait";

    private static readonly Dictionary<Guid, Sprite?> CachedSprites = new();
    private static readonly Dictionary<Guid, Task<Sprite?>> InFlightLoads = new();

    internal static bool TryGetCached(Guid sourceTemplateId, out Sprite? sprite)
    {
        sprite = null;
        return sourceTemplateId != Guid.Empty
            && CachedSprites.TryGetValue(sourceTemplateId, out sprite);
    }

    internal static Task<Sprite?> LoadPortraitAsync(Guid sourceTemplateId)
    {
        if (sourceTemplateId == Guid.Empty)
            return Task.FromResult<Sprite?>(null);

        if (CachedSprites.TryGetValue(sourceTemplateId, out var cached))
            return Task.FromResult(cached);

        if (InFlightLoads.TryGetValue(sourceTemplateId, out var inFlight))
            return inFlight;

        var task = LoadAndMaybeCacheAsync(sourceTemplateId);
        InFlightLoads[sourceTemplateId] = task;
        return task;
    }

    private static async Task<Sprite?> LoadAndMaybeCacheAsync(Guid sourceTemplateId)
    {
        Sprite? result = null;
        var shouldCacheResult = false;
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
                shouldCacheResult = staticData != null;
                BppLog.Warn(
                    LogComponent,
                    $"No encounter art key sourceTemplateId={sourceTemplateId}; using text fallback."
                );
                return null;
            }

            if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
            {
                BppLog.Warn(
                    LogComponent,
                    $"AssetLoader unavailable sourceTemplateId={sourceTemplateId}; using text fallback."
                );
                return null;
            }

            shouldCacheResult = true;
            var encounterData = await assetLoader.LoadAssetAsyncByAddress<EncounterAssetDataSO>(
                template.ArtKey
            );
            if (encounterData == null)
            {
                BppLog.Warn(
                    LogComponent,
                    $"Encounter asset unavailable artKey={template.ArtKey}; using text fallback."
                );
                return null;
            }

            result = await encounterData.LoadPortraitSpriteAsync();
            return result;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                LogComponent,
                $"Failed to load encounter portrait sourceTemplateId={sourceTemplateId}: {ex.Message}"
            );
            return null;
        }
        finally
        {
            InFlightLoads.Remove(sourceTemplateId);
            if (shouldCacheResult)
                CachedSprites[sourceTemplateId] = result;
        }
    }
}
