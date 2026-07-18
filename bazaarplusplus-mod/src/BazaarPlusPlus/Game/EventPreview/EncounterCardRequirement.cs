#nullable enable
namespace BazaarPlusPlus.Game.EventPreview;

// One TPrerequisiteCardCount: the number of owned cards matching the conditional is
// compared against Amount. The dominant shape in random-outcome groups is
// "Equal 0" (the outcome only rolls while you do NOT own the card — e.g. Farai's
// Package results), so ownership can never be read as plain "must have";
// choice steps also use count thresholds ("GreaterThanOrEqual 12").
internal sealed class EncounterCardRequirement
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

    // Any-of card ids ("Powder Keg or the Big One") — mutually exclusive with tags.
    public IReadOnlyList<Guid> Ids { get; }

    // One group per tag token of the conditional. Each group lists the candidate
    // names for that token: a single name when the source spelled it out, or the
    // plausible enum names when a runtime-serialized token only carried the number.
    public IReadOnlyList<IReadOnlyList<string>> TagCandidateGroups { get; }

    // EListComparisonOperator name (Any/All/None) applied over the tag groups.
    public string TagOperator { get; }

    // EComparisonOperator name applied to the matching-card count.
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
