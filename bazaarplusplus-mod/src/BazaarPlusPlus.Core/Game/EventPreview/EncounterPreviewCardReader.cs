#nullable enable
using BazaarPlusPlus.Core.Cards;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

/// <summary>
/// A static-data card's preview plans, read off its JSON as the game stores it: the Android host
/// reads the static-data rows, and the desktop serializes each typed template with the game's own
/// serializer settings into the same JSON. <paramref name="normalize"/> maps a native enum value
/// to its member name, as <see cref="EncounterChoicePlanParser"/> takes it; <paramref name="hero"/>
/// maps a native hero name to its facet, null when it has none.
/// </summary>
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

    /// <summary>
    /// The card's template plan: its kind, heroes, names and texts, and, for an event or an
    /// encounter step, the reward it deals. Null when the card has no id.
    /// </summary>
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

    /// <summary>
    /// The event's plan: its random outcomes, or else its choices, its choice limit, and, for a
    /// merchant, the quality it sells. Null when the card has no id.
    /// </summary>
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
