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
        VfxConfigField?.FieldType.GetMethod(
            "IsActionAttributeMapped",
            InstanceMembers,
            null,
            new[] { typeof(ActionType) },
            null
        );
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
        var warmup = new ReplayVfxWarmup(assetLoader, stats, outcome);
        var vfxTasks = new List<Task>();

        foreach (var action in actionTypes)
            vfxTasks.Add(WarmActionVfxAsync(warmup, vfxManager, vfxConfig, action));

        if (vfxConfig != null)
        {
            foreach (
                var overrideKey in sequence
                    .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal)
            )
            {
                vfxTasks.Add(WarmOverrideVfxAsync(warmup, vfxConfig, actionTypes, overrideKey));
            }
        }
        await Task.WhenAll(vfxTasks);
    }

    private static async Task WarmActionVfxAsync(
        ReplayVfxWarmup warmup,
        VFXManager vfxManager,
        object? vfxConfig,
        ActionType action
    )
    {
        if (vfxConfig != null && TryIsActionAttributeMapped(vfxConfig, action))
        {
            foreach (var size in PresentationWarmer.WarmupCardSizes)
                await warmup.WarmAsync(TryGetMappedActionVfx(vfxConfig, size, action));
        }

        await warmup.WarmAsync(vfxManager.GetVFX(action));
    }

    private static async Task WarmOverrideVfxAsync(
        ReplayVfxWarmup warmup,
        object vfxConfig,
        IReadOnlyCollection<ActionType> actionTypes,
        string overrideKey
    )
    {
        foreach (var action in actionTypes)
        {
            foreach (var size in PresentationWarmer.WarmupCardSizes)
            {
                await warmup.WarmAsync(
                    await TryGetOverrideActionVfxAsync(vfxConfig, action, size, overrideKey)
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

    // One replay's VFX warmup. Loads go to the Current scope, as VFXManager instantiates them
    // during playback: the game releases them with that scope. The key set lives only for this
    // replay, so many action/size/override lookups resolving to one prefab load and count it once,
    // and the next replay warms again after a scope release.
    private sealed class ReplayVfxWarmup(
        AssetLoader assetLoader,
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        private readonly SemaphoreSlim _semaphore = new(WarmupConstants.ReplayWarmupConcurrency);
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        internal async Task WarmAsync(AssetReference? assetReference)
        {
            if (assetReference == null || !assetReference.RuntimeKeyIsValid())
                return;

            var key = !string.IsNullOrWhiteSpace(assetReference.AssetGUID)
                ? assetReference.AssetGUID
                : assetReference.ToString();
            lock (_keys)
            {
                if (!_keys.Add(key))
                    return;
            }

            await _semaphore.WaitAsync();
            try
            {
                var prefab = await NativeGlobalAssetLoader.LoadByReferenceAsync<GameObject>(
                    assetLoader,
                    assetReference,
                    NativeAssetScopeIntent.Current
                );
                if (prefab != null)
                {
                    stats.VfxPrewarmed++;
                }
                else
                {
                    // The game's loader logs the cause and returns null instead of throwing.
                    stats.VfxFailed++;
                    ReplayWarmupLogging.AssetSkipped(
                        ReplayWarmupStage.CombatVfx,
                        key,
                        ReplayWarmupAssetReasonCode.AssetLoadFailed
                    );
                }
            }
            catch (Exception ex)
            {
                stats.VfxFailed++;
                outcome.ReportDegradation(ReplayPlaybackReasonCode.CombatVfxWarmupFailed, ex);
                ReplayWarmupLogging.AssetSkipped(
                    ReplayWarmupStage.CombatVfx,
                    key,
                    ReplayWarmupAssetReasonCode.AssetLoadFailed,
                    ex
                );
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
