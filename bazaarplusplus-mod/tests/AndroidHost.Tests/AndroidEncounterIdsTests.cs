using BazaarPlusPlus.Android.GameInterop;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The snapshot the touch Collection opens on, by the rules of the desktop's
/// <c>EncounterStateProbe</c> (#727): same game state, same source on both hosts.
/// </summary>
public sealed class AndroidEncounterIdsTests
{
    private static readonly Guid Merchant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Event = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Direct = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void A_selection_screen_resolves_its_entries_in_the_selection_order()
    {
        var snapshot = AndroidEncounterIds.Build(
            null,
            isChoiceState: true,
            isSelectionState: true,
            ["ped_b", "", Direct.ToString(), "ped_unknown", "ped_a"],
            new Dictionary<string, Guid> { ["ped_a"] = Merchant, ["ped_b"] = Event }
        );

        Assert.True(snapshot.IsChoiceState);
        Assert.True(snapshot.IsSelectionState);
        Assert.Equal(
            ["ped_b", Direct.ToString(), "ped_unknown", "ped_a"],
            snapshot.ChoiceSelectionEntryIds
        );
        Assert.Equal([Event, Direct, Merchant], snapshot.ChoiceSelectionTemplateIds);
    }

    [Fact]
    public void Another_screen_keeps_the_current_encounter_and_reads_no_selection()
    {
        var snapshot = AndroidEncounterIds.Build(
            Merchant.ToString(),
            isChoiceState: false,
            isSelectionState: false,
            ["ped_a"],
            new Dictionary<string, Guid> { ["ped_a"] = Event }
        );

        Assert.Equal(Merchant, snapshot.CurrentEncounterTemplateId);
        Assert.False(snapshot.IsSelectionState);
        Assert.Empty(snapshot.ChoiceSelectionEntryIds);
        Assert.Empty(snapshot.ChoiceSelectionTemplateIds);
    }

    [Fact]
    public void An_instance_id_as_the_current_encounter_has_no_template_id()
    {
        var snapshot = AndroidEncounterIds.Build(
            "enc_live",
            isChoiceState: false,
            isSelectionState: true,
            [],
            new Dictionary<string, Guid>()
        );

        Assert.Equal("enc_live", snapshot.CurrentEncounterId);
        Assert.Null(snapshot.CurrentEncounterTemplateId);
    }
}
