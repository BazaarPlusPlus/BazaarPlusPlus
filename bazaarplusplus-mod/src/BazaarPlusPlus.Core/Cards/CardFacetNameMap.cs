#nullable enable
namespace BazaarPlusPlus.Core.Cards;

// Map game enums by member name so adding a native member cannot change existing mappings.
// Card facet integers belong to BPP and must stay out of persisted and wire data (ADR-0015).
public sealed class CardFacetNameMap<TSource, TFacet>
    where TSource : struct, Enum
    where TFacet : struct, Enum
{
    private readonly Dictionary<TSource, TFacet> _toFacet = new();
    private readonly Dictionary<TFacet, TSource> _toSource = new();
    private readonly List<string> _unmappedSourceNames = new();

    // Prefer an exact reverse match when a build exposes both an alias and the canonical game member.
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

    public IReadOnlyList<string> UnmappedSourceNames => _unmappedSourceNames;

    public bool TryToFacet(TSource source, out TFacet facet) =>
        _toFacet.TryGetValue(source, out facet);

    public bool TryToSource(TFacet facet, out TSource source) =>
        _toSource.TryGetValue(facet, out source);

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
