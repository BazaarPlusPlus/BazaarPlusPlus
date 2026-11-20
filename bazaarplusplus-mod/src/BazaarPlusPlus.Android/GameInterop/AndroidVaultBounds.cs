using BazaarPlusPlus.Core.Ui;
using Il2CppTheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

// Project the framed platform because the coin pile changes size with the player's gold.
internal sealed class AndroidVaultBounds
{
    private Transform? _anchor;
    private BankToyController? _bank;
    private Renderer? _platform;

    public bool TryRead(float referenceHeight, out TouchRect vault)
    {
        vault = default;
        var anchor = Il2Cpp.BoardManager.Instance?.playerBankAnchor;
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

    // Collider.bounds is empty while the collider is disabled, so project its local corners
    // directly.
    private static bool Add(BoxCollider? box, Camera camera, ref Vector2 min, ref Vector2 max)
    {
        if (box == null || !box.gameObject.activeInHierarchy)
            return true;
        return Add(box.center, box.size / 2, box.transform, camera, ref min, ref max);
    }

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
