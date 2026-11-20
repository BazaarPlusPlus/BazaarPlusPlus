using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using Il2Cpp;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar.AppFramework;
using Il2CppTheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop.CardRental;

// Use Global asset scope so scenes do not own rented cards. A card's parent is a position anchor,
// so the card must keep the prefab's own dimensions.
internal sealed class AndroidCardRentalBackend : ICardRentalBackend<CardPreviewBase, RectTransform>
{
    private readonly Dictionary<Guid, TCardBase> _templates = new();
    private readonly HashSet<int> _fittedSkillMasks = new();
    private Transform? _poolRoot;
    private long _instanceSerial;

    public void Remember(Guid templateId, TCardBase template) => _templates[templateId] = template;

    public string? ResolveKind(CardRentalSubject subject, out string? failureReason)
    {
        var template = Template(subject.TemplateId);
        if (template == null)
        {
            failureReason = "template_missing";
            return null;
        }
        failureReason = null;
        var kind = template.Type switch
        {
            ECardType.Skill => "skill",
            ECardType.Item => AndroidNativeCardSetUp.ShownSize(template, subject) switch
            {
                ECardSize.Small => "small",
                ECardSize.Medium => "medium",
                ECardSize.Large => "large",
                _ => null,
            },
            _ => null,
        };
        if (kind == null)
            failureReason = "unsupported_card";
        return kind;
    }

    public object BeginInstantiate(CardRentalSubject subject, string kind)
    {
        if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
            throw new InvalidOperationException("asset loader missing");
        var reference = kind switch
        {
            "skill" => loader.SkillUIAssetRef,
            "small" => loader.SmallCardUIAssetRef,
            "medium" => loader.MediumCardUIAssetRef,
            "large" => loader.LargeCardUIAssetRef,
            _ => null,
        };
        if (reference == null)
            throw new InvalidOperationException("card reference missing");
        return loader.InstantiateAssetAsyncByReference(
            reference,
            new Il2CppSystem.Nullable<AssetScope>(AssetScope.Global)
        );
    }

    public bool TryCompleteInstantiate(
        object operation,
        out CardPreviewBase? card,
        out string? failureReason
    )
    {
        card = null;
        failureReason = null;
        var task = (Il2CppSystem.Threading.Tasks.Task<GameObject>)operation;
        if (!task.IsCompleted)
            return false;
        if (task.IsFaulted || task.IsCanceled)
        {
            failureReason = task.IsCanceled ? "instantiate_canceled" : "instantiate_faulted";
            return true;
        }
        var root = task.Result;
        if (root == null)
        {
            failureReason = "instantiate_empty";
            return true;
        }
        root.SetActive(false);
        card = root.GetComponent<CardPreviewBase>();
        if (card == null)
        {
            UnityEngine.Object.Destroy(root);
            failureReason = "component_missing";
            return true;
        }
        root.transform.SetParent(PoolRoot(), false);
        // The parent canvas can raycast the card's graphics even with the prefab's raycaster
        // disabled.
        var group = Group(card);
        group.blocksRaycasts = false;
        group.interactable = false;
        foreach (var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))
            raycaster.enabled = false;
        return true;
    }

    private static CanvasGroup Group(CardPreviewBase card)
    {
        var group = card.GetComponent<CanvasGroup>();
        return group != null ? group : card.gameObject.AddComponent<CanvasGroup>();
    }

    public object BeginSetUp(CardPreviewBase card, CardRentalSubject subject, RectTransform parent)
    {
        var template =
            Template(subject.TemplateId) ?? throw new InvalidOperationException("template missing");
        var transform = card.transform;
        if (transform.parent == null || transform.parent.Pointer != parent.Pointer)
            Group(card).alpha = 0f;
        transform.SetParent(parent, false);
        card._showTooltips = false;
        transform.localScale = Vector3.one;
        transform.localRotation = Quaternion.identity;
        card.gameObject.SetActive(true);
        var instance = AndroidNativeCardSetUp.Instance(
            template,
            subject,
            "bpp-rental-" + ++_instanceSerial
        );
        var kept = AndroidNativeCardSetUp.Keep(instance);
        var boardItem = subject.OnBoard && template.Type == ECardType.Item;
        if (boardItem)
            _ = AndroidCardAmmoBar.Settled;
        return new SetUpOperation(
            card.SetUp(template, false, instance, Il2CppSystem.Threading.CancellationToken.None),
            kept,
            AndroidNativeCardSetUp.TinySize(template, subject),
            boardItem
        );
    }

    public bool TryCompleteSetUp(object operation, CardPreviewBase card, out string? failureReason)
    {
        failureReason = null;
        var setUp = (SetUpOperation)operation;
        var task = setUp.Task;
        if (!task.IsCompleted)
            return false;
        if (task.IsFaulted || task.IsCanceled)
        {
            failureReason = task.IsCanceled ? "setup_canceled" : "setup_faulted";
            return true;
        }
        if (setUp.BoardItem && !AndroidCardAmmoBar.Settled)
            return false;
        setUp.Kept.ShowOn(card);
        if (setUp.TinySize is { } tinySize)
            AndroidNativeCardSetUp.ShowTiny(card, tinySize);
        card.Show(true);
        card.Resize();
        Place(card);
        FitSkillMask(card);
        AndroidCardAmmoBar.Show(card, setUp.BoardItem);
        Group(card).alpha = 1f;
        return true;
    }

    private sealed record SetUpOperation(
        Il2CppSystem.Threading.Tasks.Task Task,
        AndroidNativeCardSetUp.Kept Kept,
        ECardSize? TinySize,
        bool BoardItem
    );

    public void Park(CardPreviewBase card)
    {
        card.gameObject.SetActive(false);
        card.transform.SetParent(PoolRoot(), false);
    }

    public void Detach(CardPreviewBase card)
    {
        Group(card).alpha = 0f;
        card.transform.SetParent(PoolRoot(), false);
    }

    public void Destroy(CardPreviewBase card)
    {
        _fittedSkillMasks.Remove(card.GetInstanceID());
        UnityEngine.Object.Destroy(card.gameObject);
    }

    private void FitSkillMask(CardPreviewBase card)
    {
        if (
            card.TryCast<CardPreviewSkill>() == null
            || !_fittedSkillMasks.Add(card.GetInstanceID())
        )
            return;
        var image = card._cardImage;
        foreach (var mask in card.GetComponentsInChildren<Mask>(true))
        {
            if (
                !image.transform.IsChildOf(mask.transform)
                || card._frameContainer.IsChildOf(mask.transform)
            )
                continue;
            // The Android prefab's artwork extends past the rim. Mask it without scaling the
            // artwork.
            const float apertureScale = 0.85f;
            var artSize = image.rectTransform.rect.size;
            var rect = mask.graphic.rectTransform;
            rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                rect.rect.width * apertureScale
            );
            rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                rect.rect.height * apertureScale
            );
            image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, artSize.x);
            image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, artSize.y);
            mask.showMaskGraphic = false;
        }
    }

    public bool IsAlive(CardPreviewBase card) => card != null && card.gameObject != null;

    private TCardBase? Template(Guid templateId)
    {
        if (_templates.TryGetValue(templateId, out var cached))
            return cached;
        if (!Il2CppTheBazaar.Data.IsManagerCreated())
            return null;
        var found = Il2CppTheBazaar
            .Data.GetStatic()
            ?.GetCardById(Il2CppSystem.Guid.Parse(templateId.ToString()))
            ?.TryCast<TCardBase>();
        if (found != null)
            _templates[templateId] = found;
        return found;
    }

    private static void Place(CardPreviewBase card)
    {
        var rect = card.GetComponent<RectTransform>();
        if (rect == null)
            return;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        var fitter = card.GetComponent<AspectRatioFitter>();
        if (fitter != null && fitter.aspectRatio > 0.01f)
        {
            rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                NativeCardPrefab.Height * fitter.aspectRatio
            );
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, NativeCardPrefab.Height);
            // Read for its ratio only: left on, it would fit the card to its parent, and a
            // renting panel's parent is a zero-size slot.
            fitter.enabled = false;
        }
    }

    private Transform PoolRoot()
    {
        if (_poolRoot != null)
            return _poolRoot;
        var root = new GameObject("BPP.CardRentalPool");
        root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(root);
        _poolRoot = root.transform;
        return _poolRoot;
    }
}
