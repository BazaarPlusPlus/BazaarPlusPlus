#nullable enable

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

public static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet CopyBuildText = new(
        "Copy build",
        "复制阵容",
        "複製陣容"
    );
    private static readonly LocalizedTextSet CopiedBuildText = new("Copied", "已复制", "已複製");
    private static readonly LocalizedTextSet CopyHeadingText = new(
        "The Bazaar ten-win build",
        "The Bazaar 十胜阵容",
        "The Bazaar 十勝陣容"
    );
    private static readonly LocalizedTextSet CopyHeroText = new("Hero", "英雄", "英雄");
    private static readonly LocalizedTextSet CopyItemsText = new(
        "Items, left to right",
        "物品（从左到右）",
        "物品（從左到右）"
    );
    private static readonly LocalizedTextSet CopySizeText = new("Size", "尺寸", "尺寸");
    private static readonly LocalizedTextSet CopyQualityText = new("Quality", "品质", "品質");
    private static readonly LocalizedTextSet CopyEnchantmentText = new(
        "Enchantment",
        "附魔",
        "附魔"
    );
    private static readonly LocalizedTextSet CopyTagsText = new("Tags", "标签", "標籤");
    private static readonly LocalizedTextSet CopyCooldownText = new("Cooldown", "冷却", "冷卻");
    private static readonly LocalizedTextSet CopyAmmoText = new("Ammo", "弹药", "彈藥");
    private static readonly LocalizedTextSet CopySecondsText = new("s", " 秒", " 秒");
    private static readonly LocalizedTextSet CopyLabelSeparatorText = new(": ", "：", "：");
    private static readonly LocalizedTextSet CopyListSeparatorText = new(", ", "、", "、");

    // TextMeshPro rich text: colours, sizes, fonts, styles and keyword sprites.
    private static readonly Regex RichTextTag = new("<[^<>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex Sprite = new(
        @"<sprite\b[^>]*?\bname\s*=\s*""?([^""\s>]+)""?[^>]*>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );
    private static readonly Regex Spaces = new(@"[ \t\u00A0]{2,}", RegexOptions.CultureInvariant);

    public static string CopyBuild() => L.Resolve(CopyBuildText);

    public static string CopiedBuild() => L.Resolve(CopiedBuildText);

    /// <summary>
    /// The browsed Ten-Win Build as Markdown for a player to paste into a chat with an AI: the
    /// hero, the build's ten-win evidence, and each item from left to right with its size,
    /// quality, enchantment, tags, cooldown, ammo and tooltip text. <paramref name="describe"/>
    /// is the host's read of an item as the game shows it; <paramref name="iconLabel"/> names
    /// the keyword a tooltip icon stands for. Null without a recommendation.
    /// </summary>
    public static string? BuildMarkdown(
        LiveBuildPanelSnapshot snapshot,
        Func<string> theDragonsDisplayName,
        Func<BppItemBoardCard, TenWinBuildItemText> describe,
        Func<string, string?> iconLabel
    )
    {
        var recommendation = snapshot.Recommendation;
        if (recommendation == null)
            return null;

        var text = new StringBuilder();
        text.Append("# ").Append(L.Resolve(CopyHeadingText)).Append("\n\n");
        if (snapshot.Hero is { } hero)
            Fact(
                text,
                L.Resolve(CopyHeroText),
                CollectionPanelText.Hero(hero, theDragonsDisplayName)
            );
        Fact(text, L.Resolve(MatchRateLabelText), MatchRateValue(recommendation.TenWinRateBps));
        Fact(
            text,
            L.Resolve(MatchSampleLabelText),
            MatchSampleValue(recommendation.TenWinRunCount, recommendation.CompletedRunCount)
        );
        Fact(
            text,
            L.Resolve(MatchFinalDayLabelText),
            MatchFinalDayValue(recommendation.P75TenWinFinalDay)
        );

        text.Append("\n## ").Append(L.Resolve(CopyItemsText)).Append('\n');
        // In the order the panel draws them: the board's planned sockets, left to right.
        var cards = BppItemBoardSlotPlanner
            .Plan(recommendation.Board)
            .Cards.OrderBy(card => card.DisplaySocketId ?? int.MaxValue)
            .ThenBy(card => card.Order);
        var number = 0;
        foreach (var card in cards)
        {
            var item = describe(card);
            text.Append("\n### ").Append(++number).Append(". ").Append(item.Name).Append("\n\n");
            text.Append(string.Join(" · ", ItemFacts(card, item))).Append('\n');
            var lines = PlainLines(item.Description, iconLabel);
            if (lines.Count > 0)
                text.Append('\n');
            foreach (var line in lines)
                text.Append("- ").Append(line).Append('\n');
        }
        return text.ToString();
    }

    private static IEnumerable<string> ItemFacts(BppItemBoardCard card, TenWinBuildItemText item)
    {
        yield return Label(CopySizeText, CollectionPanelText.Size(card.Size));
        yield return Label(CopyQualityText, CollectionPanelText.Tier(card.Tier));
        if (!string.IsNullOrWhiteSpace(item.Enchantment))
            yield return Label(CopyEnchantmentText, item.Enchantment!);
        if (item.Tags.Count > 0)
            yield return Label(
                CopyTagsText,
                string.Join(L.Resolve(CopyListSeparatorText), item.Tags)
            );
        if (item.CooldownSeconds is { } cooldown)
            yield return Label(CopyCooldownText, Number(cooldown) + L.Resolve(CopySecondsText));
        if (item.Ammo is { } ammo)
            yield return Label(CopyAmmoText, Number(ammo));
    }

    private static void Fact(StringBuilder text, string label, string value) =>
        text.Append("- ")
            .Append(label)
            .Append(L.Resolve(CopyLabelSeparatorText))
            .Append(value)
            .Append('\n');

    private static string Label(LocalizedTextSet label, string value) =>
        L.Resolve(label) + L.Resolve(CopyLabelSeparatorText) + value;

    private static string Number(float value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);

    // The tooltip's lines without markup, one per effect, blank and repeated lines dropped. A
    // keyword icon usually sits beside its word ("Deal [icon]20 Damage", "when it [icon]Crits")
    // and goes; one before a value whose line never names it ("this gains [icon]3") becomes
    // its word.
    private static List<string> PlainLines(string richText, Func<string, string?> iconLabel)
    {
        var lines = new List<string>();
        foreach (var raw in (richText ?? string.Empty).Split('\n'))
        {
            var words = RichTextTag.Replace(raw, string.Empty);
            var spelled = Sprite.Replace(
                raw,
                match =>
                    NamesAValue(raw, match)
                    && iconLabel(match.Groups[1].Value) is { } label
                    && !string.IsNullOrWhiteSpace(label)
                    && words.IndexOf(label, StringComparison.OrdinalIgnoreCase) < 0
                        ? $" {label} "
                        : string.Empty
            );
            var line = Spaces.Replace(RichTextTag.Replace(spelled, string.Empty), " ").Trim();
            if (line.Length > 0 && !lines.Contains(line))
                lines.Add(line);
        }
        return lines;
    }

    // Whether the text after an icon, markup aside, starts with a number.
    private static bool NamesAValue(string raw, Match icon)
    {
        var after = RichTextTag
            .Replace(raw.Substring(icon.Index + icon.Length), string.Empty)
            .TrimStart();
        return after.Length > 0 && (char.IsDigit(after[0]) || after[0] is '+' or '-');
    }
}
