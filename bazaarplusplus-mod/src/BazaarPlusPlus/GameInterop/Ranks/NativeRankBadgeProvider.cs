#nullable enable
using BazaarGameShared.TempoNet.Enums;
using BazaarPlusPlus.GameInterop.AssetLoading;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Ranks;

// Uses the same rank-to-art mapping as HeroBannerController, without reading live player rank.
internal sealed class NativeRankBadgeProvider
{
    private static readonly ERank[] Ranks = (ERank[])Enum.GetValues(typeof(ERank));
    private readonly Dictionary<ERank, Sprite> _badges = new();
    private Task<(ERank Rank, Sprite? Sprite)[]>? _load;
    private float _retryAt;

    internal Sprite? Get(string? rank) =>
        Enum.TryParse<ERank>(rank?.Trim(), true, out var value)
        && Enum.IsDefined(typeof(ERank), value)
        && _badges.TryGetValue(value, out var sprite)
            ? sprite
            : null;

    internal bool Tick()
    {
        var changed = false;
        if (_load?.IsCompleted == true)
        {
            if (_load.Status == TaskStatus.RanToCompletion)
            {
                foreach (var entry in _load.Result)
                    if (entry.Sprite != null)
                    {
                        _badges[entry.Rank] = entry.Sprite;
                        changed = true;
                    }
            }
            else
                _ = _load.Exception;
            _load = null;
        }
        if (_load != null || Time.unscaledTime < _retryAt || AllBadgesReady())
            return changed;
        var missing = Ranks
            .Where(rank => !_badges.TryGetValue(rank, out var sprite) || sprite == null)
            .ToArray();
        if (missing.Length == 0)
            return changed;
        _retryAt = Time.unscaledTime + 5;
        if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
            return changed;
        _load = LoadMissing(loader, missing);
        return changed;
    }

    private static async Task<(ERank Rank, Sprite? Sprite)[]> LoadMissing(
        AssetLoader loader,
        ERank[] missing
    )
    {
        var source = await NativeGlobalAssetLoader.LoadByAddressAsync<RankBadgeSO>(
            loader,
            "Assets/ScriptableObjects/Rank/RankBadge.asset"
        );
        return source == null
            ? Array.Empty<(ERank, Sprite?)>()
            : await Task.WhenAll(missing.Select(rank => Load(loader, source, rank)));
    }

    private bool AllBadgesReady()
    {
        foreach (var rank in Ranks)
            if (!_badges.TryGetValue(rank, out var sprite) || sprite == null)
                return false;
        return true;
    }

    private static async Task<(ERank Rank, Sprite? Sprite)> Load(
        AssetLoader loader,
        RankBadgeSO source,
        ERank rank
    )
    {
        try
        {
            return (
                rank,
                await NativeGlobalAssetLoader.LoadByReferenceAsync<Sprite>(
                    loader,
                    source.GetRankBadge(rank)
                )
            );
        }
        catch
        {
            return (rank, null);
        }
    }
}
