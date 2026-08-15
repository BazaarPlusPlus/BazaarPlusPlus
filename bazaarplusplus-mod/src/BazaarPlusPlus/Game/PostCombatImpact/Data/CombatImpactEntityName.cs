namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactEntityName
{
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
