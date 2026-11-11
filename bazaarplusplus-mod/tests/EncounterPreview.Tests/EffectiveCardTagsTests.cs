using BazaarPlusPlus.Core.Cards;
using Xunit;

namespace EncounterPreview.Tests;

public sealed class EffectiveCardTagsTests
{
    [Fact]
    public void Empty_sources_keep_combat_projections_absent()
    {
        var tags = new EffectiveCardTags(null, null, null, null, null, null);

        Assert.Null(tags.Tags);
        Assert.Null(tags.HiddenTags);
        Assert.Equal(
            0,
            new EncounterInventory([
                new EncounterInventoryCard(Guid.NewGuid(), tags),
            ]).CountMatchingTags(
                [
                    [nameof(CardTag.Toy)],
                ],
                "Any"
            )
        );
    }

    [Fact]
    public void Runtime_only_public_tags_reach_encounter_count()
    {
        var tags = new EffectiveCardTags([CardTag.Toy], null, null, null, null, null);

        Assert.Equal(1, Count(tags, nameof(CardTag.Toy)));
        Assert.Single(tags.Tags!);
    }

    [Fact]
    public void Template_only_hidden_tags_reach_encounter_count()
    {
        var tags = new EffectiveCardTags(null, null, null, null, [CardHiddenTag.Freeze], null);

        Assert.Equal(1, Count(tags, nameof(CardHiddenTag.Freeze)));
        Assert.Single(tags.HiddenTags!);
    }

    [Fact]
    public void Confirmed_enchantment_tags_reach_encounter_count()
    {
        var tags = new EffectiveCardTags(
            null,
            null,
            [CardTag.Toy],
            null,
            null,
            [CardHiddenTag.Freeze]
        );

        Assert.Equal(1, Count(tags, nameof(CardTag.Toy)));
        Assert.Equal(1, Count(tags, nameof(CardHiddenTag.Freeze)));
    }

    [Fact]
    public void Three_sources_of_public_and_hidden_tags_count_once_per_card()
    {
        var tags = new EffectiveCardTags(
            [CardTag.Toy],
            [CardTag.Toy],
            [CardTag.Toy],
            [CardHiddenTag.Freeze],
            [CardHiddenTag.Freeze],
            [CardHiddenTag.Freeze]
        );
        var inventory = new EncounterInventory([new EncounterInventoryCard(Guid.NewGuid(), tags)]);

        Assert.Single(tags.Tags!);
        Assert.Single(tags.HiddenTags!);
        Assert.Equal(
            1,
            inventory.CountMatchingTags(
                [
                    [nameof(CardTag.Toy)],
                    [nameof(CardHiddenTag.Freeze)],
                ],
                "All"
            )
        );
    }

    [Fact]
    public void Matching_counts_cards_and_deduplicates_each_cards_sources()
    {
        var first = new EffectiveCardTags([CardTag.Toy], [CardTag.Toy], null, null, null, null);
        var second = new EffectiveCardTags(null, [CardTag.Toy], null, null, null, null);
        var inventory = new EncounterInventory([
            new EncounterInventoryCard(Guid.NewGuid(), first),
            new EncounterInventoryCard(Guid.NewGuid(), second),
        ]);

        Assert.Equal(
            2,
            inventory.CountMatchingTags(
                [
                    [nameof(CardTag.Toy)],
                ],
                "Any"
            )
        );
    }

    private static int Count(EffectiveCardTags tags, string name) =>
        new EncounterInventory([
            new EncounterInventoryCard(Guid.NewGuid(), tags),
        ]).CountMatchingTags(
            [
                [name],
            ],
            "Any"
        );
}
