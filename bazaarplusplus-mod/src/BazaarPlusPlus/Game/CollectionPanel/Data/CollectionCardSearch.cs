#nullable enable
using System.Globalization;
using System.Text;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.TagTypography;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionCardSearch
{
    private const string RelatedTerms = "Reference Related 相关 相關";

    public static bool Matches(CollectionCardVm card, string? query)
    {
        var normalizedQuery = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return true;

        var normalizedCorpus = Normalize(
            string.IsNullOrWhiteSpace(card.SearchText) ? BuildCorpus(card) : card.SearchText
        );
        if (string.IsNullOrWhiteSpace(normalizedCorpus))
            return false;

        var compactCorpus = Compact(normalizedCorpus);
        foreach (
            var term in normalizedQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var compactTerm = Compact(term);
            if (string.IsNullOrEmpty(compactTerm))
                continue;

            if (normalizedCorpus.Contains(term, StringComparison.Ordinal))
                continue;
            if (MatchesInitialism(card, compactTerm))
                continue;
            if (
                ContainsCjk(compactTerm)
                && (
                    compactCorpus.Contains(compactTerm, StringComparison.Ordinal)
                    || HasCjkFuzzyMatch(compactTerm, normalizedCorpus)
                )
            )
                continue;

            return false;
        }

        return true;
    }

    private static bool MatchesInitialism(CollectionCardVm card, string query)
    {
        if (query.Length < 2 || ContainsCjk(query))
            return false;

        return IsInitialism(query, card.DisplayName)
            || IsInitialism(query, SplitIdentifierWords(card.InternalName))
            || IsInitialism(query, SplitIdentifierWords(card.ArtKey));
    }

    private static bool IsInitialism(string query, string? phrase)
    {
        var normalized = Normalize(phrase);
        if (string.IsNullOrEmpty(normalized))
            return false;

        var words = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words.Length != query.Length)
            return false;

        for (var index = 0; index < words.Length; index++)
            if (words[index][0] != query[index])
                return false;
        return true;
    }

    public static string BuildCorpus(CollectionCardVm card)
    {
        var builder = new StringBuilder();
        Append(builder, card.DisplayName);
        Append(builder, card.InternalName);
        Append(builder, card.ArtKey);
        Append(builder, card.Description);
        AppendEnum(builder, card.Type);
        AppendEnum(builder, card.Size);
        AppendEnum(builder, card.StartingTier);
        AppendEnums(builder, card.Heroes);
        AppendEnums(builder, card.Tags);
        AppendHiddenTags(builder, card.HiddenTags);
        foreach (var enchantment in card.Enchantments)
        {
            AppendEnum(builder, enchantment.Key);
            AppendEnum(builder, enchantment.Value.Type);
            AppendEnums(builder, enchantment.Value.Tags);
            AppendHiddenTags(builder, enchantment.Value.HiddenTags);
        }

        return builder.ToString();
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value!.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = true;
        var inMarkup = false;
        var inTemplateToken = false;
        foreach (var raw in normalized)
        {
            if (raw == '<')
            {
                inMarkup = true;
                AppendSpace(builder, ref previousWasSpace);
                continue;
            }
            if (raw == '>' && inMarkup)
            {
                inMarkup = false;
                AppendSpace(builder, ref previousWasSpace);
                continue;
            }
            if (raw == '{')
            {
                inTemplateToken = true;
                AppendSpace(builder, ref previousWasSpace);
                continue;
            }
            if (raw == '}' && inTemplateToken)
            {
                inTemplateToken = false;
                AppendSpace(builder, ref previousWasSpace);
                continue;
            }
            if (inMarkup || inTemplateToken)
                continue;

            var character = char.ToLowerInvariant(raw);
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
                continue;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            AppendSpace(builder, ref previousWasSpace);
        }

        return builder.ToString().Trim();
    }

    private static string Compact(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            if (!char.IsWhiteSpace(character))
                builder.Append(character);
        return builder.ToString();
    }

    private static void AppendEnums<T>(StringBuilder builder, IReadOnlyCollection<T> values)
        where T : struct, Enum
    {
        foreach (var value in values)
            AppendEnum(builder, value);
    }

    private static void AppendHiddenTags(
        StringBuilder builder,
        IReadOnlyCollection<EHiddenTag> hiddenTags
    )
    {
        foreach (var tag in hiddenTags)
        {
            AppendEnum(builder, tag);
            if (!ReferenceTagBaseResolver.TryResolve(tag, out var baseTag))
                continue;

            Append(builder, RelatedTerms);
            if (baseTag.HiddenTag.HasValue)
                AppendEnum(builder, baseTag.HiddenTag.Value);
            if (baseTag.CardTag.HasValue)
                AppendEnum(builder, baseTag.CardTag.Value);
        }
    }

    private static void AppendEnum<T>(StringBuilder builder, T value)
        where T : struct, Enum
    {
        var text = value.ToString();
        Append(builder, text);
        Append(builder, SplitIdentifierWords(text));
    }

    private static string SplitIdentifierWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (
                i > 0
                && char.IsUpper(character)
                && (
                    char.IsLower(value[i - 1])
                    || (i + 1 < value.Length && char.IsLower(value[i + 1]))
                )
            )
            {
                builder.Append(' ');
            }
            builder.Append(character);
        }
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (builder.Length > 0)
            builder.Append(' ');
        builder.Append(value);
    }

    private static void AppendSpace(StringBuilder builder, ref bool previousWasSpace)
    {
        if (previousWasSpace)
            return;

        builder.Append(' ');
        previousWasSpace = true;
    }

    private static bool HasCjkFuzzyMatch(string query, string corpus)
    {
        var candidate = new StringBuilder();
        foreach (var character in corpus)
        {
            if (IsCjk(character))
            {
                candidate.Append(character);
                continue;
            }

            if (char.IsWhiteSpace(character))
                continue;

            if (IsOrderedSubsequence(query, candidate.ToString()))
                return true;
            candidate.Clear();
        }

        return IsOrderedSubsequence(query, candidate.ToString());
    }

    private static bool ContainsCjk(string value)
    {
        foreach (var character in value)
            if (IsCjk(character))
                return true;
        return false;
    }

    private static bool IsCjk(char value) => value >= '⺀';

    private static bool IsOrderedSubsequence(string query, string candidate)
    {
        var index = 0;
        foreach (var character in candidate)
        {
            if (character != query[index])
                continue;
            index++;
            if (index == query.Length)
                return true;
        }

        return false;
    }
}
