#nullable enable
using BazaarGameShared.TempoNet.Enums;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// The game's rank badges and run trophies, the art the desktop History shows, held in the
/// game's Global asset scope: the Mono host's reads behind the Mobile Platform Port's
/// <c>RankBadge</c> and <c>RunTrophy</c>. Main thread; the shared caches coalesce requests from
/// reused rows.
/// </summary>
internal static class TouchRankArt
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
            var source = await loader.LoadAssetAsyncByAddress<RankBadgeSO>(
                RankBadges,
                new Nullable<AssetScope>(AssetScope.Global)
            );
            if (source == null)
                return new(null, true);
            var sprite = await loader.LoadAssetAsyncByReference<Sprite>(
                source.GetRankBadge(rank),
                new Nullable<AssetScope>(AssetScope.Global)
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
            var sprite = await loader.LoadAssetAsyncByAddress<Sprite>(
                $"Trophy_{tier}_TUI",
                new Nullable<AssetScope>(AssetScope.Global)
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
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.rank_art_failed"),
            ("key", key),
            ("exception", exception.Message)
        );
}
