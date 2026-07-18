#nullable enable
namespace BazaarPlusPlus.Game.EventPreview;

// One owned card in the inventory snapshot: template id plus the names of its tags
// (ECardTag + EHiddenTag, compared by name).
internal sealed class EncounterInventoryCard
{
    public EncounterInventoryCard(Guid templateId, IReadOnlyCollection<string> tagNames)
    {
        TemplateId = templateId;
        TagNames = tagNames;
    }

    public Guid TemplateId { get; }

    public IReadOnlyCollection<string> TagNames { get; }
}

// Snapshot of the player's current cards (hand + stash items and skills) used to
// evaluate encounter ownership prerequisites. Kept per-card so count comparisons
// ("Equal 0", "GreaterThanOrEqual 12") and per-card tag operators (Any/All/None)
// evaluate exactly rather than over a deduplicated union.
internal sealed class EncounterInventory
{
    private readonly IReadOnlyList<EncounterInventoryCard> _cards;

    public EncounterInventory(IReadOnlyList<EncounterInventoryCard> cards)
    {
        _cards = cards;
    }

    public bool OwnsTemplate(Guid templateId)
    {
        foreach (var card in _cards)
            if (card.TemplateId == templateId)
                return true;
        return false;
    }

    public bool OwnsAnyTemplate(IReadOnlyList<Guid> anyOfIds)
    {
        foreach (var id in anyOfIds)
            if (OwnsTemplate(id))
                return true;
        return false;
    }

    public int CountMatchingTemplates(IReadOnlyList<Guid> anyOfIds)
    {
        var count = 0;
        foreach (var card in _cards)
        foreach (var id in anyOfIds)
            if (card.TemplateId == id)
            {
                count++;
                break;
            }
        return count;
    }

    // Number of owned cards matching the tag conditional: each candidate group
    // stands for one tag token (any candidate name counts as that tag being
    // present); the operator combines the tokens per card.
    public int CountMatchingTags(
        IReadOnlyList<IReadOnlyList<string>> tagCandidateGroups,
        string tagOperator
    )
    {
        if (tagCandidateGroups.Count == 0)
            return 0;

        var count = 0;
        foreach (var card in _cards)
        {
            var matchedGroups = 0;
            foreach (var group in tagCandidateGroups)
                if (CardHasAnyTag(card, group))
                    matchedGroups++;

            var matches = tagOperator switch
            {
                "All" => matchedGroups == tagCandidateGroups.Count,
                "None" => matchedGroups == 0,
                _ => matchedGroups > 0,
            };
            if (matches)
                count++;
        }
        return count;
    }

    private static bool CardHasAnyTag(EncounterInventoryCard card, IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        foreach (var tag in card.TagNames)
            if (string.Equals(tag, candidate, StringComparison.Ordinal))
                return true;
        return false;
    }
}
