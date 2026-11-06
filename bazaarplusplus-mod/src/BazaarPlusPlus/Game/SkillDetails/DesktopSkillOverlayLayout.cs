using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.SkillDetails;

internal sealed class DesktopSkillOverlayLayout
{
    private static readonly Regex LegendTitleSize = new(
        @"<size\s*=\s*[^>]+>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    // Native canvas units. Keep the scrollbar to the left of the native close button.
    internal void PrepareList(MobileSkillOverlayListBuilder list)
    {
        var scroll = (RectTransform)list._scrollRect.transform;
        var min = scroll.offsetMin;
        var max = scroll.offsetMax;
        min.x = 200f;
        max.x = -440f;
        scroll.offsetMin = min;
        scroll.offsetMax = max;

        if (list._scrollContent.GetComponent<VerticalLayoutGroup>() is { } layout)
        {
            layout.padding = new RectOffset(40, 40, 24, 24);
            layout.spacing = 20f;
        }
    }

    internal void PrepareRow(MobileSkillOverlayRowView row)
    {
        if (row.GetComponent<VerticalLayoutGroup>() is { } layout)
            layout.padding = new RectOffset(0, 24, 0, 0);
        if (row._leftEntry.transform.parent.GetComponent<HorizontalLayoutGroup>() is { } entries)
            entries.spacing = 96f;

        PrepareEntry(row._leftEntry);
        PrepareEntry(row._rightEntry);
    }

    internal void PrepareLegend(MobileSkillOverlayListBuilder list)
    {
        foreach (var row in list._legendTooltipComponent._spawnedLegendRows)
        {
            row.TitleText.enableAutoSizing = false;
            row.TitleText.fontSize = 38f;
            // Native keyword titles wrap both the icon and text in a percentage size tag.
            // Normalize that tag so it cannot enlarge the desktop heading again.
            row.TitleText.text = NormalizeLegendTitle(row.TitleText.text);
            row.DescriptionText.enableAutoSizing = false;
            row.DescriptionText.fontSize = 38f;
        }
        LayoutRebuilder.MarkLayoutForRebuild(list._legendFooter);
    }

    internal static string NormalizeLegendTitle(string title) =>
        LegendTitleSize.Replace(title, "<size=100%>");

    private static void PrepareEntry(MobileSkillOverlayEntryView entry)
    {
        entry._titleText.enableAutoSizing = false;
        entry._titleText.fontSize = 56f;
        entry._descriptionText.enableAutoSizing = false;
        entry._descriptionText.fontSize = 38f;
        entry._descriptionText.lineSpacing = -24f;
        entry._descriptionText.paragraphSpacing = 17f;
        entry._descriptionTopMarginWithLeadingSprite = -17f;

        if (entry.GetComponent<LayoutElement>() is { } element)
            element.minHeight = 200f;
        if (entry.GetComponent<HorizontalLayoutGroup>() is { } layout)
        {
            layout.spacing = 24f;
            layout.childScaleWidth = true;
            layout.childScaleHeight = true;
        }

        // Scale the complete native icon, including its mask inset and tier frame;
        // the layout group accounts for that scale instead of reserving 320 units.
        entry._iconContainer.transform.localScale = Vector3.one * 0.625f;
        if (entry._textColumn.GetComponent<VerticalLayoutGroup>() is { } text)
        {
            text.spacing = 10f;
            text.padding = new RectOffset(0, 0, 16, 0);
        }
    }
}
