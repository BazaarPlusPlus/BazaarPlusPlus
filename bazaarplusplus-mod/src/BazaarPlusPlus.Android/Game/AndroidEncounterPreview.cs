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

/// <summary>Reads encounter choices off-thread; resolves current native descriptions only on inspection.</summary>
internal sealed class AndroidEncounterPreview(BppSettings settings)
{
    private IntPtr _source;
    private Task<EncounterPreviewSnapshot>? _load;
    private EncounterPreviewSnapshot? _snapshot;
    private static readonly EncounterChoicePlanParser Parser = new(Normalize, (_, _, _, _) => null);

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
            _snapshot = null;
            _load = Task.Run(() => Read(path));
        }
        if (_load?.IsCompleted == true)
        {
            if (_load.IsCompletedSuccessfully)
            {
                _snapshot = _load.Result;
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.encounter_preview.ready"),
                    ("events", _snapshot.EventCount)
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

    public string? Render(CardTooltipController controller)
    {
        if (!settings.EnableDetailedPreviewConfig.Value || Data.IsInCombat || _snapshot == null)
            return null;
        var card = controller.CurrentCard;
        if (
            card == null
            || card.Type != ECardType.EventEncounter
            || !Guid.TryParse(card.TemplateId.ToString(), out var id)
            || !_snapshot.TryGetEvent(id, out var plan)
        )
            return null;
        var ids = plan
            .ChoiceGroups.SelectMany(group => group.Members.Select(member => member.TemplateId))
            .Concat(plan.OutcomeGroups.SelectMany(group => group.Ids))
            .Append(id)
            .Distinct();
        var templates = new List<EncounterPreviewTemplatePlan>();
        foreach (var templateId in ids)
        {
            if (!_snapshot.TryGetTemplate(templateId, out var template))
                continue;
            var title = AndroidCardTemplateText.Title(templateId) ?? template.Title.FallbackText;
            var description =
                template.Kind == EncounterPreviewTemplateKind.CombatEncounter
                    ? template.Description.FallbackText
                    : AndroidCardTemplateText.Description(templateId)
                        ?? template.Description.FallbackText;
            templates.Add(
                new(
                    templateId,
                    template.Kind,
                    template.Heroes,
                    template.InternalName,
                    new(null, title),
                    new(null, description),
                    null,
                    template.RewardFilter
                )
            );
        }
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
        return option == null ? null : EncounterPreviewTextFormatter.Build(option);
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

    internal static EncounterPreviewSnapshot Read(string path)
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
                    if (
                        Enum.TryParse<EHero>(name.ToString(), out var native)
                        && AndroidCardFacets.Hero.TryToFacet(native, out var hero)
                    )
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
        return new(events, templates);
    }

    private static string? Normalize(string kind, string? raw) =>
        kind switch
        {
            "SelectionMethod" => Name<ESpawnSelectionMethod>(raw),
            "Comparison" => Name<EComparisonOperator>(raw),
            "ListComparison" => Name<EListComparisonOperator>(raw),
            "HiddenTag" => Name<EHiddenTag>(raw),
            "Tag" => Name<ECardTag>(raw),
            _ => null,
        };

    private static string? Name<T>(string? raw)
        where T : struct, Enum =>
        Enum.TryParse<T>(raw, true, out var value) ? value.ToString() : null;
}
