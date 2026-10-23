using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarPlusPlus.Core.Cards;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EventPreviewGameTextTests
{
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
        var abilityValues = EventPreviewGameText.CaptureAbilityValues(template);
        var plan = new EncounterPreviewTemplatePlan(
            template.Id,
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<CardHero>(),
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
