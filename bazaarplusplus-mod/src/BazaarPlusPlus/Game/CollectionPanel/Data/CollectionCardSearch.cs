#nullable enable
using System.Globalization;
using System.Text;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.TagTypography;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// A query normalised once for a whole filter pass. The engine calls the search predicate for every
// card on the active tab, so normalising and splitting inside the predicate repeated the same
// character sweep hundreds of times per filter click.
internal readonly struct CollectionSearchTerms
{
    internal static readonly CollectionSearchTerms Empty = new(
        Array.Empty<string>(),
        Array.Empty<string>()
    );

    private CollectionSearchTerms(string[] terms, string[] compactTerms)
    {
        Terms = terms;
        CompactTerms = compactTerms;
    }

    internal string[] Terms { get; }

    internal string[] CompactTerms { get; }

    internal int Count => Terms.Length;

    internal bool IsEmpty => Terms.Length == 0;

    internal static CollectionSearchTerms From(string? query)
    {
        var normalized = CollectionCardSearch.Normalize(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return Empty;

        var terms = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
            return Empty;

        var compactTerms = new string[terms.Length];
        for (var index = 0; index < terms.Length; index++)
            compactTerms[index] = CollectionCardSearch.Compact(terms[index]);
        return new CollectionSearchTerms(terms, compactTerms);
    }
}

internal static class CollectionCardSearch
{
    private const string RelatedTerms = "Reference Related 相关 相關";

    public static bool Matches(CollectionCardVm card, string? query) =>
        Matches(card, CollectionSearchTerms.From(query));

    public static bool Matches(CollectionCardVm card, CollectionSearchTerms terms)
    {
        if (terms.IsEmpty)
            return true;

        var normalizedCorpus = card.NormalizedSearchCorpus;
        if (string.IsNullOrWhiteSpace(normalizedCorpus))
            return false;

        string? compactCorpus = null;
        for (var index = 0; index < terms.Count; index++)
        {
            var compactTerm = terms.CompactTerms[index];
            if (string.IsNullOrEmpty(compactTerm))
                continue;

            if (normalizedCorpus.Contains(terms.Terms[index], StringComparison.Ordinal))
                continue;
            if (MatchesInitialism(card, compactTerm))
                continue;
            if (ContainsCjk(compactTerm))
            {
                compactCorpus ??= card.CompactSearchCorpus;
                if (
                    compactCorpus.Contains(compactTerm, StringComparison.Ordinal)
                    || HasCjkFuzzyMatch(compactTerm, normalizedCorpus)
                )
                    continue;
            }

            return false;
        }

        return true;
    }

    private static bool MatchesInitialism(CollectionCardVm card, string query)
    {
        if (query.Length < 2 || ContainsCjk(query))
            return false;

        var keys = card.InitialismKeys;
        for (var index = 0; index < keys.Length; index++)
            if (string.Equals(keys[index], query, StringComparison.Ordinal))
                return true;
        return false;
    }

    // One key per phrase: the leading character of each word, defined only for phrases of two or
    // more words. A query matches as an initialism exactly when it equals one of these keys.
    internal static string[] BuildInitialismKeys(CollectionCardVm card)
    {
        var keys = new List<string>(3);
        AddInitialismKey(keys, card.DisplayName);
        AddInitialismKey(keys, SplitIdentifierWords(card.InternalName));
        AddInitialismKey(keys, SplitIdentifierWords(card.ArtKey));
        return keys.Count == 0 ? Array.Empty<string>() : keys.ToArray();
    }

    private static void AddInitialismKey(List<string> keys, string? phrase)
    {
        var normalized = Normalize(phrase);
        if (normalized.Length == 0)
            return;

        var words = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
            return;

        var builder = new StringBuilder(words.Length);
        for (var index = 0; index < words.Length; index++)
            builder.Append(words[index][0]);
        keys.Add(builder.ToString());
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

    internal static string Compact(string value)
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
