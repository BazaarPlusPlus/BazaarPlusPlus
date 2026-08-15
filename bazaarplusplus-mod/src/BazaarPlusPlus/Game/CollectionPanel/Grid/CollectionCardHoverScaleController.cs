#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Collection-only hover scale. The virtualizer drives this controller so pooled cards do not
// each run their own Update loop. The base scale comes from NativeCardCellFitter and is preserved
// while the hover multiplier animates around the centered card pivot.
internal sealed class CollectionCardHoverScaleController : MonoBehaviour
{
    private static Sprite? _shadowSprite;

    private float _baseScaleX = 1f;
    private float _baseScaleY = 1f;
    private float _currentMultiplier = 1f;
    private float _targetMultiplier = 1f;
    private float _currentShadowAlpha;
    private float _targetShadowAlpha;
    private Image? _shadowImage;
    private RectTransform? _shadowRect;
    private bool _needsTick;

    internal void SetBaseScale(float baseScaleX, float baseScaleY)
    {
        _baseScaleX = Mathf.Max(0.001f, baseScaleX);
        _baseScaleY = Mathf.Max(0.001f, baseScaleY);
        EnsureShadow();
        UpdateShadowLayout();
        ApplyScale();
    }

    internal void SetHovered(bool hovered)
    {
        var targetMultiplier = hovered ? CollectionGridConstants.CardHoverScale : 1f;
        var targetShadowAlpha = hovered ? CollectionGridConstants.CardHoverShadowAlpha : 0f;
        if (
            Mathf.Approximately(_targetMultiplier, targetMultiplier)
            && Mathf.Approximately(_targetShadowAlpha, targetShadowAlpha)
        )
            return;

        _targetMultiplier = targetMultiplier;
        _targetShadowAlpha = targetShadowAlpha;
        EnsureShadow();
        _needsTick = true;
    }

    internal void Tick(float deltaSeconds)
    {
        if (!_needsTick || deltaSeconds <= 0f)
            return;

        var scaleSeconds =
            _targetMultiplier > 1f
                ? CollectionGridConstants.CardHoverScaleInSeconds
                : CollectionGridConstants.CardHoverScaleOutSeconds;
        var scaleAmount = 1f - Mathf.Exp(-deltaSeconds / scaleSeconds);
        _currentMultiplier = Mathf.Lerp(_currentMultiplier, _targetMultiplier, scaleAmount);

        var shadowSeconds =
            _targetShadowAlpha > 0f
                ? CollectionGridConstants.CardHoverShadowInSeconds
                : CollectionGridConstants.CardHoverShadowOutSeconds;
        var shadowAmount = 1f - Mathf.Exp(-deltaSeconds / shadowSeconds);
        _currentShadowAlpha = Mathf.Lerp(_currentShadowAlpha, _targetShadowAlpha, shadowAmount);

        var scaleSettled = Mathf.Abs(_currentMultiplier - _targetMultiplier) <= 0.001f;
        var shadowSettled = Mathf.Abs(_currentShadowAlpha - _targetShadowAlpha) <= 0.001f;
        if (scaleSettled)
        {
            _currentMultiplier = _targetMultiplier;
        }
        if (shadowSettled)
            _currentShadowAlpha = _targetShadowAlpha;
        _needsTick = !scaleSettled || !shadowSettled;
        ApplyScale();
        ApplyShadow();
    }

    internal void ResetImmediate()
    {
        _currentMultiplier = 1f;
        _targetMultiplier = 1f;
        _currentShadowAlpha = 0f;
        _targetShadowAlpha = 0f;
        _needsTick = false;
        ApplyScale();
        ApplyShadow();
    }

    private void ApplyScale() =>
        transform.localScale = new Vector3(
            _baseScaleX * _currentMultiplier,
            _baseScaleY * _currentMultiplier,
            1f
        );

    private void EnsureShadow()
    {
        if (_shadowImage != null)
            return;

        var shadowObject = new GameObject("CollectionCardHoverShadow", typeof(RectTransform));
        shadowObject.transform.SetParent(transform, worldPositionStays: false);
        _shadowImage = shadowObject.AddComponent<Image>();
        _shadowImage.sprite = GetShadowSprite();
        _shadowImage.type = Image.Type.Sliced;
        _shadowImage.raycastTarget = false;
        _shadowRect = _shadowImage.rectTransform;
        _shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
        _shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
        _shadowRect.pivot = new Vector2(0.5f, 0.5f);
        _shadowRect.localScale = Vector3.one;
        _shadowRect.SetAsFirstSibling();
        ApplyShadow();
    }

    private void UpdateShadowLayout()
    {
        if (_shadowRect == null || transform is not RectTransform root)
            return;

        var size = root.rect.size;
        var padding = CollectionGridConstants.CardHoverShadowPadding;
        _shadowRect.sizeDelta = size + new Vector2(2f * padding, 2f * padding);
        _shadowRect.anchoredPosition = new Vector2(
            0f,
            CollectionGridConstants.CardHoverShadowOffsetY
        );
    }

    private void ApplyShadow()
    {
        if (_shadowImage == null)
            return;

        _shadowImage.color = new Color(0f, 0f, 0f, _currentShadowAlpha);
        _shadowImage.gameObject.SetActive(
            _currentShadowAlpha > 0.001f || _targetShadowAlpha > 0.001f
        );
    }

    private static Sprite GetShadowSprite()
    {
        if (_shadowSprite != null)
            return _shadowSprite;

        const int size = 64;
        const int padding = 12;
        const int radius = 14;
        const float blur = 9f;
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var halfExtent = new Vector2(
            size * 0.5f - padding - radius,
            size * 0.5f - padding - radius
        );
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var point = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f);
            var q = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y)) - halfExtent;
            var outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            var distance = outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            var normalized = Mathf.InverseLerp(-blur, blur, distance);
            var alpha = 1f - Mathf.SmoothStep(0f, 1f, normalized);
            texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        texture.Apply();
        _shadowSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(padding + radius, padding + radius, padding + radius, padding + radius)
        );
        return _shadowSprite;
    }
}
