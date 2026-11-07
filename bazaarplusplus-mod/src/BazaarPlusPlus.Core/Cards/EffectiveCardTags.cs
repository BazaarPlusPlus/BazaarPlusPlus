namespace BazaarPlusPlus.Core.Cards;

public sealed class EffectiveCardTags
{
    public EffectiveCardTags(
        IReadOnlyCollection<CardTag>? runtimeTags,
        IReadOnlyCollection<CardTag>? templateTags,
        IReadOnlyCollection<CardTag>? enchantmentTags,
        IReadOnlyCollection<CardHiddenTag>? runtimeHiddenTags,
        IReadOnlyCollection<CardHiddenTag>? templateHiddenTags,
        IReadOnlyCollection<CardHiddenTag>? enchantmentHiddenTags
    )
    {
        Tags = Merge(runtimeTags, templateTags, enchantmentTags);
        HiddenTags = Merge(runtimeHiddenTags, templateHiddenTags, enchantmentHiddenTags);
    }

    public IReadOnlyCollection<CardTag>? Tags { get; }

    public IReadOnlyCollection<CardHiddenTag>? HiddenTags { get; }

    public IReadOnlyCollection<string> EncounterTagNames() =>
        (Tags?.Select(tag => tag.ToString()) ?? Enumerable.Empty<string>())
            .Concat(HiddenTags?.Select(tag => tag.ToString()) ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyCollection<T>? Merge<T>(params IReadOnlyCollection<T>?[] sources)
    {
        var merged = new HashSet<T>();
        foreach (var source in sources)
            if (source != null)
                merged.UnionWith(source);
        return merged.Count == 0 ? null : merged.ToArray();
    }
}
