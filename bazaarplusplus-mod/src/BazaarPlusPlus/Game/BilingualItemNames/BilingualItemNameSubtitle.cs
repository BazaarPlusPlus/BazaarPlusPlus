#nullable enable
using BazaarPlusPlus.GameInterop.Fonts;
using TheBazaar.UI.Tooltips;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.BilingualItemNames;

// The native title is a serif TMP label. Keep it intact and add the translated title as a
// separate label: Chinese uses the game's serif face, while English uses its sans face.
// The wrapper owns both rows and has no padding or spacing, avoiding the extra line box that the
// old rich-text newline/voffset markup left between the item name and its translation.
internal static class BilingualItemNameSubtitle
{
    private const string StackName = "BppBilingualItemNameStack";
    private const string LabelName = "BppBilingualItemNameSubtitle";
    private const float SubtitleScale = 0.42f;
    private const float SubtitleGapScale = 0.6f;
    private const float ChineseLocaleEnglishOffset = 2f;

    internal static bool TryShow(
        CardTooltipController controller,
        string subtitleText,
        bool isEnglishSubtitle
    )
    {
        var header = controller?.headerText;
        if (header == null || string.IsNullOrWhiteSpace(subtitleText))
            return false;

        var stack = EnsureStack(header);
        if (
            stack == null
            || NativeGameTypography.PrepareOwnedText(
                isEnglishSubtitle
                    ? NativeGameTypography.OwnedTextRole.Body
                    : NativeGameTypography.OwnedTextRole.Heading,
                out var typography
            ) != NativeGameTypography.Outcome.Ready
            || typography == null
        )
            return false;

        var label = EnsureLabel(stack);
        if (label == null || typography.Apply(label) != NativeGameTypography.Outcome.Applied)
            return false;

        label.enableAutoSizing = false;
        label.fontSize = header.fontSize * SubtitleScale;
        label.fontStyle = FontStyles.Normal;
        label.alignment = header.alignment;
        label.color = header.color;
        label.margin = new Vector4(isEnglishSubtitle ? ChineseLocaleEnglishOffset : 0f, 0f, 0f, 0f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = false;
        label.lineSpacing = 0f;
        label.paragraphSpacing = 0f;
        label.text = subtitleText;
        label.gameObject.SetActive(true);

        if (stack.GetComponent<VerticalLayoutGroup>() is { } layout)
            layout.spacing = -label.fontSize * (1f - SubtitleGapScale);
        LayoutRebuilder.MarkLayoutForRebuild(stack);
        return true;
    }

    internal static void Hide(CardTooltipController controller)
    {
        var header = controller?.headerText;
        var stack = header?.transform.parent as RectTransform;
        var label = stack?.Find(LabelName)?.GetComponent<TMP_Text>();
        if (label == null)
            return;

        label.text = string.Empty;
        label.gameObject.SetActive(false);
        LayoutRebuilder.MarkLayoutForRebuild(stack!);
    }

    private static RectTransform? EnsureStack(TMP_Text header)
    {
        if (header.transform.parent is not RectTransform parent)
            return null;

        if (parent.name == StackName && parent.GetComponent<VerticalLayoutGroup>() is not null)
            return parent;

        var headerRect = header.rectTransform;
        var siblingIndex = header.transform.GetSiblingIndex();
        var stackObject = new GameObject(StackName, typeof(RectTransform));
        var stack = stackObject.GetComponent<RectTransform>();
        stack.SetParent(parent, worldPositionStays: false);
        stack.SetSiblingIndex(siblingIndex);
        stack.anchorMin = headerRect.anchorMin;
        stack.anchorMax = headerRect.anchorMax;
        stack.pivot = headerRect.pivot;
        stack.anchoredPosition = headerRect.anchoredPosition;
        stack.sizeDelta = headerRect.sizeDelta;
        stack.localScale = Vector3.one;

        var sourceLayout = header.GetComponent<LayoutElement>();
        if (sourceLayout != null)
        {
            var stackLayout = stackObject.AddComponent<LayoutElement>();
            stackLayout.ignoreLayout = sourceLayout.ignoreLayout;
            stackLayout.minWidth = sourceLayout.minWidth;
            stackLayout.preferredWidth = sourceLayout.preferredWidth;
            stackLayout.flexibleWidth = sourceLayout.flexibleWidth;
        }

        var layout = stackObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 0f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        if (parent.GetComponent<LayoutGroup>() == null)
        {
            var fitter = stackObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        header.transform.SetParent(stack, worldPositionStays: false);
        return stack;
    }

    private static TextMeshProUGUI? EnsureLabel(RectTransform stack)
    {
        var existing = stack.Find(LabelName)?.GetComponent<TextMeshProUGUI>();
        if (existing != null)
            return existing;

        var labelObject = new GameObject(LabelName, typeof(RectTransform), typeof(CanvasRenderer));
        labelObject.transform.SetParent(stack, worldPositionStays: false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.localScale = Vector3.one;

        var label = labelObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        return label;
    }
}
