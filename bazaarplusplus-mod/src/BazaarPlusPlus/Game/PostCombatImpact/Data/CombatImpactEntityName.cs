using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactEntityName
{
    private static readonly LocalizedTextSet PlayerText = new("You", "己方");
    private static readonly LocalizedTextSet OpponentText = new("Opponent", "对手");
    private static readonly LocalizedTextSet SkillText = new("Skill", "技能");
    private static readonly LocalizedTextSet ItemText = new("Item", "物品");

    internal static string Player => L.Resolve(PlayerText);

    internal static string Opponent => L.Resolve(OpponentText);

    internal static string Skill => L.Resolve(SkillText);

    internal static string Item => L.Resolve(ItemText);

    internal static string RemoveNativeEnchantmentPrefix(string nativeTooltipTitle)
    {
        if (string.IsNullOrWhiteSpace(nativeTooltipTitle))
            return string.Empty;

        var lastLineBreak = nativeTooltipTitle.LastIndexOf('\n');
        var displayLine =
            lastLineBreak >= 0
                ? nativeTooltipTitle[(lastLineBreak + 1)..].Trim()
                : nativeTooltipTitle.Trim();
        return RemoveRichTextTags(displayLine);
    }

    private static string RemoveRichTextTags(string value)
    {
        var firstTag = value.IndexOf('<');
        if (firstTag < 0)
            return value;

        var plain = new System.Text.StringBuilder(value.Length);
        var copyFrom = 0;
        while (firstTag >= 0)
        {
            plain.Append(value, copyFrom, firstTag - copyFrom);
            var tagEnd = value.IndexOf('>', firstTag + 1);
            if (tagEnd < 0)
            {
                plain.Append(value, firstTag, value.Length - firstTag);
                return plain.ToString().Trim();
            }

            copyFrom = tagEnd + 1;
            firstTag = value.IndexOf('<', copyFrom);
        }

        plain.Append(value, copyFrom, value.Length - copyFrom);
        return plain.ToString().Trim();
    }
}
