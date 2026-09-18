using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using Xunit;

namespace EncounterTooltip.Tests;

public class CollectionLocalizationResolverTests
{
    [Fact]
    public void Mod_accessor_resolves_the_values_modifier_and_live_total_is_dropped()
    {
        // Gourmet Kitchen / Golden Harvest: the value is a live-computed card count
        // ("{ability.0}") multiplied by a fixed modifier ("{ability.0.mod}").
        var template = Template(
            "Gain {ability.0.mod} Gold for each Food or Tool you have (including Stash) [{ability.0}]",
            new TActionPlayerModifyAttribute
            {
                Value = new TReferenceValueCardCount
                {
                    Modifier = new TValueModifier { Value = new TFixedValue { Value = 1f } },
                },
            }
        );

        var description = CollectionLocalizationResolver.ResolveDescription(template);

        Assert.Equal("Gain 1 Gold for each Food or Tool you have (including Stash)", description);
    }

    [Fact]
    public void Fixed_value_placeholders_still_resolve()
    {
        var template = Template(
            "Gain {ability.0} Gold.",
            new TActionPlayerModifyAttribute
            {
                AttributeType = EPlayerAttributeType.Gold,
                Value = new TFixedValue { Value = 10f },
            }
        );

        var description = CollectionLocalizationResolver.ResolveDescription(template);

        Assert.Equal("Gain 10 Gold.", description);
    }

    private static TCardEncounterStep Template(string description, TActionBase action)
    {
        return new TCardEncounterStep
        {
            Localization = new TCardLocalization
            {
                Description = new TLocalizableText { Text = description },
            },
            Abilities = new() { ["0"] = new TCardAbility { Action = action } },
        };
    }
}
