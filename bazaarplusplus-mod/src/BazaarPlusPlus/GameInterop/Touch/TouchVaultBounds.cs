#nullable enable
using BazaarPlusPlus.Core.Ui;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Touch;

/// <summary>
/// The vault's on-screen box, as the desktop reads the hero portrait's. The vault is the player's
/// bank, an equipped cosmetic (<see cref="BankToyController"/>) under
/// <c>BoardManager.playerBankAnchor</c>: board meshes, not canvas UI. Its framed platform
/// (<c>Bank_Platform_SM</c>) is projected through the board camera into the touch canvas; it
/// holds still while the coin pile on it grows and shrinks with the player's gold. A bank without
/// one falls back to its hover boxes, the pot's (<c>_bankCollider</c>) and the income and gold
/// plaques', which sit a few pixels up and left of the art.
/// </summary>
internal sealed class TouchVaultBounds
{
    private Transform? _anchor;
    private BankToyController? _bank;
    private Renderer? _platform;

    /// <summary>The vault in canvas units scaled to <paramref name="referenceHeight"/>.</summary>
    public bool TryRead(float referenceHeight, out TouchRect vault)
    {
        vault = default;
        var anchor = global::BoardManager.Instance?.playerBankAnchor;
        if (anchor == null || !anchor.gameObject.activeInHierarchy)
            return false;
        var camera = Camera.main;
        if (camera == null)
            return false;
        if (_anchor != anchor || _bank == null)
        {
            _anchor = anchor;
            _bank = anchor.GetComponentInChildren<BankToyController>();
            _platform = null;
            if (_bank != null)
                foreach (var renderer in _bank.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var name = renderer.gameObject.name;
                    // Its drop shadow, Platform_Shadow_SM, is a little larger than the frame.
                    if (
                        name.Contains("Platform", StringComparison.Ordinal)
                        && !name.Contains("Shadow", StringComparison.Ordinal)
                    )
                    {
                        _platform = renderer;
                        break;
                    }
                }
        }
        var bank = _bank;
        if (bank == null || !bank.gameObject.activeInHierarchy)
            return false;
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var platform = _platform;
        if (platform != null && platform.enabled && platform.gameObject.activeInHierarchy)
        {
            var bounds = platform.bounds;
            if (!Add(bounds.center, bounds.extents, null, camera, ref min, ref max))
                return false;
        }
        else if (
            !Add(bank._bankCollider, camera, ref min, ref max)
            || !Add(bank.incomeCollider, camera, ref min, ref max)
            || !Add(bank.goldCollider, camera, ref min, ref max)
        )
            return false;
        if (!float.IsFinite(min.x) || max.x <= min.x || max.y <= min.y)
            return false;
        // Screen pixels run up from the bottom; the canvas runs down from the top.
        var scale = referenceHeight / Screen.height;
        vault = new(
            min.x * scale,
            (Screen.height - max.y) * scale,
            (max.x - min.x) * scale,
            (max.y - min.y) * scale
        );
        return true;
    }

    /// <summary>
    /// Widens the screen box by <paramref name="box"/>'s corners; false when one is behind the
    /// camera. A missing or inactive box adds nothing. The corners are the box's own:
    /// <c>Collider.bounds</c> reads empty while the collider is disabled.
    /// </summary>
    private static bool Add(BoxCollider? box, Camera camera, ref Vector2 min, ref Vector2 max)
    {
        if (box == null || !box.gameObject.activeInHierarchy)
            return true;
        return Add(box.center, box.size / 2, box.transform, camera, ref min, ref max);
    }

    /// <summary>
    /// Widens the screen box by the corners of the box at <paramref name="center"/> with
    /// <paramref name="half"/> extents, in <paramref name="space"/>'s local frame or in world
    /// space when it is null; false when a corner is behind the camera.
    /// </summary>
    private static bool Add(
        Vector3 center,
        Vector3 half,
        Transform? space,
        Camera camera,
        ref Vector2 min,
        ref Vector2 max
    )
    {
        for (var i = 0; i < 8; i++)
        {
            var local =
                center
                + Vector3.Scale(
                    half,
                    new Vector3(
                        (i & 1) == 0 ? -1f : 1f,
                        (i & 2) == 0 ? -1f : 1f,
                        (i & 4) == 0 ? -1f : 1f
                    )
                );
            var point = camera.WorldToScreenPoint(
                space != null ? space.TransformPoint(local) : local
            );
            if (point.z <= 0f)
                return false;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return true;
    }
}
