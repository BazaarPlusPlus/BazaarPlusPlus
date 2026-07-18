#nullable enable
using System.Globalization;
using System.Text;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Tooltips;

internal readonly struct CollectionTierTooltipText
{
    public CollectionTierTooltipText(ETier tier, string text)
    {
        Tier = tier;
        Text = text ?? string.Empty;
    }

    public ETier Tier { get; }
    public string Text { get; }
}

internal static class CollectionTierTooltipTextMerger
{
    private const char PlaceholderStart = '\uE000';
    private const char PlaceholderEnd = '\uE001';
    private const string Arrow = " <sprite name=Fusion> ";
    private const string CooldownArrow = ">";

    public static string Merge(IReadOnlyList<CollectionTierTooltipText> variants) =>
        Merge(variants, Arrow);

    public static string MergeCooldown(IReadOnlyList<CollectionTierTooltipText> variants)
    {
        var merged = Merge(variants, CooldownArrow);
        return string.IsNullOrEmpty(merged) ? merged : $"<size=36%>{merged}</size>";
    }

    private static string Merge(IReadOnlyList<CollectionTierTooltipText> variants, string separator)
    {
        if (variants == null || variants.Count == 0)
            return string.Empty;
        if (variants.Count == 1)
            return variants[0].Text;

        var parsed = new ParsedText[variants.Count];
        for (var index = 0; index < variants.Count; index++)
            parsed[index] = Parse(variants[index].Text);

        var first = parsed[0];
        for (var index = 1; index < parsed.Length; index++)
        {
            if (
                !string.Equals(first.Skeleton, parsed[index].Skeleton, StringComparison.Ordinal)
                || first.Values.Count != parsed[index].Values.Count
            )
            {
                return variants[0].Text;
            }
        }

        var output = new StringBuilder(first.Skeleton.Length + variants.Count * 24);
        for (var index = 0; index < first.Skeleton.Length; index++)
        {
            if (first.Skeleton[index] != PlaceholderStart)
            {
                output.Append(first.Skeleton[index]);
                continue;
            }

            var end = first.Skeleton.IndexOf(PlaceholderEnd, index + 1);
            if (end < 0)
                return variants[0].Text;
            if (
                !int.TryParse(
                    first.Skeleton.Substring(index + 1, end - index - 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var valueIndex
                )
            )
            {
                return variants[0].Text;
            }

            AppendValue(output, variants, parsed, valueIndex, separator);
            index = end;
        }

        return output.ToString();
    }

    private static void AppendValue(
        StringBuilder output,
        IReadOnlyList<CollectionTierTooltipText> variants,
        IReadOnlyList<ParsedText> parsed,
        int valueIndex,
        string separator
    )
    {
        var firstValue = parsed[0].Values[valueIndex];
        var differs = false;
        for (var index = 1; index < parsed.Count; index++)
        {
            if (
                !string.Equals(
                    firstValue,
                    parsed[index].Values[valueIndex],
                    StringComparison.Ordinal
                )
            )
            {
                differs = true;
                break;
            }
        }

        if (!differs)
        {
            output.Append(firstValue);
            return;
        }

        for (var index = 0; index < variants.Count; index++)
        {
            if (index > 0)
                output.Append(separator);
            output.Append("<color=#");
            output.Append(TierColorHex(variants[index].Tier));
            output.Append('>');
            output.Append(parsed[index].Values[valueIndex]);
            output.Append("</color>");
        }
    }

    private static ParsedText Parse(string text)
    {
        var skeleton = new StringBuilder(text.Length);
        var values = new List<string>();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '<')
            {
                var tagEnd = text.IndexOf('>', index + 1);
                if (tagEnd < 0)
                {
                    skeleton.Append(text[index]);
                    continue;
                }

                skeleton.Append(text, index, tagEnd - index + 1);
                index = tagEnd;
                continue;
            }

            if (!IsNumberStart(text, index))
            {
                skeleton.Append(text[index]);
                continue;
            }

            var start = index;
            if (text[index] == '+' || text[index] == '-')
                index++;
            while (index < text.Length && char.IsDigit(text[index]))
                index++;
            while (
                index + 1 < text.Length
                && (text[index] == '.' || text[index] == ',')
                && char.IsDigit(text[index + 1])
            )
            {
                index++;
                while (index < text.Length && char.IsDigit(text[index]))
                    index++;
            }
            if (index < text.Length && text[index] == '%')
                index++;

            values.Add(text.Substring(start, index - start));
            skeleton.Append(PlaceholderStart);
            skeleton.Append(values.Count - 1);
            skeleton.Append(PlaceholderEnd);
            index--;
        }

        return new ParsedText(skeleton.ToString(), values);
    }

    private static bool IsNumberStart(string text, int index)
    {
        if (char.IsDigit(text[index]))
            return true;
        return (text[index] == '+' || text[index] == '-')
            && index + 1 < text.Length
            && char.IsDigit(text[index + 1]);
    }

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

    private readonly struct ParsedText
    {
        public ParsedText(string skeleton, IReadOnlyList<string> values)
        {
            Skeleton = skeleton;
            Values = values;
        }

        public string Skeleton { get; }
        public IReadOnlyList<string> Values { get; }
    }
}
