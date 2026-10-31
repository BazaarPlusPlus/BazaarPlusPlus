using System.Text;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Spawning;
using Il2CppTheBazaar;
using Il2CppTheBazaar.UI.Tooltips;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Android.Game;

/// <summary>
/// Reads encounter choices and merchant quality rules off-thread; resolves current native
/// descriptions only on inspection. A merchant shows the quality it sells, as on the desktop: its
/// pinned tier, or the day's tier odds (<see cref="AndroidDayTiers"/>).
/// </summary>
internal sealed class AndroidEncounterPreview(BppSettings settings)
{
    private IntPtr _source;
    private Task<AndroidEncounterPlans>? _load;
    private AndroidEncounterPlans? _plans;
    private static readonly EncounterChoicePlanParser Parser = new(Normalize, (_, _, _, _) => null);

    /// <summary>The Detailed Preview switch.</summary>
    public bool Enabled => settings.EnableDetailedPreviewConfig.Value;

    public void Tick()
    {
        if (!settings.EnableDetailedPreviewConfig.Value || !Data.IsManagerCreated())
            return;
        var manager = Data.GetStatic();
        if (manager == null)
            return;
        // The path is marshalled only for a new manager: each read of the IL2CPP field allocates
        // a managed string. An empty path leaves the source unset, so the next frame retries.
        if (_source != manager.Pointer)
        {
            var path = manager._dbPath;
            if (string.IsNullOrWhiteSpace(path))
                return;
            _source = manager.Pointer;
            _plans = null;
            _load = Task.Run(() => Read(path));
        }
        if (_load?.IsCompleted == true)
        {
            if (_load.IsCompletedSuccessfully)
            {
                _plans = _load.Result;
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.encounter_preview.ready"),
                    ("events", _plans.Snapshot.EventCount),
                    ("merchants", _plans.Merchants.Count),
                    ("level_ups", _plans.Snapshot.LevelUpCount)
                );
            }
            else
                BppLog.WarnEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.encounter_preview.failed"),
                    _load.Exception!,
                    ("reason_code", "database_read")
                );
            _load = null;
        }
    }

    /// <summary>
    /// What leveling up from <paramref name="currentLevel"/> gives <paramref name="hero"/>, the
    /// desktop's level-up text over the same Core formatter: max health, board slots below level
    /// four, and the rewards to choose from, with each reward's text as its Android tooltip
    /// renders it. Null when the switch is off, in combat (as the desktop's
    /// <c>EncounterPreviewModule</c> leaves the native strip alone in a fight), the plans are not
    /// read yet, or the level has none.
    /// </summary>
    public string? RenderLevelUp(int currentLevel, CardHero? hero)
    {
        if (!settings.EnableDetailedPreviewConfig.Value || Data.IsInCombat || _plans == null)
            return null;
        var loaded = _plans.Snapshot;
        if (!loaded.TryGetLevelUp(currentLevel, out var plan))
            return null;
        return LevelUpPreviewTextFormatter.Build(
            plan,
            id => loaded.TryGetTemplate(id, out var template) ? Localized(template) : null,
            hero,
            ColorKeywords,
            currentLevel
        );
    }

    // The desktop colors keywords in the plain lines it builds. A description the game's tooltip
    // rendered already carries its colors and sprites, so it is left as it is.
    private static string ColorKeywords(string text)
    {
        if (text.Contains('<'))
            return text;
        try
        {
            return Data.TooltipTypography?.ColorKeywords(
                    text,
                    false,
                    false,
                    new Il2CppSystem.Nullable<ECardAttributeType>()
                ) ?? text;
        }
        catch (Exception)
        {
            return text;
        }
    }

    public string? Render(CardTooltipController controller)
    {
        if (!settings.EnableDetailedPreviewConfig.Value || Data.IsInCombat || _plans == null)
            return null;
        var card = controller.CurrentCard;
        if (
            card == null
            || card.Type != ECardType.EventEncounter
            || !Guid.TryParse(card.TemplateId.ToString(), out var id)
        )
            return null;
        if (_plans.Merchants.TryGetValue(id, out var merchant))
        {
            if (!merchant.IsSupported)
                return null;
            var quality = EncounterPreviewTextFormatter.BuildQualityLine(
                merchant.UsesDayDistribution ? AndroidDayTiers.Current() : null,
                merchant.FixedTier
            );
            return string.IsNullOrWhiteSpace(quality) ? null : quality;
        }
        var loaded = _plans.Snapshot;
        if (!loaded.TryGetEvent(id, out var plan))
            return null;
        var ids = plan
            .ChoiceGroups.SelectMany(group => group.Members.Select(member => member.TemplateId))
            .Concat(plan.OutcomeGroups.SelectMany(group => group.Ids))
            .Append(id)
            .Distinct();
        var templates = new List<EncounterPreviewTemplatePlan>();
        foreach (var templateId in ids)
            if (loaded.TryGetTemplate(templateId, out var template))
                templates.Add(Localized(template));
        var snapshot = new EncounterPreviewSnapshot([plan], templates);
        var run = Data.Run;
        CardHero? hero =
            run?.Player is { } player
            && AndroidCardFacets.Hero.TryToFacet(player.Hero, out var facet)
                ? facet
                : null;
        var option = EncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            hero,
            Inventory(),
            run == null ? null : (int)run.Day
        );
        return option == null
            ? null
            : EncounterPreviewTextFormatter.Build(option, dayTiers: AndroidDayTiers.Current());
    }

    // A static template with the title and description the current language and the game's
    // tooltip give it.
    private static EncounterPreviewTemplatePlan Localized(EncounterPreviewTemplatePlan template)
    {
        var id = template.TemplateId;
        var title = AndroidCardTemplateText.Title(id) ?? template.Title.FallbackText;
        var description =
            template.Kind == EncounterPreviewTemplateKind.CombatEncounter
                ? template.Description.FallbackText
                : AndroidCardTemplateText.Description(id) ?? template.Description.FallbackText;
        return new(
            id,
            template.Kind,
            template.Heroes,
            template.InternalName,
            new(null, title),
            new(null, description),
            null,
            template.RewardFilter
        );
    }

    private static EncounterInventory? Inventory()
    {
        var player = Data.Run?.Player;
        if (player == null)
            return null;
        var cards = new List<EncounterInventoryCard>();
        var seen = new HashSet<IntPtr>();
        void Add(Il2CppBazaarGameClient.Domain.Models.Cards.Card card)
        {
            if (!seen.Add(card.Pointer) || !Guid.TryParse(card.TemplateId.ToString(), out var id))
                return;
            var tags = new List<string>();
            if (card.Tags != null)
                foreach (var tag in card.Tags)
                    tags.Add(tag.ToString());
            if (card.HiddenTags != null)
                foreach (var tag in card.HiddenTags)
                    tags.Add(tag.ToString());
            cards.Add(new(id, tags));
        }
        foreach (var card in Data.Entities.Values)
            if (
                card != null
                && card.Owner?.Pointer == player.Pointer
                && AndroidGameIds.Unboxed(() => card.Section)
                    is EInventorySection.Hand
                        or EInventorySection.Stash
            )
                Add(card);
        if (player.Skills != null)
            foreach (var skill in player.Skills)
                if (skill != null)
                    Add(skill);
        return new(cards);
    }

    internal static AndroidEncounterPlans Read(string path)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString()
        );
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Data FROM cards";
        using var reader = command.ExecuteReader();
        var events = new List<EncounterPreviewEventPlan>();
        var templates = new List<EncounterPreviewTemplatePlan>();
        var merchants = new Dictionary<Guid, EncounterMerchantTierRule>();
        while (reader.Read())
        {
            var card = JObject.Parse(Encoding.UTF8.GetString((byte[])reader.GetValue(0)));
            if (!Guid.TryParse(card["Id"]?.ToString(), out var id))
                continue;
            var type = card["Type"]?.ToString();
            var kind = type switch
            {
                "EventEncounter" => EncounterPreviewTemplateKind.Event,
                "EncounterStep" => EncounterPreviewTemplateKind.EncounterStep,
                "CombatEncounter" => EncounterPreviewTemplateKind.CombatEncounter,
                "Skill" => EncounterPreviewTemplateKind.Skill,
                "Item" => EncounterPreviewTemplateKind.Item,
                _ => EncounterPreviewTemplateKind.Other,
            };
            var heroes = new List<CardHero>();
            if (card["Heroes"] is JArray heroNames)
                foreach (var name in heroNames)
                    if (Hero(name.ToString()) is { } hero)
                        heroes.Add(hero);
            EncounterPreviewLocalizedText Text(string key) =>
                new(
                    card.SelectToken("Localization." + key + ".Key")?.ToString(),
                    card.SelectToken("Localization." + key + ".Text")?.ToString()
                );
            templates.Add(
                new(
                    id,
                    kind,
                    heroes,
                    card["InternalName"]?.ToString(),
                    Text("Title"),
                    Text("Description"),
                    null,
                    null
                )
            );
            if (kind != EncounterPreviewTemplateKind.Event)
                continue;
            var random = Parser.TryParseEventOutcomeGroups(card, out var outcomes);
            var merchant =
                card["Tags"] is JArray tags && tags.Any(tag => tag.ToString() == "Merchant");
            if (merchant)
                merchants[id] = Parser.TryParseMerchantTierRule(card);
            events.Add(
                new(
                    id,
                    random,
                    merchant,
                    Parser.TryParseEventChoiceLimit(card),
                    outcomes,
                    random ? [] : Parser.TryParseEventChoiceGroups(card)
                )
            );
        }
        reader.Close();
        command.CommandText = "SELECT Id, Data FROM level_ups";
        using var levelReader = command.ExecuteReader();
        var levelUps = new List<LevelUpPreviewPlan>();
        while (levelReader.Read())
            if (
                Parser.TryParseLevelUp(
                    levelReader.GetInt32(0),
                    JObject.Parse(Encoding.UTF8.GetString((byte[])levelReader.GetValue(1))),
                    Hero
                ) is
                { } levelUp
            )
                levelUps.Add(levelUp);
        return new(new EncounterPreviewSnapshot(events, templates, levelUps), merchants);
    }

    private static CardHero? Hero(string name) =>
        Enum.TryParse<EHero>(name, out var native)
        && AndroidCardFacets.Hero.TryToFacet(native, out var hero)
            ? hero
            : null;

    private static string? Normalize(string kind, string? raw) =>
        kind switch
        {
            "SelectionMethod" => Name<ESpawnSelectionMethod>(raw),
            "Comparison" => Name<EComparisonOperator>(raw),
            "ListComparison" => Name<EListComparisonOperator>(raw),
            "HiddenTag" => Name<EHiddenTag>(raw),
            "Tag" => Name<ECardTag>(raw),
            "Tier" => Name<ETier>(raw),
            _ => null,
        };

    private static string? Name<T>(string? raw)
        where T : struct, Enum =>
        Enum.TryParse<T>(raw, true, out var value) ? value.ToString() : null;
}

/// <summary>The encounter plans read from one static-data source, and its merchants' quality rules.</summary>
internal sealed record AndroidEncounterPlans(
    EncounterPreviewSnapshot Snapshot,
    IReadOnlyDictionary<Guid, EncounterMerchantTierRule> Merchants
);
