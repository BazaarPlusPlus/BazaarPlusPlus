#nullable enable
namespace BazaarPlusPlus.Game.RandomCosmetics;

/// <summary>
/// One category's Random Cosmetic Pool for one hero: the cosmetics the game's Randomize
/// draws from at run start. Candidates are the non-default cosmetics the game draws from
/// itself; a default cosmetic is never a candidate. The player saves what to leave out, so a
/// cosmetic acquired later joins the pool. While no current candidate is left out, the pool is
/// every candidate and the game's own pick stands.
/// </summary>
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

    /// <summary>
    /// <paramref name="candidateIds"/> are the current non-default cosmetics in display order;
    /// <paramref name="excludedIds"/> are the saved ids to leave out, or null when none are saved.
    /// </summary>
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

    /// <summary>True when the pool leaves out a current candidate, so it replaces the game's pick.</summary>
    public bool OverridesGamePick => Members.Count < _candidates.Length;

    public bool Contains(string? id) => id != null && Members.Contains(id, StringComparer.Ordinal);

    /// <summary>
    /// The ids to save after a click on <paramref name="id"/>, or null when the click changes
    /// nothing: the id is not a candidate, or it is the last member. Saved ids that are not
    /// current candidates (a cosmetic that is unavailable now) stay saved.
    /// </summary>
    public IReadOnlyList<string>? Toggle(string? id)
    {
        if (id == null || !_candidateSet.Contains(id))
            return null;

        // Start from what the player sees: the fallback shows every candidate as a member.
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

    /// <summary>
    /// The cosmetic to equip instead of the game's pick, or null to keep the game's pick.
    /// <paramref name="nextIndex"/> returns a uniform index below its argument.
    /// </summary>
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
