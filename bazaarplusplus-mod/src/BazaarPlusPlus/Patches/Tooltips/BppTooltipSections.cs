#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.UI.Tooltips;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Patches.Tooltips;

// Manages BPP-owned text sections cloned into the pooled native tooltip. Each section
// is a clone of the tooltip's passive-text block, keyed per controller + purpose,
// inserted after a caller-supplied anchor sibling. Native typography is preserved;
// BPP-authored CJK content receives the game's own zh-CN fallback chain.
internal static class BppTooltipSections
{
    // Clearly below the native body size so appended blocks read as secondary info.
    private const float DefaultFontScale = 0.75f;

    internal sealed class Section
    {
        public GameObject Block = null!;
        public CardEffectTooltipController Text = null!;
        public GameObject? Divider;
        public UnityEngine.UI.LayoutGroup? SourceLayout;
        public int SourceBottomPadding;
        public UnityEngine.UI.LayoutGroup? QuestGroupLayout;
        public int QuestGroupBottomPadding;
    }

    internal sealed class Style
    {
        public bool ClearTopPadding { get; init; } = true;
        public int? BottomPadding { get; init; }
        public bool MirrorSourceTopPaddingToBottom { get; init; }
        public float? SectionTopPaddingScale { get; init; }
        public float? SectionBottomPaddingScale { get; init; }
        public float? NativeSectionBottomPaddingScale { get; init; }
        public float? SourceBottomPaddingScale { get; init; }
        public float? QuestGroupBottomPaddingScale { get; init; }
        public float? ParagraphSpacing { get; init; }
        public float FontScale { get; init; } = DefaultFontScale;
        public bool ShowNativeDivider { get; init; }
        public float DividerHorizontalInset { get; init; }
    }

    // Structured rich-text sections own their paragraph and list rhythm through
    // explicit font-relative markup. Disable the cloned native label's additional
    // paragraph spacing so every newline is counted exactly once.
    internal static readonly Style MarkupControlledStyle = new() { ParagraphSpacing = 0f };

    private static readonly Dictionary<(CardTooltipController, string), Section> Sections = new();

    public static bool TryShow(
        CardTooltipController controller,
        string key,
        GameObject? anchor,
        string content,
        Style? style = null
    )
    {
        if (anchor == null)
            return false;

        var section = Ensure(controller, key, anchor, style);
        if (section == null)
            return false;

        ApplyHostPadding(section, style);
        if (
            NativeGameTypography.EnsureNativeTextCoverage(section.Text.textObject, content)
            is not (NativeGameTypography.Outcome.Applied or NativeGameTypography.Outcome.NotNeeded)
        )
        {
            Hide(controller, key);
            return false;
        }
        section.Text.SetText(content);
        var siblingIndex = anchor.transform.GetSiblingIndex() + 1;
        if (section.Divider != null)
        {
            section.Divider.transform.SetSiblingIndex(siblingIndex);
            section.Divider.SetActive(true);
            siblingIndex++;
        }
        section.Block.transform.SetSiblingIndex(siblingIndex);
        section.Block.SetActive(true);
        return true;
    }

    public static void Hide(CardTooltipController controller, string key)
    {
        if (!Sections.TryGetValue((controller, key), out var section))
            return;
        if (section.Block == null)
        {
            if (section.Divider != null)
                Object.Destroy(section.Divider);
            Sections.Remove((controller, key));
            return;
        }
        if (section.Divider != null)
            section.Divider.SetActive(false);
        RestoreHostPadding(section);
        section.Block.SetActive(false);
    }

    internal static void HideAll(string key)
    {
        List<CardTooltipController>? owners = null;
        foreach (var entry in Sections)
        {
            if (!string.Equals(entry.Key.Item2, key, System.StringComparison.Ordinal))
                continue;

            owners ??= new List<CardTooltipController>();
            owners.Add(entry.Key.Item1);
        }

        if (owners == null)
            return;
        foreach (var owner in owners)
            Hide(owner, key);
    }

    public static void ReleaseAll(CardTooltipController controller)
    {
        List<(CardTooltipController, string)>? owned = null;
        foreach (var entry in Sections)
        {
            if (!ReferenceEquals(entry.Key.Item1, controller))
                continue;
            owned ??= new List<(CardTooltipController, string)>();
            owned.Add(entry.Key);
            RestoreHostPadding(entry.Value);
            if (entry.Value.Divider != null)
                Object.Destroy(entry.Value.Divider);
            if (entry.Value.Block != null)
                Object.Destroy(entry.Value.Block);
        }

        if (owned == null)
            return;
        foreach (var key in owned)
            Sections.Remove(key);
    }

    private static Section? Ensure(
        CardTooltipController controller,
        string key,
        GameObject anchor,
        Style? style
    )
    {
        if (Sections.TryGetValue((controller, key), out var existing))
        {
            if (existing.Block != null && existing.Text != null)
                return existing;
            Sections.Remove((controller, key));
        }

        var passiveBlock = controller.passiveEffectParent;
        if (passiveBlock == null)
        {
            ReportHostDegraded(key, TooltipLogReasonCode.PassiveEffectParentUnavailable);
            return null;
        }

        var blockClone = Object.Instantiate(passiveBlock, anchor.transform.parent);
        blockClone.name = $"BppTooltipSection_{key}";
        var sourceLayout = anchor.GetComponent<UnityEngine.UI.LayoutGroup>();
        var sourceBottomPadding = sourceLayout?.padding.bottom ?? 0;
        // Quest rows sit inside the source block but carry their own trailing padding.
        // Cache both baselines so a quest-only section can trim the stacked gap and
        // pooled tooltips still restore the native layout when the section is hidden.
        var questGroupLayout =
            style?.QuestGroupBottomPaddingScale == null
                ? null
                : controller.QuestGroupParent?.GetComponent<UnityEngine.UI.LayoutGroup>();
        var questGroupBottomPadding = questGroupLayout?.padding.bottom ?? 0;

        GameObject? dividerClone = null;
        if (style?.ShowNativeDivider == true && controller.dividerParent != null)
        {
            dividerClone = Object.Instantiate(controller.dividerParent, anchor.transform.parent);
            dividerClone.name = $"BppTooltipSectionDivider_{key}";
            foreach (var group in dividerClone.GetComponentsInChildren<CanvasGroup>(true))
                group.alpha = 1f;
            var dividerRect = dividerClone.GetComponent<RectTransform>();
            var dividerImages = dividerClone.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach (var image in dividerImages)
            {
                image.enabled = true;
                var color = image.color;
                color.a = 1f;
                image.color = color;
                image.canvasRenderer.SetAlpha(1f);
                if (dividerRect != null && style.DividerHorizontalInset > 0f)
                    InsetDividerImage(image, dividerRect, style.DividerHorizontalInset);
            }
            var dividerLayout =
                dividerClone.GetComponent<UnityEngine.UI.LayoutElement>()
                ?? dividerClone.AddComponent<UnityEngine.UI.LayoutElement>();
            dividerLayout.ignoreLayout = false;
            if (dividerRect != null && dividerLayout.preferredHeight < 0f)
                dividerLayout.preferredHeight = dividerRect.rect.height;
        }

        var textController = blockClone.GetComponentInChildren<CardEffectTooltipController>(
            includeInactive: true
        );
        if (textController == null)
        {
            ReportHostDegraded(key, TooltipLogReasonCode.TextControllerUnavailable);
            if (dividerClone != null)
                Object.Destroy(dividerClone);
            Object.Destroy(blockClone);
            return null;
        }

        // The source block's visibility state (animated alpha) is cloned as-is; force
        // the clone fully opaque so SetActive toggling is the only visibility gate.
        foreach (var group in blockClone.GetComponentsInChildren<CanvasGroup>(true))
            group.alpha = 1f;

        // Only the text participates: the clone inherits the passive box's other
        // children and the quest-row container among them stays active, silently
        // padding the section with dead height.
        var textTransform = textController.transform;
        foreach (Transform child in blockClone.transform)
            if (child != textTransform && !textTransform.IsChildOf(child))
                child.gameObject.SetActive(false);

        // The seam to the block above already carries the native box's bottom
        // padding and the layout spacing; the clone's own top padding doubles it up.
        if (blockClone.GetComponent<UnityEngine.UI.LayoutGroup>() is { } layoutGroup)
        {
            var sourceTopPadding = layoutGroup.padding.top;
            var nativeBottomPadding = layoutGroup.padding.bottom;
            if (style?.SectionTopPaddingScale is { } topScale)
                layoutGroup.padding.top = Mathf.RoundToInt(sourceTopPadding * topScale);
            else if (style?.ClearTopPadding ?? true)
                layoutGroup.padding.top = 0;
            if (style?.NativeSectionBottomPaddingScale is { } nativeBottomScale)
                layoutGroup.padding.bottom = Mathf.RoundToInt(
                    nativeBottomPadding * nativeBottomScale
                );
            else if (style?.SectionBottomPaddingScale is { } bottomScale)
                layoutGroup.padding.bottom = Mathf.RoundToInt(sourceTopPadding * bottomScale);
            else if (style?.MirrorSourceTopPaddingToBottom == true)
                layoutGroup.padding.bottom = sourceTopPadding;
            else if (style?.BottomPadding is { } bottomPadding)
                layoutGroup.padding.bottom = bottomPadding;
        }

        // The source block's LayoutElement.ignoreLayout is toggled together with its
        // visibility; a clone taken while the source was hidden (e.g. the hero-level
        // tooltip, where the passive box is off) inherits ignore=true, so the tooltip's
        // vertical layout/fitter reserves no space and the frame fails to grow around
        // the section. Always participate in layout.
        var rootLayoutElement =
            blockClone.GetComponent<UnityEngine.UI.LayoutElement>()
            ?? blockClone.AddComponent<UnityEngine.UI.LayoutElement>();
        rootLayoutElement.ignoreLayout = false;

        var label = textController.textObject;
        if (label != null)
        {
            if (style?.ParagraphSpacing is { } paragraphSpacing)
                label.paragraphSpacing = paragraphSpacing;
            if (label.enableAutoSizing)
            {
                label.fontSizeMax *= style?.FontScale ?? DefaultFontScale;
                label.fontSizeMin = Mathf.Min(label.fontSizeMin, label.fontSizeMax);
            }
            else
            {
                label.fontSize *= style?.FontScale ?? DefaultFontScale;
            }
        }

        var section = new Section
        {
            Block = blockClone,
            Text = textController,
            Divider = dividerClone,
            SourceLayout = sourceLayout,
            SourceBottomPadding = sourceBottomPadding,
            QuestGroupLayout = questGroupLayout,
            QuestGroupBottomPadding = questGroupBottomPadding,
        };
        Sections[(controller, key)] = section;
        return section;
    }

    private static void ReportHostDegraded(string key, TooltipLogReasonCode reasonCode) =>
        BppLog.WarnEvent(
            TooltipLogEvents.SectionHostDegraded,
            TooltipLogEvents.SectionHostDegradedSectionId.Bind(ResolveSectionId(key)),
            TooltipLogEvents.SectionHostDegradedReasonCode.Bind(reasonCode)
        );

    private static TooltipSectionId ResolveSectionId(string key) =>
        key switch
        {
            "enchant-preview-with-native" => TooltipSectionId.EnchantPreview,
            "enchant-preview-after-quest" => TooltipSectionId.EnchantPreview,
            "enchant-preview-without-native" => TooltipSectionId.EnchantPreview,
            "quest-reward-preview" => TooltipSectionId.QuestRewardPreview,
            "aggregate-missing-types" => TooltipSectionId.AggregateMissingTypes,
            "encounter" => TooltipSectionId.EncounterPreview,
            "level-rewards" => TooltipSectionId.HeroLevelRewards,
            _ => TooltipSectionId.Unknown,
        };

    private static void ApplyHostPadding(Section section, Style? style)
    {
        if (section.SourceLayout != null && style?.SourceBottomPaddingScale is { } sourceScale)
            section.SourceLayout.padding.bottom = Mathf.RoundToInt(
                section.SourceBottomPadding * sourceScale
            );
        if (
            section.QuestGroupLayout != null
            && style?.QuestGroupBottomPaddingScale is { } questScale
        )
            section.QuestGroupLayout.padding.bottom = Mathf.RoundToInt(
                section.QuestGroupBottomPadding * questScale
            );
    }

    private static void RestoreHostPadding(Section section)
    {
        if (section.SourceLayout != null)
            section.SourceLayout.padding.bottom = section.SourceBottomPadding;
        if (section.QuestGroupLayout != null)
            section.QuestGroupLayout.padding.bottom = section.QuestGroupBottomPadding;
    }

    private static void InsetDividerImage(
        UnityEngine.UI.Image image,
        RectTransform dividerRect,
        float inset
    )
    {
        var imageRect = image.rectTransform;
        if (imageRect == dividerRect)
        {
            var insetObject = new GameObject(
                "BppInsetDividerImage",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image)
            );
            var insetRect = (RectTransform)insetObject.transform;
            insetRect.SetParent(dividerRect, worldPositionStays: false);
            insetRect.anchorMin = Vector2.zero;
            insetRect.anchorMax = Vector2.one;
            insetRect.offsetMin = new Vector2(inset, 0f);
            insetRect.offsetMax = new Vector2(-inset, 0f);

            var insetImage = insetObject.GetComponent<UnityEngine.UI.Image>();
            insetImage.sprite = image.sprite;
            insetImage.material = image.material;
            insetImage.color = image.color;
            insetImage.type = image.type;
            insetImage.preserveAspect = image.preserveAspect;
            insetImage.fillCenter = image.fillCenter;
            insetImage.raycastTarget = false;
            image.enabled = false;
            return;
        }

        imageRect.offsetMin = new Vector2(imageRect.offsetMin.x + inset, imageRect.offsetMin.y);
        imageRect.offsetMax = new Vector2(imageRect.offsetMax.x - inset, imageRect.offsetMax.y);
    }
}
