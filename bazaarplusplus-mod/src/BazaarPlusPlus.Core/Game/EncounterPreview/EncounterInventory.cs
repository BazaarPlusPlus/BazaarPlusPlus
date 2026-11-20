#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.EncounterPreview;

public sealed class EncounterInventoryCard
{
    public EncounterInventoryCard(Guid templateId, EffectiveCardTags tags)
        : this(templateId, tags.EncounterTagNames()) { }

    public EncounterInventoryCard(Guid templateId, IReadOnlyCollection<string> tagNames)
    {
        TemplateId = templateId;
        TagNames = tagNames;
    }

    public Guid TemplateId { get; }

    public IReadOnlyCollection<string> TagNames { get; }
}

// Keep one entry per owned card so duplicate copies and per-card tag conditions count correctly.
public sealed class EncounterInventory
{
    private readonly IReadOnlyList<EncounterInventoryCard> _cards;

    public EncounterInventory(IReadOnlyList<EncounterInventoryCard> cards)
    {
        _cards = cards;
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
