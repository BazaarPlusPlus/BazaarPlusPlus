#nullable enable
using System.Text.RegularExpressions;

namespace BazaarPlusPlus.GameInterop.Cards;

/// <summary>Converts native TMP tooltip markup into text appropriate for an agent prompt.</summary>
internal static class CardDescriptionTextNormalizer
{
    private static readonly Regex RichTextTag = new(
        @"<[^>]*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    internal static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = RichTextTag
            .Replace(text, string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
        var normalizedLines = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                normalizedLines.Add(trimmed);
        }

        return normalizedLines.Count == 0 ? null : string.Join("\n", normalizedLines);
    }
}
