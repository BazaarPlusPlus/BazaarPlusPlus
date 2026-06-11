#nullable enable

using System.Text;

namespace BazaarPlusPlus.Infrastructure;

internal static class StablePanelText
{
    private const string Ellipsis = "...";

    public static string Compact(string? text, int maxCharacters)
    {
        if (maxCharacters <= 0)
            return string.Empty;

        var normalized = CollapseWhitespace(text);
        if (normalized.Length <= maxCharacters)
            return normalized;

        if (maxCharacters <= Ellipsis.Length)
            return normalized[..maxCharacters];

        return normalized[..(maxCharacters - Ellipsis.Length)].TrimEnd() + Ellipsis;
    }

    public static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
