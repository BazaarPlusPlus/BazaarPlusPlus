#nullable enable
namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// A host adapter's mapping between one game enum and one card facet, by member name
/// (ADR-0015). Each host instantiates it over its own game enum (Mono or IL2CPP interop), so
/// neither host writes a per-member table and a game build that adds a member needs no code
/// change until BPP adds the facet member: until then the value maps to nothing.
/// </summary>
public sealed class CardFacetNameMap<TSource, TFacet>
    where TSource : struct, Enum
    where TFacet : struct, Enum
{
    private readonly Dictionary<TSource, TFacet> _toFacet = new();
    private readonly Dictionary<TFacet, TSource> _toSource = new();
    private readonly List<string> _unmappedSourceNames = new();

    /// <param name="aliases">
    /// Game member names that map to a differently named facet member, such as the legacy
    /// <c>Hero8</c> for <see cref="CardHero.TheDragons"/>. An exact name match wins the reverse
    /// direction when a game build defines both.
    /// </param>
    public CardFacetNameMap(IReadOnlyDictionary<string, TFacet>? aliases = null)
    {
        var exactFacets = new HashSet<TFacet>();
        foreach (var name in Enum.GetNames(typeof(TSource)))
        {
            var source = (TSource)Enum.Parse(typeof(TSource), name);
            if (
                Enum.TryParse<TFacet>(name, ignoreCase: false, out var facet)
                && Enum.IsDefined(typeof(TFacet), facet)
            )
            {
                _toFacet[source] = facet;
                _toSource[facet] = source;
                exactFacets.Add(facet);
            }
            else if (aliases != null && aliases.TryGetValue(name, out var aliased))
            {
                _toFacet[source] = aliased;
                if (!exactFacets.Contains(aliased))
                    _toSource[aliased] = source;
            }
            else
            {
                _unmappedSourceNames.Add(name);
            }
        }
    }

    /// <summary>Game member names with no facet member: the vocabulary drift a pin test reports.</summary>
    public IReadOnlyList<string> UnmappedSourceNames => _unmappedSourceNames;

    public bool TryToFacet(TSource source, out TFacet facet) =>
        _toFacet.TryGetValue(source, out facet);

    public bool TryToSource(TFacet facet, out TSource source) =>
        _toSource.TryGetValue(facet, out source);

    /// <summary>The mapped facets of a game collection, in order, without duplicates.</summary>
    public HashSet<TFacet> ToFacetSet(IEnumerable<TSource>? sources)
    {
        var result = new HashSet<TFacet>();
        if (sources == null)
            return result;
        foreach (var source in sources)
        {
            if (_toFacet.TryGetValue(source, out var facet))
                result.Add(facet);
        }
        return result;
    }
}
