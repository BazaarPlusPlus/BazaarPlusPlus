#nullable enable
using BazaarPlusPlus.GameInterop.AssetLoading;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Ranks;

/// <summary>A finished run's trophy, from the lowest tier to the highest.</summary>
internal enum NativeTrophy
{
    Bronze,
    Silver,
    Gold,
    Diamond,
}

/// <summary>
/// The game's four trophy sprites. Call <see cref="Tick"/> every frame the view shows; it
/// loads what is missing, tries again every five seconds, and says when a sprite arrived.
/// </summary>
internal sealed class NativeTrophyProvider
{
    private static readonly string[] Addresses =
    [
        "Trophy_Bronze_TUI",
        "Trophy_Silver_TUI",
        "Trophy_Gold_TUI",
        "Trophy_Diamond_TUI",
    ];
    private readonly Sprite?[] _sprites = new Sprite?[Addresses.Length];
    private Task<Sprite?[]>? _load;
    private float _retryAt;

    internal Sprite? Get(NativeTrophy trophy) => _sprites[(int)trophy];

    internal bool Tick()
    {
        var changed = false;
        if (_load?.IsCompleted == true)
        {
            if (_load.Status == TaskStatus.RanToCompletion)
                for (var i = 0; i < _sprites.Length; i++)
                {
                    var loaded = _load.Result[i];
                    if (loaded == null || loaded == _sprites[i])
                        continue;
                    _sprites[i] = loaded;
                    changed = true;
                }
            else
                _ = _load.Exception;
            _load = null;
        }
        if (
            _load != null
            || Time.unscaledTime < _retryAt
            || _sprites.All(sprite => sprite != null)
            || !Services.TryGet<AssetLoader>(out var loader)
            || loader == null
        )
            return changed;
        _retryAt = Time.unscaledTime + 5;
        _load = Task.WhenAll(
            Addresses.Select(address =>
                NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(loader, address)
            )
        );
        return changed;
    }
}
