#nullable enable
namespace BazaarPlusPlus.Game.EncounterPreview;

// Equal 0 makes a reward eligible only while the player lacks the matching card, as with Farai's Packages.
public sealed class EncounterCardRequirement
{
    public EncounterCardRequirement(
        IReadOnlyList<Guid> ids,
        IReadOnlyList<IReadOnlyList<string>> tagCandidateGroups,
        string tagOperator,
        string comparison,
        int amount
    )
    {
        Ids = ids;
        TagCandidateGroups = tagCandidateGroups;
        TagOperator = tagOperator;
        Comparison = comparison;
        Amount = amount;
    }

    public IReadOnlyList<Guid> Ids { get; }

    // A numeric tag token can map to several plausible enum names, all treated as one tag for comparison.
    public IReadOnlyList<IReadOnlyList<string>> TagCandidateGroups { get; }

    public string TagOperator { get; }

    public string Comparison { get; }

    public int Amount { get; }

    public bool Matches(EncounterInventory inventory)
    {
        var count =
            Ids.Count > 0
                ? inventory.CountMatchingTemplates(Ids)
                : inventory.CountMatchingTags(TagCandidateGroups, TagOperator);
        return Comparison switch
        {
            "Equal" => count == Amount,
            "NotEqual" => count != Amount,
            "GreaterThan" => count > Amount,
            "GreaterThanOrEqual" => count >= Amount,
            "LessThan" => count < Amount,
            "LessThanOrEqual" => count <= Amount,
            _ => true,
        };
    }
}
