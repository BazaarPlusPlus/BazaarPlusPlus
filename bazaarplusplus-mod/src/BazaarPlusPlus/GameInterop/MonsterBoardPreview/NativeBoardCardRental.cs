#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.GameInterop.AssetLoading;
using TheBazaar.AppFramework;
using TheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

// A rental is registered before any native Resize/SetUp work can fail.
internal sealed class NativeBoardCardRental : IDisposable
{
    private readonly RectTransform _prefabRect;
    private readonly AspectRatioFitter? _prefabAspect;
    private bool _disposed;
    private RectTransform? _framePrefabRect;
    internal CardPreviewBase Card { get; }
    internal bool Ready { get; private set; }

    internal NativeBoardCardRental(CardPreviewBase card, GameObject prefab)
    {
        Card = card;
        _prefabRect = prefab.GetComponent<RectTransform>();
        _prefabAspect = prefab.GetComponent<AspectRatioFitter>();
    }

    private void RestoreLayout()
    {
        var rect = Card.ParentRect;
        rect.anchorMin = _prefabRect.anchorMin;
        rect.anchorMax = _prefabRect.anchorMax;
        rect.pivot = _prefabRect.pivot;
        rect.sizeDelta = _prefabRect.sizeDelta;
        rect.anchoredPosition3D = _prefabRect.anchoredPosition3D;
        rect.localScale = _prefabRect.localScale;
        rect.localRotation = _prefabRect.localRotation;
        if (Card.TryGetComponent<AspectRatioFitter>(out var aspect) && _prefabAspect != null)
        {
            aspect.aspectMode = _prefabAspect.aspectMode;
            aspect.aspectRatio = _prefabAspect.aspectRatio;
            aspect.enabled = _prefabAspect.enabled;
        }
    }

    internal async Task Prepare(TCardBase template, TCardInstance instance)
    {
        RestoreLayout();
        // Native LoadFrame/LoadArt swallow exceptions. Clear their success signals first.
        ResetVisuals();
        Card.gameObject.SetActive(true);
        Card.Resize();
        await Card.SetUp(template, false, instance);
        if (
            Card._currentFrame == null
            || Card._currentFrame.transform.parent != Card._frameContainer
            || (
                Card is CardPreviewSkill
                    ? Card._cardImage.texture == null
                    : Card._cardMaterial == null
            )
        )
            throw new InvalidOperationException(
                $"Native preview assets are incomplete for {template.Id}."
            );
        if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
            throw new InvalidOperationException("Frame prefab loader is unavailable.");
        var framePrefab = await NativeGlobalAssetLoader.LoadByReferenceAsync<GameObject>(
            loader,
            Card._cardTierFrameSO.GetAssetReferenceByRarity(instance.Tier)
        );
        _framePrefabRect = framePrefab != null ? framePrefab.GetComponent<RectTransform>() : null;
        if (_framePrefabRect == null)
            throw new InvalidOperationException("Frame prefab layout is unavailable.");
        RestoreFrameLayout();
        Card.Resize();
        Ready = true;
    }

    private void ResetVisuals()
    {
        if (Card._currentFrame != null)
        {
            RestoreFrameLayout();
            Card._currentFrame.transform.SetParent(null, false);
            Card._currentFrame.PoolObject();
            Card._currentFrame = null;
        }
        Card._currentTier = null;
        Card._cardImage.texture = null;
        Card._cardImage.material = null;
        if (Card._cardMaterial != null)
            UnityEngine.Object.Destroy(Card._cardMaterial);
        Card._cardMaterial = null;
    }

    private void RestoreFrameLayout()
    {
        if (_framePrefabRect == null || Card._currentFrame == null)
            return;
        var rect = Card._currentFrame.GetComponent<RectTransform>();
        if (rect == null)
            return;
        rect.anchorMin = _framePrefabRect.anchorMin;
        rect.anchorMax = _framePrefabRect.anchorMax;
        rect.pivot = _framePrefabRect.pivot;
        rect.localRotation = _framePrefabRect.localRotation;
        rect.localScale = Vector3.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Card == null)
            return;
        Card.transform.SetParent(null, false);
        ResetVisuals();
        RestoreLayout();
        if (Ready)
            Card.gameObject.PoolObject();
        else
            UnityEngine.Object.Destroy(Card.gameObject);
    }
}
