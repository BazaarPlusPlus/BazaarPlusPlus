#nullable enable
using System.Globalization;
using System.Text;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.EventPreview;

namespace BazaarPlusPlus.Game.CollectionPanel.Tooltips;

public readonly struct CollectionTierTooltipText
{
    public CollectionTierTooltipText(CardTier tier, string text)
    {
        Tier = tier;
        Text = text ?? string.Empty;
    }

    public CardTier Tier { get; }
    public string Text { get; }
}

public static class CollectionTierTooltipTextMerger
{
    private const char PlaceholderStart = '\uE000';
    private const char PlaceholderEnd = '\uE001';
    private const string Arrow = " <sprite name=Fusion> ";
    private const string CooldownArrow = ">";

    public static string Merge(IReadOnlyList<CollectionTierTooltipText> variants) =>
        Merge(variants, Arrow);

    public static string Merge(
        IReadOnlyList<CollectionTierTooltipText> variants,
        bool keepValuesTogether
    ) => Merge(variants, Arrow, keepValuesTogether);

    /// <summary>Merge each tooltip line separately so a newly unlocked line stays visible.</summary>
    public static string MergeLines(
        IReadOnlyList<CollectionTierTooltipText> variants,
        bool keepValuesTogether = false
    )
    {
        if (variants == null || variants.Count == 0)
            return string.Empty;
        var lines = new string[variants.Count][];
        var count = 0;
        for (var index = 0; index < variants.Count; index++)
        {
            lines[index] = variants[index]
                .Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            count = Math.Max(count, lines[index].Length);
        }
        var output = new List<string>(count);
        for (var line = 0; line < count; line++)
        {
            var values = new List<CollectionTierTooltipText>(variants.Count);
            for (var index = 0; index < variants.Count; index++)
                if (line < lines[index].Length)
                    values.Add(
                        new CollectionTierTooltipText(variants[index].Tier, lines[index][line])
                    );
            output.Add(Merge(values, keepValuesTogether));
        }
        return string.Join("\n", output);
    }

    public static string MergeCooldown(IReadOnlyList<CollectionTierTooltipText> variants)
    {
        var merged = Merge(variants, CooldownArrow);
        return string.IsNullOrEmpty(merged) ? merged : $"<size=36%>{merged}</size>";
    }

    private static string Merge(
        IReadOnlyList<CollectionTierTooltipText> variants,
        string separator,
        bool keepValuesTogether = false
    )
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

            AppendValue(output, variants, parsed, valueIndex, separator, keepValuesTogether);
            index = end;
        }

        return output.ToString();
    }

    private static void AppendValue(
        StringBuilder output,
        IReadOnlyList<CollectionTierTooltipText> variants,
        IReadOnlyList<ParsedText> parsed,
        int valueIndex,
        string separator,
        bool keepValuesTogether
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

        if (keepValuesTogether)
            output.Append("<nobr>");
        for (var index = 0; index < variants.Count; index++)
        {
            if (index > 0)
                output.Append(separator);
            output.Append("<color=#");
            output.Append(EncounterPreviewText.TierColorHex(variants[index].Tier));
            output.Append('>');
            output.Append(parsed[index].Values[valueIndex]);
            output.Append("</color>");
        }
        if (keepValuesTogether)
            output.Append("</nobr>");
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
