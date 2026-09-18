using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EventPreviewLocalizationTests
{
    [Fact]
    public void Missing_static_value_uses_the_live_game_value()
    {
        var templateId = Guid.Parse("a7c9181b-6fa5-48b8-a870-90a9c054dc1d");
        var plan = new EncounterPreviewTemplatePlan(
            templateId,
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<EHero>(),
            "Heroic Encounters (Dabora)",
            new EncounterPreviewLocalizedText(null, "Heroic Encounters"),
            new EncounterPreviewLocalizedText(
                null,
                "Gain {ability.0.mod} Prestige for each Player fight you have lost this run [{ability.0}]"
            ),
            new Dictionary<string, EncounterPreviewAbilityValue>
            {
                ["0.mod"] = new EncounterPreviewAbilityValue("2"),
            },
            rewardFilter: null
        );

        var description = EventPreviewLocalization.ResolveDescription(plan, ResolveLiveValue);

        Assert.Equal(
            "Gain 2 Prestige for each Player fight you have lost this run [6]",
            description
        );

        bool ResolveLiveValue(
            Guid requestedTemplateId,
            string effectId,
            bool isAura,
            out string valueText,
            out string? unit
        )
        {
            Assert.Equal(templateId, requestedTemplateId);
            Assert.Equal("0", effectId);
            Assert.False(isAura);
            valueText = "6";
            unit = null;
            return true;
        }
    }

    [Theory]
    [InlineData(EPlayerAttributeType.Experience, 3, "XP")]
    [InlineData(EPlayerAttributeType.Gold, 30, "Gold")]
    public void Shrouded_figure_aura_value_renders_in_event_preview(
        EPlayerAttributeType playerAttribute,
        int value,
        string unit
    )
    {
        var template = new TCardEncounterStep
        {
            Localization = new TCardLocalization
            {
                Description = new TLocalizableText
                {
                    Text = $"Pick a Chest containing up to {{aura.9}} {unit}",
                },
            },
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_0] = value,
            },
            Auras = new Dictionary<string, TCardAura>
            {
                ["9"] = new TCardAura
                {
                    Action = new TAuraActionPlayerModifyAttribute
                    {
                        AttributeType = playerAttribute,
                        Value = new TReferenceValueCardAttribute
                        {
                            AttributeType = ECardAttributeType.Custom_0,
                            Modifier = new TValueModifier
                            {
                                ModifyMode = EValueModifierMode.Multiply,
                                Value = new TFixedValue { Value = 1f },
                            },
                        },
                    },
                },
            },
        };
        var abilityValues = EventPreviewLocalization.CaptureAbilityValues(template);
        var plan = new EncounterPreviewTemplatePlan(
            template.Id,
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<EHero>(),
            template.InternalName,
            new EncounterPreviewLocalizedText(null, null),
            new EncounterPreviewLocalizedText(null, template.Localization.Description.Text),
            abilityValues,
            rewardFilter: null
        );

        var description = EventPreviewLocalization.ResolveDescription(plan);

        Assert.Equal($"Pick a Chest containing up to {value} {unit}", description);
        Assert.Equal(value.ToString(), abilityValues["aura.9"].ValueText);
        Assert.DoesNotContain("{aura.", description);
    }
}
