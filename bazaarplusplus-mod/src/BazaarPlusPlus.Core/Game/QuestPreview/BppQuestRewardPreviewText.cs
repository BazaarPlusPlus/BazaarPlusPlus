#nullable enable
using BazaarPlusPlus.Game.ItemEnchantPreview;

namespace BazaarPlusPlus.Game.QuestPreview;

public static class BppQuestRewardPreviewText
{
    private const string SpriteMarkupPrefix = "<sprite";
    private const int RewardSizePercent = 76;
    public const float VerticalMarginScale = 0.5f;
    public const float RewardLineSpacingIncrease = 12f;
    private const float RewardInlineSizeScale = RewardSizePercent / 100f;

    public static string AppendRewardPreview(
        string? questText,
        string? passiveRewardText,
        string? activeRewardText
    )
    {
        var normalizedQuestText = (questText ?? string.Empty).Trim();
        if (normalizedQuestText.Length == 0)
            return normalizedQuestText;

        var rewardText = BuildRewardText(passiveRewardText, activeRewardText);
        if (rewardText.Length == 0)
            return normalizedQuestText;

        return $"{normalizedQuestText}\n<size={RewardSizePercent}%>{rewardText}</size>";
    }

    public static bool ContainsInlineSprite(params string?[] values)
    {
        foreach (var value in values)
        {
            if (value?.IndexOf(SpriteMarkupPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string BuildRewardText(params string?[] values)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var line = ItemEnchantPreviewText.ScaleInlineSizes(
                NormalizeInline(value),
                RewardInlineSizeScale
            );
            if (line.Length == 0 || !seen.Add(line))
                continue;
            lines.Add(line);
        }

        return string.Join(" / ", lines);
    }

    private static string NormalizeInline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(
            " ",
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
        );
    }
}
