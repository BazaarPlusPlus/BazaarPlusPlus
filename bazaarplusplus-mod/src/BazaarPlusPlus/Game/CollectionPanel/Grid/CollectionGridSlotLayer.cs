#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Display-case slot visuals drawn behind the native cards on the overlay board: a weak
// translucent rounded rect per visible cell (so the fixed grid order reads even before art
// loads) plus a single brighter hover highlight that glows around the pointed cell.
//
// Purely decorative. Every Image has raycastTarget = false — re-enabling it would let the
// overlay swallow the wheel/click events UITK relies on, which was the P1/P2 regression in the
// design log (§16.6). The layer is the board's first child so the pooled cards (later siblings)
// always render on top of the slots.
internal sealed class CollectionGridSlotLayer
{
    private static Sprite? _roundedSprite;

    private readonly RectTransform _layerRoot;
    private readonly List<Image> _slots = new();
    private readonly Image _hover;
    private int _activeSlotCount;
    private bool _hoverActive;

    public CollectionGridSlotLayer(RectTransform board)
    {
        var go = new GameObject("CollectionGridSlotLayer", typeof(RectTransform));
        go.transform.SetParent(board, worldPositionStays: false);
        _layerRoot = go.GetComponent<RectTransform>();
        _layerRoot.anchorMin = new Vector2(0f, 1f);
        _layerRoot.anchorMax = new Vector2(0f, 1f);
        _layerRoot.pivot = new Vector2(0f, 1f);
        _layerRoot.anchoredPosition = Vector2.zero;
        _layerRoot.sizeDelta = Vector2.zero;
        _layerRoot.localScale = Vector3.one;
        _layerRoot.SetAsFirstSibling();

        _hover = CreateImage("CollectionGridHover", Colors.CollectionSlotHover);
    }

    // Position one slot per visible cell rect (board-local, top-left origin, y down). Extra
    // pooled slots from a previous, larger window are deactivated.
    public void Sync(IReadOnlyList<CollectionGridRect> rects)
    {
        var poolBefore = _slots.Count;
        EnsureCapacity(rects.Count);
        for (var i = 0; i < rects.Count; i++)
        {
            var img = _slots[i];
            if (!img.gameObject.activeSelf)
                img.gameObject.SetActive(true);
            Place(img.rectTransform, rects[i]);
        }
        for (var i = rects.Count; i < _activeSlotCount; i++)
            _slots[i].gameObject.SetActive(false);
        _activeSlotCount = rects.Count;

        // Newly grown pool slots are appended after the hover highlight in the sibling list;
        // re-assert the highlight on top of the fills (still behind the cards) while it shows.
        if (_hoverActive && _slots.Count > poolBefore)
            _hover.transform.SetAsLastSibling();
    }

    public void SetHover(CollectionGridRect? rect)
    {
        if (rect == null)
        {
            if (_hoverActive)
            {
                _hover.gameObject.SetActive(false);
                _hoverActive = false;
            }
            return;
        }
        if (!_hoverActive)
        {
            _hover.gameObject.SetActive(true);
            // Keep the highlight above the slot fills (still behind the cards, which are the
            // board's later siblings). Re-asserted only on activation, not every frame.
            _hover.transform.SetAsLastSibling();
            _hoverActive = true;
        }
        Place(_hover.rectTransform, rect.Value);
    }

    public void Clear()
    {
        for (var i = 0; i < _activeSlotCount; i++)
            _slots[i].gameObject.SetActive(false);
        _activeSlotCount = 0;
        if (_hoverActive)
        {
            _hover.gameObject.SetActive(false);
            _hoverActive = false;
        }
    }

    private void EnsureCapacity(int count)
    {
        while (_slots.Count < count)
            _slots.Add(
                CreateImage($"CollectionGridSlot{_slots.Count}", Colors.CollectionSlotBackground)
            );
    }

    private Image CreateImage(string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_layerRoot, worldPositionStays: false);
        go.SetActive(false);
        var img = go.AddComponent<Image>();
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.localScale = Vector3.one;
        return img;
    }

    private static void Place(RectTransform rt, CollectionGridRect rect)
    {
        rt.anchoredPosition = new Vector2(rect.X, -rect.Y);
        rt.sizeDelta = new Vector2(rect.Width, rect.Height);
    }

    // Self-contained rounded-rect sprite (9-sliced) so slots/hover have soft corners without a
    // game-asset dependency. Mirrors CombatStatusBar.GetRoundedSprite.
    private static Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null)
            return _roundedSprite;

        const int size = 32;
        const int radius = 12;
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var alpha = IsInsideRoundedRect(x, y, size, radius) ? 1f : 0f;
            texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        texture.Apply();

        _roundedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius)
        );
        return _roundedSprite;
    }

    private static bool IsInsideRoundedRect(int x, int y, int size, int radius)
    {
        var clampedX = Mathf.Clamp(x, radius, size - radius - 1);
        var clampedY = Mathf.Clamp(y, radius, size - radius - 1);
        var deltaX = x - clampedX;
        var deltaY = y - clampedY;
        return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
    }
}
