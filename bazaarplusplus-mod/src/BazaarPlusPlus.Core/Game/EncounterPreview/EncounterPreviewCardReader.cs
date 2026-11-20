#nullable enable
using BazaarPlusPlus.Core.Cards;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EncounterPreview;

/// <summary>Both hosts use the game's static-data JSON shape. The desktop gets it by serializing typed templates with the game's settings.</summary>
public sealed class EncounterPreviewCardReader(
    Func<string, string?, string?> normalize,
    Func<string, CardHero?> hero
)
{
    private readonly EncounterChoicePlanParser _choices = new(normalize);
    private readonly EncounterRewardFilterParser _rewards = new(normalize);

    public EncounterPreviewTemplateKind Classify(string? type) =>
        (normalize("CardType", type) ?? type) switch
        {
            "EventEncounter" => EncounterPreviewTemplateKind.Event,
            "EncounterStep" => EncounterPreviewTemplateKind.EncounterStep,
            "CombatEncounter" => EncounterPreviewTemplateKind.CombatEncounter,
            "Skill" => EncounterPreviewTemplateKind.Skill,
            "Item" => EncounterPreviewTemplateKind.Item,
            _ => EncounterPreviewTemplateKind.Other,
        };

    public EncounterPreviewTemplatePlan? ReadTemplate(
        JObject card,
        IReadOnlyDictionary<string, EncounterPreviewAbilityValue>? abilityValues = null
    )
    {
        if (!Guid.TryParse(card["Id"]?.ToString(), out var id))
            return null;
        var kind = Classify(card["Type"]?.ToString());
        var heroes = new List<CardHero>();
        if (card["Heroes"] is JArray names)
            foreach (var name in names)
                if (hero(name.ToString()) is { } facet && !heroes.Contains(facet))
                    heroes.Add(facet);
        return new EncounterPreviewTemplatePlan(
            id,
            kind,
            heroes,
            card["InternalName"]?.ToString(),
            Text(card, "Title"),
            Text(card, "Description"),
            abilityValues,
            kind is EncounterPreviewTemplateKind.Event or EncounterPreviewTemplateKind.EncounterStep
                ? _rewards.TryParse(card)
                : null
        );
    }

    public EncounterPreviewEventPlan? ReadEvent(JObject card)
    {
        if (!Guid.TryParse(card["Id"]?.ToString(), out var id))
            return null;
        var random = _choices.TryParseEventOutcomeGroups(card, out var outcomes);
        return new EncounterPreviewEventPlan(
            id,
            random,
            IsMerchant(card) ? _choices.TryParseMerchantTierRule(card) : null,
            _choices.TryParseEventChoiceLimit(card),
            outcomes,
            random
                ? Array.Empty<EncounterChoiceGroupData>()
                : _choices.TryParseEventChoiceGroups(card)
        );
    }

    private bool IsMerchant(JObject card) =>
        card["Tags"] is JArray tags
        && tags.Any(tag => (normalize("Tag", tag.ToString()) ?? tag.ToString()) == "Merchant");

    private static EncounterPreviewLocalizedText Text(JObject card, string key) =>
        new(
            card.SelectToken("Localization." + key + ".Key")?.ToString(),
            card.SelectToken("Localization." + key + ".Text")?.ToString()
        );
}
