using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.QuestPreview;
using Il2CppBazaarGameShared.Domain.Tooltips;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Game;

internal sealed class AndroidQuestPreview(BppSettings settings) : IDisposable
{
    private readonly Dictionary<int, Presentation> _presentations = new();

    // An entry's text or layout was restored or rewritten since the group last laid out.
    private bool _layoutDirty;

    public void Render(
        TooltipQuestEntry entryView,
        CardQuestEntryData entry,
        CardTooltipData tooltip
    )
    {
        var label = entryView._descriptionText;
        if (label == null)
            return;
        var id = label.GetInstanceID();
        if (_presentations.Remove(id, out var previous))
        {
            _layoutDirty = true;
            RestoreLayout(previous);
        }
        if (!settings.EnableDetailedPreviewConfig.Value)
            return;
        var reward = entry.QuestEntry.Reward?.Localization?.Tooltips;
        if (reward == null)
            return;
        var nativeText = label.text;
        var passive = tooltip.RenderQuestTooltips(ref reward, ETooltipType.Passive);
        var active = tooltip.RenderQuestTooltips(ref reward, ETooltipType.Active);
        var text = BppQuestRewardPreviewText.AppendRewardPreview(nativeText, passive, active);
        if (text == nativeText)
            return;
        _presentations[id] = new(label, nativeText, label.margin, label.lineSpacing);
        var margin = label.margin;
        label.margin = new(
            margin.x,
            margin.y * BppQuestRewardPreviewText.VerticalMarginScale,
            margin.z,
            margin.w * BppQuestRewardPreviewText.VerticalMarginScale
        );
        if (!BppQuestRewardPreviewText.ContainsInlineSprite(nativeText, passive, active))
            label.lineSpacing += BppQuestRewardPreviewText.RewardLineSpacingIncrease;
        label.text = text;
        _layoutDirty = true;
    }

    /// <summary>
    /// Whether a quest entry's height may have changed since the last call: only then does its
    /// group need the forced layout rebuild. A restore counts, or a reused entry keeps the
    /// previous card's height.
    /// </summary>
    public bool ConsumeLayoutDirty()
    {
        var dirty = _layoutDirty;
        _layoutDirty = false;
        return dirty;
    }

    public void Tick()
    {
        if (!settings.EnableDetailedPreviewConfig.Value)
            Dispose();
    }

    public void Dispose()
    {
        foreach (var presentation in _presentations.Values)
        {
            if (presentation.Label == null)
                continue;
            presentation.Label.text = presentation.Text;
            RestoreLayout(presentation);
            presentation.Label.GetComponentInParent<TooltipQuestGroup>()?.ForceRebuildLayout();
        }
        _presentations.Clear();
    }

    private static void RestoreLayout(Presentation presentation)
    {
        if (presentation.Label == null)
            return;
        presentation.Label.margin = presentation.Margin;
        presentation.Label.lineSpacing = presentation.LineSpacing;
    }

    private sealed record Presentation(
        TMP_Text Label,
        string Text,
        Vector4 Margin,
        float LineSpacing
    );
}
