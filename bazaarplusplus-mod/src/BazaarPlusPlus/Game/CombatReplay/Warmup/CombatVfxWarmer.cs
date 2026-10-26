#nullable enable
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarPlusPlus.GameInterop.AssetLoading;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class CombatVfxWarmer
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo? VfxConfigField = typeof(VFXManager).GetField(
        "vfxManagerSO",
        InstanceMembers
    );
    private static readonly MethodInfo? IsActionAttributeMappedMethod =
        VfxConfigField?.FieldType.GetMethod("IsActionAttributeMapped", InstanceMembers);
    private static readonly MethodInfo? GetMappedActionVfxMethod =
        VfxConfigField?.FieldType.GetMethod(
            "GetVFX",
            InstanceMembers,
            null,
            new[] { typeof(ECardSize), typeof(ActionType) },
            null
        );
    private static readonly MethodInfo? GetActionOverrideVfxMethod =
        VfxConfigField?.FieldType.GetMethod(
            "GetActionOverrideVFX",
            InstanceMembers,
            null,
            new[] { typeof(ActionType), typeof(ECardSize), typeof(string) },
            null
        );

    internal static async Task WarmCombatVfxAsync(
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        Services.TryGet<VFXManager>(out var vfxManager);
        if (assetLoader == null || vfxManager == null)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.CombatVfxWarmupFailed);
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
        var vfxConfig = VfxConfigField?.GetValue(vfxManager);
        var vfxSemaphore = new SemaphoreSlim(WarmupConstants.ReplayWarmupConcurrency);
        var vfxTasks = new List<Task>();

        foreach (var action in actionTypes)
        {
            vfxTasks.Add(
                WarmActionVfxAsync(
                    assetLoader,
                    vfxManager,
                    vfxConfig,
                    action,
                    vfxSemaphore,
                    stats,
                    outcome
                )
            );
        }

        if (vfxConfig != null)
        {
            foreach (
                var overrideKey in sequence
                    .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal)
            )
            {
                vfxTasks.Add(
                    WarmOverrideVfxAsync(
                        assetLoader,
                        vfxConfig,
                        actionTypes,
                        overrideKey,
                        vfxSemaphore,
                        stats,
                        outcome
                    )
                );
            }
        }
        await Task.WhenAll(vfxTasks);
    }

    private static async Task WarmActionVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        object? vfxConfig,
        ActionType action,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (vfxConfig != null && TryIsActionAttributeMapped(vfxConfig, action))
        {
            foreach (var size in PresentationWarmer.WarmupCardSizes)
            {
                await WarmVfxReferenceAsync(
                    assetLoader,
                    TryGetMappedActionVfx(vfxConfig, size, action),
                    semaphore,
                    stats,
                    outcome
                );
            }
        }

        await WarmVfxReferenceAsync(
            assetLoader,
            vfxManager.GetVFX(action),
            semaphore,
            stats,
            outcome
        );
    }

    private static async Task WarmOverrideVfxAsync(
        AssetLoader assetLoader,
        object vfxConfig,
        IReadOnlyCollection<ActionType> actionTypes,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        foreach (var action in actionTypes)
        {
            foreach (var size in PresentationWarmer.WarmupCardSizes)
            {
                await WarmVfxReferenceAsync(
                    assetLoader,
                    await TryGetOverrideActionVfxAsync(vfxConfig, action, size, overrideKey),
                    semaphore,
                    stats,
                    outcome
                );
            }
        }
    }

    private static bool TryIsActionAttributeMapped(object vfxConfig, ActionType action) =>
        IsActionAttributeMappedMethod?.Invoke(vfxConfig, new object[] { action }) as bool? == true;

    private static AssetReference? TryGetMappedActionVfx(
        object vfxConfig,
        ECardSize size,
        ActionType action
    ) =>
        GetMappedActionVfxMethod?.Invoke(vfxConfig, new object[] { size, action })
        as AssetReference;

    private static async Task<AssetReference?> TryGetOverrideActionVfxAsync(
        object vfxConfig,
        ActionType action,
        ECardSize size,
        string overrideKey
    )
    {
        if (GetActionOverrideVfxMethod == null)
            return null;

        var taskObject = GetActionOverrideVfxMethod.Invoke(
            vfxConfig,
            new object[] { action, size, overrideKey }
        );
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
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (assetReference == null || !assetReference.RuntimeKeyIsValid())
            return;

        // Current scope, as VFXManager instantiates it during playback: the game releases the
        // prefab with that scope and dedupes repeat loads within it, so no process-level key set.
        await semaphore.WaitAsync();
        try
        {
            _ = await NativeGlobalAssetLoader.LoadByReferenceAsync<GameObject>(
                assetLoader,
                assetReference,
                NativeAssetScopeIntent.Current
            );
            stats.VfxPrewarmed++;
        }
        catch (Exception ex)
        {
            stats.VfxFailed++;
            outcome.ReportDegradation(ReplayPlaybackReasonCode.CombatVfxWarmupFailed, ex);
            ReplayWarmupLogging.AssetSkipped(
                ReplayWarmupStage.CombatVfx,
                !string.IsNullOrWhiteSpace(assetReference.AssetGUID)
                    ? assetReference.AssetGUID
                    : assetReference.ToString(),
                ReplayWarmupAssetReasonCode.AssetLoadFailed,
                ex
            );
        }
        finally
        {
            semaphore.Release();
        }
    }
}
