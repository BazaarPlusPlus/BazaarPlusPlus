#nullable enable
using System.Text;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EncounterPreviewText
{
    private static readonly LocalizedTextSet DayTierSuffix = new(
        "(up to {0})",
        "（最高{0}）",
        "（最高{0}）"
    );
    private static readonly LocalizedTextSet TierExact = new("({0})", "（{0}）", "（{0}）");
    private static readonly LocalizedTextSet TierDistributionSuffix = new(
        "({0})",
        "（{0}）",
        "（{0}）"
    );
    private static readonly LocalizedTextSet TierCeilingLine = new(
        "up to {0}",
        "最高{0}",
        "最高{0}"
    );
    private static readonly LocalizedTextSet MaxHealth = new(
        "+ {0} Max Health",
        "+ {0} 生命上限",
        "+ {0} 生命上限"
    );
    private static readonly LocalizedTextSet RandomPool = new(
        "{0}× random reward ({1} options)",
        "随机奖励 ×{0}（{1} 个选项）",
        "隨機獎勵 ×{0}（{1} 個選項）"
    );
    private static readonly LocalizedTextSet RandomPoolSingle = new(
        "Random reward ({0} options)",
        "随机奖励（{0} 个选项）",
        "隨機獎勵（{0} 個選項）"
    );
    private static readonly LocalizedTextSet OneOf = new("Choose one:", "选择其一：", "選擇其一：");
    private static readonly LocalizedTextSet Outcomes = new(
        "Possible outcomes:",
        "随机结果：",
        "隨機結果："
    );
    private static readonly LocalizedTextSet CombatPool = new(
        "Fight a monster ({0} possible)",
        "战斗：随机怪物（{0} 种）",
        "戰鬥：隨機怪物（{0} 種）"
    );
    private static readonly LocalizedTextSet RandomItem = new(
        "Random item",
        "随机物品",
        "隨機物品"
    );
    private static readonly LocalizedTextSet RandomSkill = new(
        "Random skill",
        "随机技能",
        "隨機技能"
    );
    private static readonly LocalizedTextSet RandomReward = new(
        "Random reward",
        "随机奖励",
        "隨機獎勵"
    );
    private static readonly LocalizedTextSet SubPool = new(
        "one of {0}:",
        "以下 {0} 种随机其一：",
        "以下 {0} 種隨機其一："
    );
    private static readonly LocalizedTextSet GainSkill = new(
        "Gain skill: {0}",
        "获得技能：{0}",
        "獲得技能：{0}"
    );
    private static readonly LocalizedTextSet BoardSlots = new(
        "+ {0} board slots",
        "+ {0} 个摊位格子",
        "+ {0} 個攤位格子"
    );

    internal static string EncounterDayTierSuffix(ETier tier) =>
        string.Format(Resolve(DayTierSuffix), Tier(tier));

    internal static string EncounterTierExact(ETier tier) =>
        string.Format(Resolve(TierExact), Tier(tier));

    internal static string EncounterTierDistributionSuffix(string distribution) =>
        string.Format(Resolve(TierDistributionSuffix), distribution);

    internal static string EncounterTierCeilingLine(ETier tier) =>
        string.Format(Resolve(TierCeilingLine), Tier(tier));

    internal static string LevelUpMaxHealth(int amount) =>
        string.Format(Resolve(MaxHealth), amount);

    internal static string LevelUpRandomPool(int count, int optionCount) =>
        string.Format(Resolve(RandomPool), count, optionCount);

    internal static string LevelUpRandomPoolSingle(int optionCount) =>
        string.Format(Resolve(RandomPoolSingle), optionCount);

    internal static string LevelUpOneOf() => Resolve(OneOf);

    internal static string OutcomesHeader() => Resolve(Outcomes);

    internal static string OutcomeCombatPool(int count) =>
        string.Format(Resolve(CombatPool), count);

    internal static string OutcomeRandomItem() => Resolve(RandomItem);

    internal static string OutcomeRandomSkill() => Resolve(RandomSkill);

    internal static string OutcomeRandomReward() => Resolve(RandomReward);

    internal static string OutcomeSubPool(int count) => string.Format(Resolve(SubPool), count);

    internal static string OutcomeGainSkill(string skillName) =>
        string.Format(Resolve(GainSkill), skillName);

    internal static string LevelUpBoardSlots(int count) =>
        string.Format(Resolve(BoardSlots), count);

    internal static string JoinTooltipLabel(string label, string detail) =>
        LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode)
            ? $"{label}：{detail}"
            : $"{label}: {detail}";

    internal static string JoinColoredTooltipLabel(string label, string detail, string color) =>
        LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode)
            ? $"<color={color}>{label}：</color>{detail}"
            : $"<color={color}>{label}:</color> {detail}";

    internal static string NormalizeRewardSpacing(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var builder = new StringBuilder(text.Length + 4);
        for (var index = 0; index < text.Length; index++)
        {
            builder.Append(text[index]);
            if (NeedsSpaceAfterPlus(text, index))
                builder.Append(' ');
        }
        return builder.ToString();
    }

    internal static string Tier(ETier tier)
    {
        var (english, chineseMainland, chineseTraditional) = TierForms(tier);
        return chineseMainland.Length == 0
            ? english
            : LocalizedTextHelpers.FormatSimple(english, chineseMainland, chineseTraditional);
    }

    internal static (string English, string ChineseMainland, string ChineseTraditional) TierForms(
        ETier tier
    ) =>
        tier switch
        {
            ETier.Bronze => ("Bronze", "青铜", "青銅"),
            ETier.Silver => ("Silver", "白银", "白銀"),
            ETier.Gold => ("Gold", "黄金", "黃金"),
            ETier.Diamond => ("Diamond", "钻石", "鑽石"),
            ETier.Legendary => ("Legendary", "传说", "傳說"),
            _ => (tier.ToString(), string.Empty, string.Empty),
        };

    internal static string TierColorHex(ETier tier) =>
        tier switch
        {
            ETier.Bronze => "B46241",
            ETier.Silver => "C0C0C0",
            ETier.Gold => "FFD700",
            ETier.Diamond => "00FFFF",
            ETier.Legendary => "FF4500",
            _ => "FFFFFF",
        };

    private static bool NeedsSpaceAfterPlus(string text, int plusIndex)
    {
        if (
            plusIndex < 0
            || plusIndex + 1 >= text.Length
            || text[plusIndex] != '+'
            || !char.IsDigit(text[plusIndex + 1])
        )
            return false;

        var cursor = plusIndex + 2;
        while (cursor < text.Length && char.IsDigit(text[cursor]))
            cursor++;
        return cursor < text.Length && char.IsWhiteSpace(text[cursor]);
    }

    private static string Resolve(LocalizedTextSet text) => LocalizedTextHelpers.Resolve(text);
}
