#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal static class CollectionSourceAttributionBadge
{
    private const string BadgeName = "BppCollectionSourceAttributionBadge";
    private const string LabelName = "BppCollectionSourceAttributionLabel";
    private const float BadgeRootHeightScale =
        CollectionGridVirtualizer.FallbackNativeCardHeight / 200f;

    public static void Bind(
        GameObject host,
        IReadOnlyList<CollectionSourceOfferMatch>? sourceMatches
    )
    {
        var badge = EnsureBadge(host);
        if (badge == null)
            return;
        var text = AttributionText(sourceMatches);
        if (string.IsNullOrWhiteSpace(text))
        {
            badge.SetActive(false);
            return;
        }

        badge.SetActive(true);
        var label = badge.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        if (label != null)
            label.text = text;
    }

    private static GameObject? EnsureBadge(GameObject host)
    {
        var existing = host.transform.Find(BadgeName);
        if (existing != null)
            return existing.gameObject;
        if (
            NativeGameTypography.PrepareOwnedText(out var typography)
                != NativeGameTypography.Outcome.Ready
            || typography == null
        )
            return null;

        var badge = new GameObject(BadgeName, typeof(RectTransform), typeof(Image));
        badge.transform.SetParent(host.transform, worldPositionStays: false);
        var rect = badge.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(
            -10f * BadgeRootHeightScale,
            -12f * BadgeRootHeightScale
        );
        rect.sizeDelta = new Vector2(132f * BadgeRootHeightScale, 28f * BadgeRootHeightScale);
        rect.localScale = Vector3.one;

        var image = badge.GetComponent<Image>();
        image.color = new Color(0.07f, 0.08f, 0.1f, 0.9f);
        image.raycastTarget = false;

        var labelObject = new GameObject(LabelName, typeof(RectTransform), typeof(CanvasRenderer));
        labelObject.transform.SetParent(badge.transform, worldPositionStays: false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f * BadgeRootHeightScale, 0f);
        labelRect.offsetMax = new Vector2(-6f * BadgeRootHeightScale, 0f);
        labelRect.localScale = Vector3.one;

        var label = labelObject.AddComponent<TextMeshProUGUI>();
        if (typography.Apply(label) != NativeGameTypography.Outcome.Applied)
        {
            UnityEngine.Object.Destroy(badge);
            return null;
        }
        label.fontSize = Mathf.RoundToInt(12f * BadgeRootHeightScale);
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Colors.HistoryTitleText;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return badge;
    }

    private static string AttributionText(IReadOnlyList<CollectionSourceOfferMatch>? sourceMatches)
    {
        if (sourceMatches == null || sourceMatches.Count == 0)
            return string.Empty;

        var enchanted = sourceMatches
            .Where(match => match.SegmentKind == CollectionSourceOfferSegmentKind.Enchanted)
            .ToArray();
        if (enchanted.Length == 0)
            return string.Empty;

        var hasNormal = sourceMatches.Any(match =>
            match.SegmentKind == CollectionSourceOfferSegmentKind.Normal
        );
        if (hasNormal)
            return "Normal + Rare";

        var label = enchanted
            .Select(match => match.RarityLabel)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(label))
            label = "Rare";

        var enchantments = enchanted
            .Where(match => match.EnchantmentType.HasValue)
            .Select(match => match.EnchantmentType!.Value.ToString())
            .Distinct()
            .Take(2)
            .ToArray();
        if (enchantments.Length == 0)
            return label!;
        return $"{label}: {string.Join("/", enchantments)}";
    }
}
