using BazaarPlusPlus.Core.Cards;
using Xunit;

namespace EventPreview.Tests;

public sealed class EventPreviewLocalizationTests : IDisposable
{
    public void Dispose() => EventPreviewLocalization.TextLocalizer = null;

    [Fact]
    public void Missing_static_value_uses_the_live_game_value()
    {
        var templateId = Guid.Parse("a7c9181b-6fa5-48b8-a870-90a9c054dc1d");
        var plan = new EncounterPreviewTemplatePlan(
            templateId,
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<CardHero>(),
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

    [Fact]
    public void Installed_text_localizer_wins_over_the_english_fallback()
    {
        var plan = TitledPlan(new EncounterPreviewLocalizedText("title.key", "Heroic Encounters"));
        EventPreviewLocalization.TextLocalizer = text =>
            text.Key == "title.key" ? "英雄遭遇" : null;

        Assert.Equal("英雄遭遇", EventPreviewLocalization.ResolveTitle(plan));
    }

    [Fact]
    public void Failing_or_empty_text_localizer_falls_back_to_the_english_text_then_the_key()
    {
        EventPreviewLocalization.TextLocalizer = _ =>
            throw new InvalidOperationException("localization services are not ready");
        Assert.Equal(
            "Heroic Encounters",
            EventPreviewLocalization.ResolveTitle(
                TitledPlan(new EncounterPreviewLocalizedText("title.key", "Heroic Encounters"))
            )
        );

        EventPreviewLocalization.TextLocalizer = _ => " ";
        Assert.Equal(
            "title.key",
            EventPreviewLocalization.ResolveTitle(
                TitledPlan(new EncounterPreviewLocalizedText("title.key", null))
            )
        );
    }

    private static EncounterPreviewTemplatePlan TitledPlan(EncounterPreviewLocalizedText title) =>
        new(
            Guid.Parse("a7c9181b-6fa5-48b8-a870-90a9c054dc1e"),
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<CardHero>(),
            "Heroic Encounters (Dabora)",
            title,
            description: null,
            abilityValues: null,
            rewardFilter: null
        );
}
