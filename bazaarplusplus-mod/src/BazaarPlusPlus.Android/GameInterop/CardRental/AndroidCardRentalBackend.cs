using BazaarPlusPlus.Core.Cards;
using Il2Cpp;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Cards.Skill;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar.AppFramework;
using Il2CppTheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop.CardRental;

/// <summary>
/// The IL2CPP half of the rental, as the #396 spike measured it on device: the Small, Medium,
/// Large or Skill card UI prefab is instantiated through the game's <see cref="AssetLoader"/> in
/// the Global asset scope (so a scene's Current scope never owns it), set up with a synthetic
/// card instance at the subject's tier and enchantment, shown, resized, and destroyed with
/// <c>Object.Destroy</c>. Native tasks are polled, never awaited.
/// <para>
/// A ready card's RectTransform is centred on its parent's top-left corner and keeps the prefab's
/// own size: 484 high, with an item as wide as its aspect ratio makes it (252, 503 or 755) and a
/// skill square. The renting panel positions and scales the parent, so a card set up again in
/// place, or reused from the pool, never shows anywhere else.
/// </para>
/// </summary>
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
            ECardType.Item => template.Size switch
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
        // A rented card is a picture: the panel above it takes the taps. The card's own canvas
        // does not override sorting, so the renting canvas raycasts its graphics; the group
        // turns them off there, and any raycaster inside the prefab is switched off too.
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
        // A card that moves to a new parent stays invisible until its setup has sized and
        // placed it; a retarget in place keeps showing the previous tier meanwhile.
        if (transform.parent == null || transform.parent.Pointer != parent.Pointer)
            Group(card).alpha = 0f;
        transform.SetParent(parent, false);
        card._showTooltips = false;
        transform.localScale = Vector3.one;
        transform.localRotation = Quaternion.identity;
        card.gameObject.SetActive(true);
        return card.SetUp(
            template,
            false,
            BuildInstance(template, subject),
            Il2CppSystem.Threading.CancellationToken.None
        );
    }

    public bool TryCompleteSetUp(object operation, CardPreviewBase card, out string? failureReason)
    {
        failureReason = null;
        var task = (Il2CppSystem.Threading.Tasks.Task)operation;
        if (!task.IsCompleted)
            return false;
        if (task.IsFaulted || task.IsCanceled)
        {
            failureReason = task.IsCanceled ? "setup_canceled" : "setup_faulted";
            return true;
        }
        card.Show(true);
        card.Resize();
        Place(card);
        FitSkillMask(card);
        Group(card).alpha = 1f;
        return true;
    }

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
            // The Android prefab's circle extends beyond its decorative frame. Clip beneath
            // the metal rim with its own circular stencil; keep the artwork's size unchanged.
            // Applied once per native instance, not on every pooled rebind.
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

    private TCardInstance BuildInstance(TCardBase template, CardRentalSubject subject)
    {
        var tier = AndroidCardFacets.Tier.TryToSource(subject.Tier, out var nativeTier)
            ? nativeTier
            : template.StartingTier;
        var instanceId = "bpp-rental-" + ++_instanceSerial;
        var attributes = new Il2CppSystem.Collections.Generic.Dictionary<ECardAttributeType, int>();
        if (template.Type == ECardType.Skill)
        {
            return new TCardInstanceSkill
            {
                TemplateId = template.Id,
                TemplateVersion = string.Empty,
                InstanceId = instanceId,
                Tier = tier,
                Attributes = attributes,
            };
        }
        var item = new TCardInstanceItem
        {
            TemplateId = template.Id,
            TemplateVersion = string.Empty,
            InstanceId = instanceId,
            Tier = tier,
            Attributes = attributes,
        };
        if (
            subject.Enchantment is { } enchantment
            && AndroidCardFacets.Enchantment.TryToSource(enchantment, out var nativeEnchantment)
        )
            item.EnchantmentType = new Il2CppSystem.Nullable<EEnchantmentType>(nativeEnchantment);
        return item;
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
