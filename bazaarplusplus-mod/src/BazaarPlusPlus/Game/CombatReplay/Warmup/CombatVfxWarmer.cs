#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class CombatVfxWarmer
{
    internal static async Task WarmCombatVfxAsync(
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        Services.TryGet<VFXManager>(out var vfxManager);
        if (assetLoader == null || vfxManager == null)
        {
            BppLog.Warn(
                "CombatVfxWarmer",
                "Saved replay combat VFX warmup skipped because replay asset services are unavailable."
            );
            return;
        }

        var actionTypes = sequence
            .CombatMessage.Data.Frames.SelectMany(frame =>
                frame?.Events ?? Enumerable.Empty<ICombatSimEvent>()
            )
            .OfType<CombatSimEventEffectExecuted>()
            .Select(evt => DTOUtils.GetActionType(evt.ActionType))
            .Where(action => action != ActionType.Unknown)
            .Distinct()
            .ToList();
        var vfxSemaphore = new SemaphoreSlim(WarmupConstants.ReplayWarmupConcurrency);
        var vfxTasks = new List<Task>();

        foreach (var action in actionTypes)
        {
            vfxTasks.Add(WarmActionVfxAsync(assetLoader, vfxManager, action, vfxSemaphore, stats));
        }

        foreach (
            var overrideKey in sequence
                .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
        )
        {
            vfxTasks.Add(
                WarmOverrideVfxAsync(
                    assetLoader,
                    vfxManager,
                    actionTypes,
                    overrideKey,
                    vfxSemaphore,
                    stats
                )
            );
        }
        await Task.WhenAll(vfxTasks);
    }

    private static async Task WarmActionVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        ActionType action,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetVfxConfig(vfxManager);
        if (vfxConfig == null)
        {
            await WarmVfxReferenceAsync(assetLoader, vfxManager.GetVFX(action), semaphore, stats);
            return;
        }

        if (TryIsActionAttributeMapped(vfxConfig, action))
        {
            foreach (var size in PresentationWarmer.WarmupCardSizes)
            {
                await WarmVfxReferenceAsync(
                    assetLoader,
                    TryGetMappedActionVfx(vfxConfig, size, action),
                    semaphore,
                    stats
                );
            }
        }

        await WarmVfxReferenceAsync(assetLoader, vfxManager.GetVFX(action), semaphore, stats);
    }

    private static async Task WarmOverrideVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        IReadOnlyCollection<ActionType> actionTypes,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetVfxConfig(vfxManager);
        if (vfxConfig == null)
            return;

        foreach (var action in actionTypes)
        {
            foreach (var size in PresentationWarmer.WarmupCardSizes)
            {
                await WarmVfxReferenceAsync(
                    assetLoader,
                    await TryGetOverrideActionVfxAsync(vfxConfig, action, size, overrideKey),
                    semaphore,
                    stats
                );
            }
        }
    }

    private static object? GetVfxConfig(VFXManager vfxManager)
    {
        return vfxManager
            .GetType()
            .GetField(
                "vfxManagerSO",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?.GetValue(vfxManager);
    }

    private static bool TryIsActionAttributeMapped(object vfxConfig, ActionType action)
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "IsActionAttributeMapped",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        return method?.Invoke(vfxConfig, new object[] { action }) as bool? == true;
    }

    private static AssetReference? TryGetMappedActionVfx(
        object vfxConfig,
        ECardSize size,
        ActionType action
    )
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "GetVFX",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ECardSize), typeof(ActionType) },
                null
            );
        return method?.Invoke(vfxConfig, new object[] { size, action }) as AssetReference;
    }

    private static async Task<AssetReference?> TryGetOverrideActionVfxAsync(
        object vfxConfig,
        ActionType action,
        ECardSize size,
        string overrideKey
    )
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "GetActionOverrideVFX",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ActionType), typeof(ECardSize), typeof(string) },
                null
            );
        if (method == null)
            return null;

        var taskObject = method.Invoke(vfxConfig, new object[] { action, size, overrideKey });
        if (taskObject is Task<AssetReference> typedTask)
            return await typedTask;

        if (taskObject is not Task task)
            return taskObject as AssetReference;

        await task;
        return task.GetType()
                .GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(task) as AssetReference;
    }

    private static async Task WarmVfxReferenceAsync(
        AssetLoader assetLoader,
        AssetReference? assetReference,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (assetReference == null || !assetReference.RuntimeKeyIsValid())
            return;

        var key = !string.IsNullOrWhiteSpace(assetReference.AssetGUID)
            ? assetReference.AssetGUID
            : assetReference.ToString();
        if (!WarmupCache.TryReserveCacheKey(WarmupCache.PrewarmedVfxKeys, key))
        {
            stats.VfxSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            _ = await assetLoader.LoadAssetAsyncByReference<GameObject>(assetReference);
            stats.VfxPrewarmed++;
        }
        catch (Exception ex)
        {
            WarmupCache.ReleaseCacheKey(WarmupCache.PrewarmedVfxKeys, key);
            stats.VfxFailed++;
            BppLog.Debug(
                "CombatVfxWarmer",
                $"Saved replay VFX warmup skipped for '{key}': {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }
}
