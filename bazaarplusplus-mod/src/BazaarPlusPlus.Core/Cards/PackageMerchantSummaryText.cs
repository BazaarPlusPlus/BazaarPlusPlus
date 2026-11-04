#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// A package's merchant line: the merchant's title and the first sentence of its description, and
/// how it sits in the package's passive block. Plain text work, so both hosts share it.
/// </summary>
public static class PackageMerchantSummaryText
{
    private static readonly Regex ScaledInlineSpritePrefix = new(
        @"<size=[^>]+>(?=<voffset=[^>]+><sprite\s)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly char[] SentenceTerminators = ['.', '。', '!', '！', '?', '？'];
    private static readonly HashSet<string> SelfClosingTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br",
        "sprite",
        "space",
        "quad",
    };

    public static string Format(string? title, string? nativeDescription)
    {
        var normalizedTitle = title?.Trim();
        var sellingRule = FirstSentence(nativeDescription);
        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(sellingRule))
            return string.Empty;

        var separator = ContainsCjk(normalizedTitle) ? "：" : ": ";
        return normalizedTitle + separator + sellingRule;
    }

    public static string WrapForPassiveBlock(string summary)
    {
        var compactSummary = summary
            .Replace("<line-height=1.6em>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</line-height>", string.Empty, StringComparison.OrdinalIgnoreCase);
        compactSummary = ScaledInlineSpritePrefix.Replace(compactSummary, "<size=100%>");
        return "<size=80%><line-height=1.1em>\n</line-height><line-height=1.6em>"
            + compactSummary
            + "</line-height></size>";
    }

    public static void AppendToPassiveBlock(StringBuilder passiveText, string summary)
    {
        while (passiveText.Length > 0 && passiveText[passiveText.Length - 1] is '\r' or '\n')
            passiveText.Length--;

        passiveText.Append(WrapForPassiveBlock(summary));
    }

    private static string FirstSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var value = text.Trim();
        var openTags = new List<string>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '<')
            {
                var tagEnd = value.IndexOf('>', index + 1);
                if (tagEnd < 0)
                    break;

                TrackTag(value.AsSpan(index + 1, tagEnd - index - 1), openTags);
                index = tagEnd;
                continue;
            }

            if (Array.IndexOf(SentenceTerminators, value[index]) < 0)
                continue;
            if (
                value[index] == '.'
                && index > 0
                && index + 1 < value.Length
                && char.IsDigit(value[index - 1])
                && char.IsDigit(value[index + 1])
            )
                continue;

            var sentence = value[..index].TrimEnd();
            for (var tagIndex = openTags.Count - 1; tagIndex >= 0; tagIndex--)
                sentence += $"</{openTags[tagIndex]}>";
            return sentence;
        }

        return value;
    }

    private static void TrackTag(ReadOnlySpan<char> tag, List<string> openTags)
    {
        tag = tag.Trim();
        if (tag.IsEmpty || tag[0] is '!' or '?')
            return;

        var isClosing = tag[0] == '/';
        if (isClosing)
            tag = tag[1..].TrimStart();

        var nameLength = 0;
        while (
            nameLength < tag.Length
            && !char.IsWhiteSpace(tag[nameLength])
            && tag[nameLength] != '='
            && tag[nameLength] != '/'
        )
            nameLength++;
        if (nameLength == 0)
            return;

        var name = tag[..nameLength].ToString();
        if (isClosing)
        {
            for (var index = openTags.Count - 1; index >= 0; index--)
            {
                if (!string.Equals(openTags[index], name, StringComparison.OrdinalIgnoreCase))
                    continue;

                openTags.RemoveAt(index);
                break;
            }
            return;
        }

        if (!tag.EndsWith("/", StringComparison.Ordinal) && !SelfClosingTags.Contains(name))
            openTags.Add(name);
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var character in text)
        {
            if (
                character is >= '\u3400' and <= '\u4dbf'
                || character is >= '\u4e00' and <= '\u9fff'
                || character is >= '\uf900' and <= '\ufaff'
            )
                return true;
        }

        return false;
    }
}
