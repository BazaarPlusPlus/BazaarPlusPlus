using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2Cpp;
using Il2CppBazaarGameShared.TempoNet.Enums;
using Il2CppTheBazaar;
using Il2CppTheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The game's rank badges and run trophies, the art the desktop History shows, held in the
/// game's Global asset scope. Call under the Android main-thread context; the shared caches
/// coalesce requests from reused rows.
/// </summary>
internal static class AndroidRankArt
{
    private const string RankBadges = "Assets/ScriptableObjects/Rank/RankBadge.asset";
    private static readonly AsyncLoadCache<ERank, Sprite> Ranks = new(LoadRank);
    private static readonly AsyncLoadCache<RunOutcomeTier, Sprite> Trophies = new(LoadTrophy);

    /// <summary>The badge for a stored rank name; null when the name is no game rank.</summary>
    public static Task<Sprite?>? Rank(string? rank) =>
        Enum.TryParse<ERank>(rank?.Trim(), true, out var value)
        && Enum.IsDefined(typeof(ERank), value)
            ? Ranks.GetOrLoadAsync(value)
            : null;

    /// <summary>The trophy a finished run earned; a Misfortune run earns none.</summary>
    public static Task<Sprite?>? Trophy(RunOutcomeTier tier) =>
        tier == RunOutcomeTier.Misfortune ? null : Trophies.GetOrLoadAsync(tier);

    private static async Task<AsyncLoadResult<Sprite>> LoadRank(ERank rank)
    {
        try
        {
            if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
                return new(null, false);
            var source = await Il2CppTasks.AwaitAsync(
                loader.LoadAssetAsyncByAddress<RankBadgeSO>(
                    RankBadges,
                    new Il2CppSystem.Nullable<AssetScope>(AssetScope.Global)
                ),
                "rank badges"
            );
            if (source == null)
                return new(null, true);
            var sprite = await Il2CppTasks.AwaitAsync(
                loader.LoadAssetAsyncByReference<Sprite>(
                    source.GetRankBadge(rank),
                    new Il2CppSystem.Nullable<AssetScope>(AssetScope.Global)
                ),
                "rank badge"
            );
            return new(sprite, true);
        }
        catch (Exception exception)
        {
            Failed(rank.ToString(), exception);
            return new(null, true);
        }
    }

    private static async Task<AsyncLoadResult<Sprite>> LoadTrophy(RunOutcomeTier tier)
    {
        try
        {
            if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
                return new(null, false);
            var sprite = await Il2CppTasks.AwaitAsync(
                loader.LoadAssetAsyncByAddress<Sprite>(
                    $"Trophy_{tier}_TUI",
                    new Il2CppSystem.Nullable<AssetScope>(AssetScope.Global)
                ),
                "run trophy"
            );
            return new(sprite, true);
        }
        catch (Exception exception)
        {
            Failed(tier.ToString(), exception);
            return new(null, true);
        }
    }

    private static void Failed(string key, Exception exception) =>
        BppLog.WarnEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.rank_art_failed"),
            ("key", key),
            ("exception", exception.Message)
        );
}
