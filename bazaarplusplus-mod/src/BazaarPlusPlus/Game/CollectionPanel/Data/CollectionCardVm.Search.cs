#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Search-side memoisation for the immutable card projection. Every one of these derivations is a
// pure function of fields fixed at construction, but the filter engine re-runs the whole search
// predicate over the active tab on each filter click, so recomputing them per card per query cost
// two full character sweeps and two allocations per card. The backing fields stay here rather than
// in CollectionCardVm.cs because CollectionGridLayout.Tests compiles that file without
// CollectionCardSearch.cs; keep this file in step with the projects that include both.
internal sealed partial class CollectionCardVm
{
    private string? _normalizedSearchCorpus;
    private string? _compactSearchCorpus;
    private string[]? _initialismKeys;

    internal string NormalizedSearchCorpus =>
        _normalizedSearchCorpus ??= CollectionCardSearch.Normalize(
            string.IsNullOrWhiteSpace(SearchText)
                ? CollectionCardSearch.BuildCorpus(this)
                : SearchText
        );

    internal string CompactSearchCorpus =>
        _compactSearchCorpus ??= CollectionCardSearch.Compact(NormalizedSearchCorpus);

    internal string[] InitialismKeys =>
        _initialismKeys ??= CollectionCardSearch.BuildInitialismKeys(this);
}
