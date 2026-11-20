#nullable enable
namespace BazaarPlusPlus.Game.RandomCosmetics;

// Save exclusions so newly acquired cosmetics enter the pool automatically.
public sealed class RandomCosmeticPool
{
    private readonly string[] _candidates;
    private readonly HashSet<string> _candidateSet;
    private readonly HashSet<string> _excluded;

    private RandomCosmeticPool(string[] candidates, HashSet<string> excluded)
    {
        _candidates = candidates;
        _candidateSet = new HashSet<string>(candidates, StringComparer.Ordinal);
        _excluded = excluded;
    }

    public static RandomCosmeticPool Create(
        IEnumerable<string?> candidateIds,
        IEnumerable<string?>? excludedIds
    )
    {
        if (candidateIds == null)
            throw new ArgumentNullException(nameof(candidateIds));

        return new RandomCosmeticPool(
            Normalize(candidateIds),
            new HashSet<string>(
                excludedIds == null ? [] : Normalize(excludedIds),
                StringComparer.Ordinal
            )
        );
    }

    public IReadOnlyList<string> Members
    {
        get
        {
            var members = _candidates.Where(id => !_excluded.Contains(id)).ToArray();
            // Every candidate left out (cosmetics removed since the save) leaves the game's pick.
            return members.Length == 0 ? _candidates : members;
        }
    }

    public bool OverridesGamePick => Members.Count < _candidates.Length;

    public bool Contains(string? id) => id != null && Members.Contains(id, StringComparer.Ordinal);

    // Keep exclusions for unavailable cosmetics so their settings survive if they return.
    public IReadOnlyList<string>? Toggle(string? id)
    {
        if (id == null || !_candidateSet.Contains(id))
            return null;

        // Use the displayed fallback membership so the first click can recover from stale exclusions.
        var members = new HashSet<string>(Members, StringComparer.Ordinal);
        var next = new HashSet<string>(
            _excluded.Where(saved => !_candidateSet.Contains(saved)),
            StringComparer.Ordinal
        );
        next.UnionWith(_candidates.Where(candidate => !members.Contains(candidate)));

        if (members.Contains(id))
        {
            if (members.Count == 1)
                return null;
            next.Add(id);
        }
        else
        {
            next.Remove(id);
        }

        return next.OrderBy(saved => saved, StringComparer.Ordinal).ToArray();
    }

    public string? Pick(Func<int, int> nextIndex)
    {
        if (nextIndex == null)
            throw new ArgumentNullException(nameof(nextIndex));
        if (!OverridesGamePick)
            return null;

        var members = Members;
        var index = nextIndex(members.Count);
        if (index < 0 || index >= members.Count)
            throw new InvalidOperationException("The random index is out of range.");
        return members[index];
    }

    private static string[] Normalize(IEnumerable<string?> ids) =>
        ids.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
