using Xunit;

namespace EventPreview.Tests;

public sealed class EncounterCardRequirementTests
{
    private static readonly Guid PackageId = Guid.Parse("b0000000-0000-0000-0000-000000000001");

    // Farai's delivery outcomes: the group only rolls while you do NOT own the
    // package ("Equal 0") — the exact inverse of a plain has-check.
    [Fact]
    public void Equal_zero_requirement_is_met_only_without_the_card()
    {
        var requirement = new EncounterCardRequirement(
            new[] { PackageId },
            Array.Empty<IReadOnlyList<string>>(),
            tagOperator: "Any",
            comparison: "Equal",
            amount: 0
        );

        Assert.True(requirement.Matches(Inventory()));
        Assert.False(requirement.Matches(Inventory(Card(PackageId))));
    }

    [Fact]
    public void Count_threshold_requirements_count_duplicates()
    {
        var requirement = new EncounterCardRequirement(
            new[] { PackageId },
            Array.Empty<IReadOnlyList<string>>(),
            tagOperator: "Any",
            comparison: "GreaterThanOrEqual",
            amount: 2
        );

        Assert.False(requirement.Matches(Inventory(Card(PackageId))));
        Assert.True(requirement.Matches(Inventory(Card(PackageId), Card(PackageId))));
    }

    [Fact]
    public void Tag_requirement_with_equal_zero_inverts_on_hidden_tag_ownership()
    {
        var requirement = new EncounterCardRequirement(
            Array.Empty<Guid>(),
            new IReadOnlyList<string>[] { new[] { "Package" } },
            tagOperator: "Any",
            comparison: "Equal",
            amount: 0
        );

        Assert.True(requirement.Matches(Inventory(Card(Guid.NewGuid(), "Food"))));
        Assert.False(requirement.Matches(Inventory(Card(Guid.NewGuid(), "Package"))));
    }

    private static EncounterInventory Inventory(params EncounterInventoryCard[] cards) =>
        new(cards.ToList());

    private static EncounterInventoryCard Card(Guid templateId, params string[] tags) =>
        new(templateId, new HashSet<string>(tags, StringComparer.Ordinal));
}
