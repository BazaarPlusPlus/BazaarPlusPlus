#nullable enable
using System.Globalization;
using System.Text;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.EventPreview;

public static class EncounterPreviewText
{
    private static readonly LocalizedTextSet TierDistributionSuffix = new(
        "({0})",
        "（{0}）",
        "（{0}）"
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

    public static string EncounterTierDistributionSuffix(string distribution) =>
        string.Format(CultureInfo.CurrentCulture, Resolve(TierDistributionSuffix), distribution);

    public static string LevelUpMaxHealth(int amount) =>
        string.Format(CultureInfo.CurrentCulture, Resolve(MaxHealth), amount);

    public static string LevelUpRandomPool(int count, int optionCount) =>
        string.Format(CultureInfo.CurrentCulture, Resolve(RandomPool), count, optionCount);

    public static string LevelUpRandomPoolSingle(int optionCount) =>
        string.Format(CultureInfo.CurrentCulture, Resolve(RandomPoolSingle), optionCount);

    public static string LevelUpOneOf() => Resolve(OneOf);

    public static string OutcomesHeader() => Resolve(Outcomes);

    public static string OutcomeCombatPool(int count) =>
        string.Format(CultureInfo.CurrentCulture, Resolve(CombatPool), count);

    public static string OutcomeRandomItem() => Resolve(RandomItem);

    public static string OutcomeRandomSkill() => Resolve(RandomSkill);

    public static string OutcomeRandomReward() => Resolve(RandomReward);

    public static string OutcomeSubPool(int count) =>
        string.Format(CultureInfo.CurrentCulture, Resolve(SubPool), count);

    public static string OutcomeGainSkill(string skillName) =>
        string.Format(CultureInfo.CurrentCulture, Resolve(GainSkill), skillName);

    public static string LevelUpBoardSlots(int count) =>
        string.Format(CultureInfo.CurrentCulture, Resolve(BoardSlots), count);

    public static string JoinTooltipLabel(string label, string detail) =>
        LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode)
            ? $"{label}：{detail}"
            : $"{label}: {detail}";

    public static string JoinColoredTooltipLabel(string label, string detail, string color) =>
        LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode)
            ? $"<color={color}>{label}：</color>{detail}"
            : $"<color={color}>{label}:</color> {detail}";

    public static string NormalizeRewardSpacing(string text)
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

    public static string Tier(CardTier tier)
    {
        var (english, chineseMainland, chineseTraditional) = TierForms(tier);
        return chineseMainland.Length == 0
            ? english
            : L.Resolve(new LocalizedTextSet(english, chineseMainland, chineseTraditional));
    }

    public static (string English, string ChineseMainland, string ChineseTraditional) TierForms(
        CardTier tier
    ) =>
        tier switch
        {
            CardTier.Bronze => ("Bronze", "青铜", "青銅"),
            CardTier.Silver => ("Silver", "白银", "白銀"),
            CardTier.Gold => ("Gold", "黄金", "黃金"),
            CardTier.Diamond => ("Diamond", "钻石", "鑽石"),
            CardTier.Legendary => ("Legendary", "传说", "傳說"),
            _ => (tier.ToString(), string.Empty, string.Empty),
        };

    public static string TierColorHex(CardTier tier) =>
        tier switch
        {
            CardTier.Bronze => "B46241",
            CardTier.Silver => "C0C0C0",
            CardTier.Gold => "FFD700",
            CardTier.Diamond => "00FFFF",
            CardTier.Legendary => "FF4500",
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

    private static string Resolve(LocalizedTextSet text) => L.Resolve(text);
}
